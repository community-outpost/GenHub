using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
    private sealed record DownloadConnection(
        HttpResponseMessage Response,
        bool IsResumed,
        long ExistingBytes,
        long TotalBytes);

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
            if (string.Equals(header.Key, "ETag", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            request.Headers.Add(header.Key, header.Value);
        }

        if (rangeStart > 0 && request.Headers.Range == null)
        {
            request.Headers.Range = new RangeHeaderValue(rangeStart, null);
            if (request.Headers.IfRange == null
                && TryGetETagHeader(configuration, out var etag)
                && TryParseEntityTag(etag, out var parsedEtag))
            {
                request.Headers.IfRange = new RangeConditionHeaderValue(parsedEtag);
            }
        }

        return request;
    }

    private static bool TryGetETagHeader(DownloadConfiguration configuration, [NotNullWhen(true)] out string? etag)
    {
        if (configuration.Headers.TryGetValue("ETag", out etag) && !string.IsNullOrWhiteSpace(etag))
        {
            return true;
        }

        etag = null;
        return false;
    }

    private static bool TryParseEntityTag(string? raw, [NotNullWhen(true)] out EntityTagHeaderValue? entityTag)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            entityTag = null;
            return false;
        }

        if (EntityTagHeaderValue.TryParse(raw, out entityTag))
        {
            return true;
        }

        var trimmed = raw.Trim();
        if (EntityTagHeaderValue.TryParse($"\"{trimmed.Trim('\"')}\"", out entityTag))
        {
            return true;
        }

        entityTag = null;
        return false;
    }

    private static void ReportDownloadProgress(
        IProgress<DownloadProgress> progress,
        long downloadedBytes,
        long totalBytes,
        string fileName,
        Uri url,
        TimeSpan elapsed)
    {
        var elapsedSeconds = elapsed.TotalSeconds;
        var bytesPerSecond = elapsedSeconds > 0 ? (long)(downloadedBytes / elapsedSeconds) : 0L;

        var downloadProgress = new DownloadProgress(
            downloadedBytes,
            totalBytes,
            fileName,
            url,
            bytesPerSecond,
            elapsed);

        progress.Report(downloadProgress);
    }

    private static async Task<long> StreamContentToFileAsync(
        DownloadConnection connection,
        DownloadConfiguration configuration,
        IProgress<DownloadProgress>? progress,
        Stopwatch stopwatch,
        CancellationTokenSource cts)
    {
        var fileName = Path.GetFileName(configuration.DestinationPath);
        var fileMode = connection.IsResumed ? FileMode.Append : FileMode.Create;
        var buffer = new byte[configuration.BufferSize];
        var downloadedBytes = connection.ExistingBytes;
        var lastProgressReport = DateTime.UtcNow;

        await using var contentStream = await connection.Response.Content.ReadAsStreamAsync(cts.Token);
        await using var fileStream = new FileStream(configuration.DestinationPath, fileMode, FileAccess.Write, FileShare.None, configuration.BufferSize, useAsync: true);

        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, cts.Token)) > 0)
        {
            cts.CancelAfter(configuration.Timeout);
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token);
            downloadedBytes += bytesRead;

            var now = DateTime.UtcNow;
            if (progress != null && (now - lastProgressReport >= configuration.ProgressReportingInterval || downloadedBytes == connection.TotalBytes))
            {
                ReportDownloadProgress(progress, downloadedBytes, connection.TotalBytes, fileName, configuration.Url, stopwatch.Elapsed);
                lastProgressReport = now;
            }
        }

        if (connection.IsResumed && connection.TotalBytes > 0 && downloadedBytes < connection.TotalBytes)
        {
            throw new InvalidDataException($"Resumed download ended early: expected {connection.TotalBytes} bytes, got {downloadedBytes}.");
        }

        return downloadedBytes;
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
        Exception? lastException = null;

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
            }
        }

        var errorMessage = $"Download failed after {configuration.MaxRetryAttempts} attempts";
        if (lastException != null)
        {
            errorMessage += $": {lastException.Message}";
        }

        return DownloadResult.CreateFailure(errorMessage);
    }

    private async Task<DownloadResult> PerformDownloadAsync(
        DownloadConfiguration configuration,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var destFileInfo = new FileInfo(configuration.DestinationPath);
        if (destFileInfo.Exists && await TrySkipAlreadyCompletedDownloadAsync(configuration, cancellationToken))
        {
            return DownloadResult.CreateSuccess(configuration.DestinationPath, destFileInfo.Length, TimeSpan.Zero, true);
        }

        var existingBytes = (configuration.EnableResumption
            && destFileInfo.Exists
            && TryGetETagHeader(configuration, out var etag)
            && TryParseEntityTag(etag, out _))
            ? destFileInfo.Length
            : 0L;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(configuration.Timeout);
        var validator = urlValidator ?? new DownloadUrlValidator();

        var connection = await EstablishDownloadConnectionAsync(configuration, validator, existingBytes, cts);
        using (connection.Response)
        {
            var stopwatch = Stopwatch.StartNew();
            var downloadedBytes = await StreamContentToFileAsync(
                connection,
                configuration,
                progress,
                stopwatch,
                cts);
            stopwatch.Stop();

            return await FinalizeDownloadAsync(
                configuration,
                downloadedBytes,
                stopwatch.Elapsed,
                cancellationToken);
        }
    }

    private async Task<DownloadConnection> EstablishDownloadConnectionAsync(
        DownloadConfiguration configuration,
        IDownloadUrlValidator validator,
        long existingBytes,
        CancellationTokenSource cts)
    {
        if (existingBytes > 0)
        {
            var resumedConnection = await TryEstablishResumedConnectionAsync(configuration, validator, existingBytes, cts);
            if (resumedConnection != null)
            {
                return resumedConnection;
            }

            TryDeleteFile(configuration.DestinationPath);
        }

        var response = await SendRequestAsync(configuration, validator, 0, cts.Token);
        try
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            return new DownloadConnection(response, false, 0, totalBytes);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private async Task<DownloadConnection?> TryEstablishResumedConnectionAsync(
        DownloadConfiguration configuration,
        IDownloadUrlValidator validator,
        long existingBytes,
        CancellationTokenSource cts)
    {
        var response = await SendRequestAsync(configuration, validator, existingBytes, cts.Token);
        try
        {
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                var contentRange = response.Content.Headers.ContentRange;
                if (contentRange?.From == existingBytes)
                {
                    var totalBytes = contentRange.Length
                        ?? (existingBytes + (response.Content.Headers.ContentLength ?? 0));
                    return new DownloadConnection(response, true, existingBytes, totalBytes);
                }

                logger.LogWarning("Range {Range} mismatch on {Url}; expected {Expected}. Restarting download.", contentRange?.From, configuration.Url, existingBytes);
            }
            else if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                logger.LogWarning("Range {Range} not satisfiable for {Url}. Restarting download from scratch.", existingBytes, configuration.Url);
            }
            else if (response.IsSuccessStatusCode)
            {
                // Server responded 200 OK: ignored Range header and sent full content
                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                return new DownloadConnection(response, false, 0, totalBytes);
            }
            else
            {
                response.EnsureSuccessStatusCode();
            }
        }
        catch
        {
            response.Dispose();
            throw;
        }

        response.Dispose();
        return null;
    }

    private async Task<DownloadResult> FinalizeDownloadAsync(
        DownloadConfiguration configuration,
        long downloadedBytes,
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration.ExpectedHash))
        {
            return DownloadResult.CreateSuccess(configuration.DestinationPath, downloadedBytes, elapsed, false);
        }

        var actualHash = await hashProvider.ComputeFileHashAsync(configuration.DestinationPath, cancellationToken);
        var hashVerified = string.Equals(actualHash, configuration.ExpectedHash, StringComparison.OrdinalIgnoreCase);
        if (!hashVerified)
        {
            TryDeleteFile(configuration.DestinationPath);

            return DownloadResult.CreateFailure(
                $"Hash verification failed. Expected: {configuration.ExpectedHash}, Actual: {actualHash}",
                downloadedBytes,
                elapsed);
        }

        return DownloadResult.CreateSuccess(configuration.DestinationPath, downloadedBytes, elapsed, true);
    }

    private async Task<bool> TrySkipAlreadyCompletedDownloadAsync(
        DownloadConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration.ExpectedHash))
        {
            return false;
        }

        var existingHash = await hashProvider.ComputeFileHashAsync(configuration.DestinationPath, cancellationToken);
        if (string.Equals(existingHash, configuration.ExpectedHash, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("File {FilePath} already exists and matches expected hash; skipping download", configuration.DestinationPath);
            return true;
        }

        return false;
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete file {FilePath}", path);
        }
    }
}
