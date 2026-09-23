using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Common.Services;

/// <summary>
/// Service for downloading files with progress reporting and hash verification.
/// </summary>
public class DownloadService(
    ILogger<DownloadService> logger,
    HttpClient httpClient,
    IFileHashProvider hashProvider,
    IDownloadUrlValidator? urlValidator = null) : IDownloadService
{
    /// <inheritdoc/>
    public async Task<DownloadResult> DownloadFileAsync(
        DownloadConfiguration configuration,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var destDir = Path.GetDirectoryName(configuration.DestinationPath);
        if (!string.IsNullOrWhiteSpace(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        return await DownloadWithRetryAsync(configuration, progress, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DownloadResult> DownloadFileAsync(
        Uri url,
        string destinationPath,
        string? expectedHash = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var configuration = new DownloadConfiguration
        {
            Url = url,
            DestinationPath = destinationPath,
            ExpectedHash = expectedHash,
        };

        return await DownloadFileAsync(configuration, progress, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return await hashProvider.ComputeFileHashAsync(filePath, cancellationToken);
    }

    private static HttpRequestMessage CreateRequest(DownloadConfiguration configuration, Uri url, long rangeStart = 0)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", configuration.UserAgent);
        foreach (var header in configuration.Headers)
        {
            request.Headers.Add(header.Key, header.Value);
        }

        if (rangeStart > 0 && request.Headers.Range == null)
        {
            request.Headers.Range = new RangeHeaderValue(rangeStart, null);
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
        DownloadConfiguration configuration,
        IDownloadUrlValidator validator,
        long rangeStart,
        CancellationToken cancellationToken)
    {
        if (!configuration.ValidateRedirectsManually)
        {
            using var request = CreateRequest(configuration, configuration.Url, rangeStart);
            return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }

        var validated = await SsrfSafeHttpHelper.SendWithValidatedRedirectsAsync(
            httpClient,
            uri => CreateRequest(configuration, uri, rangeStart),
            configuration.Url,
            DownloadDefaults.MaxRedirects,
            validator,
            cancellationToken);
        return validated.Response;
    }

    private async Task<DownloadResult> DownloadWithRetryAsync(
        DownloadConfiguration configuration,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        Exception? lastException;

        for (int attempt = 1; attempt <= configuration.MaxRetryAttempts; attempt++)
        {
            try
            {
                if (attempt > 1)
                {
                    await Task.Delay(configuration.RetryDelay, cancellationToken);
                }

                return await PerformDownloadAsync(configuration, progress, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt < configuration.MaxRetryAttempts)
                {
                    logger.LogWarning(ex, "Download attempt {Attempt} failed for {Url}, retrying...", attempt, configuration.Url);
                }
                else
                {
                    var errorMessage = $"Download failed after {configuration.MaxRetryAttempts} attempts";
                    if (lastException != null)
                    {
                        errorMessage += $": {lastException.Message}";
                    }

                    return DownloadResult.CreateFailure(errorMessage);
                }
            }
        }

        return DownloadResult.CreateFailure($"Download failed after {configuration.MaxRetryAttempts} attempts (unexpected error)");
    }

    private async Task<DownloadResult> PerformDownloadAsync(
        DownloadConfiguration configuration,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(configuration.DestinationPath);
        var stopwatch = Stopwatch.StartNew();
        var lastProgressReport = DateTime.UtcNow;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(configuration.Timeout);
        var validator = urlValidator ?? new DownloadUrlValidator();

        var destFileInfo = new FileInfo(configuration.DestinationPath);
        var existingBytes = (configuration.EnableResumption && destFileInfo.Exists) ? destFileInfo.Length : 0L;

        // If file already exists and hash verification passes, skip download immediately
        if (existingBytes > 0 && !string.IsNullOrWhiteSpace(configuration.ExpectedHash))
        {
            var existingHash = await hashProvider.ComputeFileHashAsync(configuration.DestinationPath, cancellationToken);
            if (string.Equals(existingHash, configuration.ExpectedHash, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("File {FilePath} already exists and matches expected hash; skipping download", configuration.DestinationPath);
                return DownloadResult.CreateSuccess(configuration.DestinationPath, existingBytes, TimeSpan.Zero, true);
            }
        }

        HttpResponseMessage response;
        bool isResumed = false;
        long totalBytes;
        long downloadedBytes = 0;

        if (existingBytes > 0)
        {
            response = await SendRequestAsync(configuration, validator, existingBytes, cts.Token);
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                isResumed = true;
                downloadedBytes = existingBytes;
                totalBytes = response.Content.Headers.ContentRange?.Length
                    ?? (existingBytes + (response.Content.Headers.ContentLength ?? 0));
            }
            else if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                response.Dispose();
                logger.LogWarning("Range {Range} not satisfiable for {Url}. Restarting download from scratch.", existingBytes, configuration.Url);
                try
                {
                    File.Delete(configuration.DestinationPath);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete file {FilePath} on 416 retry", configuration.DestinationPath);
                }

                existingBytes = 0;
                response = await SendRequestAsync(configuration, validator, 0, cts.Token);
                response.EnsureSuccessStatusCode();
                totalBytes = response.Content.Headers.ContentLength ?? 0;
            }
            else
            {
                response.EnsureSuccessStatusCode();
                totalBytes = response.Content.Headers.ContentLength ?? 0;
                existingBytes = 0;
            }
        }
        else
        {
            response = await SendRequestAsync(configuration, validator, 0, cts.Token);
            response.EnsureSuccessStatusCode();
            totalBytes = response.Content.Headers.ContentLength ?? 0;
        }

        var fileMode = isResumed ? FileMode.Append : FileMode.Create;
        var buffer = new byte[configuration.BufferSize];

        using (response)
        await using (var contentStream = await response.Content.ReadAsStreamAsync(cts.Token))
        await using (var fileStream = new FileStream(configuration.DestinationPath, fileMode, FileAccess.Write, FileShare.None, configuration.BufferSize, useAsync: true))
        {
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, cts.Token)) > 0)
            {
                // Reset inactivity / stall timeout countdown timer because data is actively arriving
                cts.CancelAfter(configuration.Timeout);

                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token);
                downloadedBytes += bytesRead;

                // Report progress at specified intervals
                var now = DateTime.UtcNow;
                if (progress != null && (now - lastProgressReport >= configuration.ProgressReportingInterval || downloadedBytes == totalBytes))
                {
                    var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                    var sessionBytes = downloadedBytes - existingBytes;
                    var speed = elapsedSeconds > 0 ? (long)(sessionBytes / elapsedSeconds) : 0;

                    progress.Report(new DownloadProgress(
                        downloadedBytes,
                        totalBytes,
                        fileName,
                        configuration.Url,
                        speed,
                        stopwatch.Elapsed));

                    lastProgressReport = now;
                }
            }
        }

        // Hash verification if required
        bool hashVerified = false;
        if (!string.IsNullOrWhiteSpace(configuration.ExpectedHash))
        {
            var actualHash = await hashProvider.ComputeFileHashAsync(configuration.DestinationPath, cancellationToken);
            hashVerified = string.Equals(actualHash, configuration.ExpectedHash, StringComparison.OrdinalIgnoreCase);
            if (!hashVerified)
            {
                try
                {
                    File.Delete(configuration.DestinationPath);
                }
                catch
                {
                    logger.LogWarning("Failed to delete corrupted file: {FilePath}", configuration.DestinationPath);
                }

                return DownloadResult.CreateFailure($"Hash verification failed. Expected: {configuration.ExpectedHash}, Actual: {actualHash}", downloadedBytes, stopwatch.Elapsed);
            }
        }

        return DownloadResult.CreateSuccess(configuration.DestinationPath, downloadedBytes, stopwatch.Elapsed, hashVerified);
    }
}
