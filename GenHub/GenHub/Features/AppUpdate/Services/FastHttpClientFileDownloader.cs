using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using Microsoft.Extensions.Logging;
using Velopack.Sources;

namespace GenHub.Features.AppUpdate.Services;

/// <summary>
/// High-performance file downloader for Velopack and application updates.
/// Supports parallel range chunk downloading for large assets from GitHub Releases and CDN origins.
/// </summary>
public class FastHttpClientFileDownloader(ILogger<FastHttpClientFileDownloader>? logger = null) : HttpClientFileDownloader
{
    /// <inheritdoc/>
    public override async Task DownloadFile(
        string url,
        string targetFile,
        Action<int> progress,
        IDictionary<string, string>? headers,
        double timeout,
        CancellationToken cancelToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFile);

        var destinationDirectory = Path.GetDirectoryName(targetFile);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        using var client = CreateHttpClient(headers, timeout);

        try
        {
            using var response = await client.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancelToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var acceptRanges = response.Headers.AcceptRanges;
            var supportsRanges = acceptRanges.Contains("bytes");
            var resolvedUri = response.RequestMessage?.RequestUri ?? new Uri(url);

            // If the file is smaller than the parallel threshold or doesn't advertise range support, use optimized single-stream download
            if (totalBytes < AppUpdateConstants.ParallelDownloadThresholdBytes || !supportsRanges)
            {
                logger?.LogDebug(
                    "Downloading {Url} via single-stream mode (Size: {Size} bytes, SupportsRanges: {SupportsRanges})",
                    url,
                    totalBytes,
                    supportsRanges);

                await DownloadSingleStreamAsync(response, targetFile, totalBytes, progress, cancelToken).ConfigureAwait(false);
                return;
            }

            logger?.LogInformation(
                "Downloading {Url} via parallel chunk mode ({Concurrency} connections, Size: {Size:N0} bytes)",
                url,
                AppUpdateConstants.ParallelDownloadConcurrency,
                totalBytes);

            await DownloadParallelAsync(
                client,
                resolvedUri,
                targetFile,
                totalBytes,
                progress,
                cancelToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(
                ex,
                "Parallel download encountered an issue for {Url}. Falling back to default downloader",
                url);

            await base.DownloadFile(url, targetFile, progress, headers, timeout, cancelToken).ConfigureAwait(false);
        }
    }

    private static async Task DownloadSingleStreamAsync(
        HttpResponseMessage response,
        string targetFile,
        long totalBytes,
        Action<int>? progress,
        CancellationToken cancelToken)
    {
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancelToken).ConfigureAwait(false);
        await using var fileStream = new FileStream(
            targetFile,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            AppUpdateConstants.DefaultStreamBufferSize,
            useAsync: true);

        var buffer = new byte[AppUpdateConstants.DefaultStreamBufferSize];
        var totalRead = 0L;
        int bytesRead = 0;

        while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancelToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancelToken).ConfigureAwait(false);
            totalRead += bytesRead;

            if (progress != null && totalBytes > 0)
            {
                var percent = (int)((double)totalRead / totalBytes * 100);
                progress(Math.Clamp(percent, 0, 100));
            }
        }

        progress?.Invoke(100);
    }

    private static async Task DownloadParallelAsync(
        HttpClient client,
        Uri uri,
        string targetFile,
        long totalBytes,
        Action<int>? progress,
        CancellationToken cancelToken)
    {
        // Pre-allocate the full file on disk to prevent fragmentation and allow concurrent range writing
        await using (var preAllocStream = new FileStream(
            targetFile,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Write,
            AppUpdateConstants.DefaultStreamBufferSize,
            useAsync: true))
        {
            preAllocStream.SetLength(totalBytes);
        }

        var chunkSize = AppUpdateConstants.DownloadChunkSizeBytes;
        var chunkCount = (int)Math.Ceiling((double)totalBytes / chunkSize);
        var totalDownloaded = 0L;

        using var semaphore = new SemaphoreSlim(AppUpdateConstants.ParallelDownloadConcurrency);

        var tasks = Enumerable.Range(0, chunkCount).Select(async chunkIndex =>
        {
            await semaphore.WaitAsync(cancelToken).ConfigureAwait(false);
            try
            {
                var start = chunkIndex * chunkSize;
                var end = Math.Min(start + chunkSize - 1, totalBytes - 1);

                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Range = new RangeHeaderValue(start, end);

                using var chunkResponse = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancelToken).ConfigureAwait(false);

                chunkResponse.EnsureSuccessStatusCode();

                await using var chunkStream = await chunkResponse.Content.ReadAsStreamAsync(cancelToken).ConfigureAwait(false);
                await using var fileStream = new FileStream(
                    targetFile,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.ReadWrite,
                    AppUpdateConstants.DefaultStreamBufferSize,
                    useAsync: true);

                fileStream.Seek(start, SeekOrigin.Begin);

                var buffer = new byte[AppUpdateConstants.DefaultStreamBufferSize];
                int bytesRead = 0;

                while ((bytesRead = await chunkStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancelToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancelToken).ConfigureAwait(false);

                    var currentTotal = Interlocked.Add(ref totalDownloaded, bytesRead);
                    if (progress != null && totalBytes > 0)
                    {
                        var percent = (int)((double)currentTotal / totalBytes * 100);
                        progress(Math.Clamp(percent, 0, 100));
                    }
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        progress?.Invoke(100);
    }
}
