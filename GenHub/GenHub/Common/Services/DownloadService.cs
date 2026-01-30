using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.Services;

/// <summary>
/// Service for downloading files with progress reporting and hash verification.
/// </summary>
public class DownloadService(
    ILogger<DownloadService> logger,
    HttpClient httpClient,
    IFileHashProvider hashProvider) : IDownloadService
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
    public async Task<IDictionary<string, DownloadResult>> DownloadFilesAsync(
        IDictionary<Uri, string> files,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, DownloadResult>();

        // Limit concurrent downloads to avoid overwhelming the system
        // Limit concurrent downloads to avoid overwhelming the system
        using var semaphore = new SemaphoreSlim(DownloadDefaults.MaxConcurrentDownloads);

        var fileKeys = files.Keys.ToList();
        int completedFiles = 0;
        int totalFiles = fileKeys.Count;

        var tasks = fileKeys.Select(async uri =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                // Create file-specific progress to track individual download progress
                IProgress<DownloadProgress>? fileProgress = null;
                if (progress != null)
                {
                    fileProgress = new Progress<DownloadProgress>(dp =>
                    {
                        // Report overall progress including completed files
                        progress.Report(dp);
                    });
                }

                var result = await DownloadFileAsync(uri, files[uri], null, fileProgress, cancellationToken);

                // Update overall progress for completed files
                Interlocked.Increment(ref completedFiles);
                if (progress != null)
                {
                    progress.Report(new DownloadProgress(
                        completedFiles,
                        totalFiles,
                        "Batch Download",
                        new Uri("about:blank"),
                        0));
                }

                return new { Path = files[uri], Result = result };
            }
            finally
            {
                semaphore.Release();
            }
        });

        var downloadResults = await Task.WhenAll(tasks);
        foreach (var item in downloadResults)
        {
            results[item.Path] = item.Result;
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<OperationResult> DownloadFilesAsync(
        IEnumerable<ManifestFile> files,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        // Validate inputs
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        var downloadMap = new Dictionary<Uri, string>();
        foreach (var file in files)
        {
            if (!string.IsNullOrEmpty(file.DownloadUrl) && Uri.TryCreate(file.DownloadUrl, UriKind.Absolute, out var uri))
            {
                // Validate and sanitize RelativePath to prevent path traversal attacks
                var relativePath = file.RelativePath;

                // Check for path traversal attempts
                if (relativePath.Contains("..") || Path.IsPathRooted(relativePath))
                {
                    logger.LogWarning("Skipping file with invalid relative path: {RelativePath}", relativePath);
                    continue;
                }

                var destPath = Path.Combine(destinationDirectory, relativePath);

                // Ensure the resolved path is still within the destination directory
                var normalizedDest = Path.GetFullPath(destPath);
                var normalizedDestDir = Path.GetFullPath(destinationDirectory);
                if (!normalizedDest.StartsWith(normalizedDestDir, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning("Skipping file with path outside destination directory: {RelativePath}", relativePath);
                    continue;
                }

                downloadMap[uri] = destPath;
            }
        }

        if (downloadMap.Count == 0)
        {
            return OperationResult.CreateSuccess();
        }

        var results = await DownloadFilesAsync(downloadMap, null, cancellationToken);

        var failures = results.Values.Where(r => !r.Success).ToList();
        if (failures.Count != 0)
        {
            // Aggregate all failure messages for better debugging
            var allErrors = string.Join("; ", failures.Select(f => $"{f.FirstError}"));
            return OperationResult.CreateFailure($"Failed to download {failures.Count} file(s): {allErrors}");
        }

        return OperationResult.CreateSuccess();
    }

    /// <inheritdoc/>
    public async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return await hashProvider.ComputeFileHashAsync(filePath, cancellationToken);
    }

    private static HttpRequestMessage CreateRequest(DownloadConfiguration configuration)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, configuration.Url);
        request.Headers.Add("User-Agent", configuration.UserAgent);
        foreach (var header in configuration.Headers)
        {
            request.Headers.Add(header.Key, header.Value);
        }

        return request;
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

        using var request = CreateRequest(configuration);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(configuration.Timeout);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        long downloadedBytes = 0;
        var buffer = new byte[configuration.BufferSize];

        await using (var contentStream = await response.Content.ReadAsStreamAsync(cts.Token))
        await using (var fileStream = new FileStream(configuration.DestinationPath, FileMode.Create, FileAccess.Write, FileShare.None, configuration.BufferSize, useAsync: true))
        {
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, cts.Token)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token);
                downloadedBytes += bytesRead;

                // Report progress at specified intervals
                var now = DateTime.UtcNow;
                if (progress != null && (now - lastProgressReport >= configuration.ProgressReportingInterval || downloadedBytes == totalBytes))
                {
                    var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                    progress.Report(new DownloadProgress(
                        downloadedBytes,
                        totalBytes,
                        fileName,
                        configuration.Url,
                        elapsedSeconds > 0 ? (long)(downloadedBytes / elapsedSeconds) : 0,
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
