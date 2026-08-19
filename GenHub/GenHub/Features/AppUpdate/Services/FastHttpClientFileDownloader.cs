using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
public class FastHttpClientFileDownloader(
    ILogger<FastHttpClientFileDownloader>? logger = null,
    HttpMessageHandler? httpMessageHandler = null) : HttpClientFileDownloader
{
    private static readonly SocketsHttpHandler SharedSocketsHandler = new()
    {
        MaxConnectionsPerServer = 32,
        EnableMultipleHttp2Connections = true,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(60),
        ConnectTimeout = TimeSpan.FromSeconds(30),
    };

    private sealed class MonotonicProgressReporter(Action<int>? progressCallback, long totalBytes)
    {
        private readonly object _sync = new();
        private int _lastReportedPercent = -1;
        private long _totalBytesDownloaded;

        public void ReportBytesRead(int bytesRead)
        {
            if (progressCallback is null || totalBytes <= 0)
            {
                return;
            }

            var currentTotal = Interlocked.Add(ref _totalBytesDownloaded, bytesRead);
            var currentPercent = (int)Math.Clamp((double)currentTotal / totalBytes * 100, 0, 99);

            if (currentPercent <= Volatile.Read(ref _lastReportedPercent))
            {
                return;
            }

            lock (_sync)
            {
                if (currentPercent > _lastReportedPercent)
                {
                    _lastReportedPercent = currentPercent;
                    progressCallback(currentPercent);
                }
            }
        }

        public void Complete()
        {
            if (progressCallback is null)
            {
                return;
            }

            lock (_sync)
            {
                if (_lastReportedPercent < 100)
                {
                    _lastReportedPercent = 100;
                    progressCallback(100);
                }
            }
        }
    }

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
            // Probe range support and resolve redirects without holding open full stream
            using var probeRequest = new HttpRequestMessage(HttpMethod.Get, url);
            probeRequest.Headers.Range = new RangeHeaderValue(0, 0);

            using var probeResponse = await client.SendAsync(
                probeRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancelToken).ConfigureAwait(false);

            probeResponse.EnsureSuccessStatusCode();

            var resolvedUri = probeResponse.RequestMessage?.RequestUri ?? new Uri(url);
            var contentRange = probeResponse.Content.Headers.ContentRange;

            // Validate that probe returned 206 Partial Content with valid byte range (bytes 0-0/totalLength)
            var hasValidProbeRange = probeResponse.StatusCode == HttpStatusCode.PartialContent &&
                contentRange is not null &&
                string.Equals(contentRange.Unit, "bytes", StringComparison.OrdinalIgnoreCase) &&
                contentRange.From == 0 &&
                contentRange.To == 0 &&
                contentRange.Length is { } probeTotalLength &&
                probeTotalLength >= AppUpdateConstants.ParallelDownloadThresholdBytes;

            if (hasValidProbeRange)
            {
                var totalLength = contentRange!.Length!.Value;
                probeResponse.Dispose();

                logger?.LogInformation(
                    "Downloading {Url} via parallel chunk mode ({Concurrency} connections, Size: {Size:N0} bytes)",
                    url,
                    AppUpdateConstants.ParallelDownloadConcurrency,
                    totalLength);

                // If redirected to a third-party CDN/storage host (e.g. Azure Blob/S3), strip Authorization header to avoid 400 Bad Request on presigned URLs
                HttpClient chunkClient = client;
                HttpClient? cdnClient = null;
                var originUri = new Uri(url);
                if (!string.Equals(resolvedUri.Host, originUri.Host, StringComparison.OrdinalIgnoreCase) && headers?.ContainsKey("Authorization") == true)
                {
                    var cdnHeaders = headers.Where(h => !string.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
                                           .ToDictionary(h => h.Key, h => h.Value);
                    cdnClient = CreateHttpClient(cdnHeaders, timeout);
                    chunkClient = cdnClient;
                }

                try
                {
                    await DownloadParallelAsync(
                        chunkClient,
                        resolvedUri,
                        targetFile,
                        totalLength,
                        progress,
                        cancelToken).ConfigureAwait(false);
                }
                finally
                {
                    cdnClient?.Dispose();
                }

                return;
            }

            // If probe returned 200 OK (server ignored Range header), stream the probe response directly
            if (probeResponse.StatusCode == HttpStatusCode.OK)
            {
                var totalBytes = probeResponse.Content.Headers.ContentLength ?? -1L;
                await DownloadSingleStreamAsync(probeResponse, targetFile, totalBytes, progress, cancelToken).ConfigureAwait(false);
                return;
            }

            // Fallback to single-stream GET (e.g. for files below parallel threshold)
            using var fullResponse = await client.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancelToken).ConfigureAwait(false);

            fullResponse.EnsureSuccessStatusCode();
            var fullBytes = fullResponse.Content.Headers.ContentLength ?? -1L;
            await DownloadSingleStreamAsync(fullResponse, targetFile, fullBytes, progress, cancelToken).ConfigureAwait(false);
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

    /// <inheritdoc/>
    protected override HttpClient CreateHttpClient(IDictionary<string, string>? headers, double timeout)
    {
        var handler = httpMessageHandler ?? SharedSocketsHandler;
        var client = new HttpClient(handler, disposeHandler: false);
        if (timeout > 0)
        {
            client.Timeout = TimeSpan.FromSeconds(timeout);
        }

        if (headers != null)
        {
            foreach (var header in headers)
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return client;
    }

    private static async Task DownloadSingleStreamAsync(
        HttpResponseMessage response,
        string targetFile,
        long totalBytes,
        Action<int>? progress,
        CancellationToken cancelToken)
    {
        var progressReporter = new MonotonicProgressReporter(progress, totalBytes);

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancelToken).ConfigureAwait(false);
        await using var fileStream = new FileStream(
            targetFile,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            AppUpdateConstants.DefaultStreamBufferSize,
            useAsync: true);

        var buffer = new byte[AppUpdateConstants.DefaultStreamBufferSize];
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancelToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancelToken).ConfigureAwait(false);
            progressReporter.ReportBytesRead(bytesRead);
        }

        progressReporter.Complete();
    }

    private static async Task DownloadParallelAsync(
        HttpClient client,
        Uri uri,
        string targetFile,
        long totalBytes,
        Action<int>? progress,
        CancellationToken cancelToken)
    {
        // Pre-allocate the full file on disk and open safe handle for lock-free parallel writes
        using var fileHandle = File.OpenHandle(
            targetFile,
            FileMode.Create,
            FileAccess.Write,
            FileShare.ReadWrite,
            FileOptions.Asynchronous);

        RandomAccess.SetLength(fileHandle, totalBytes);

        var chunkSize = AppUpdateConstants.DownloadChunkSizeBytes;
        var chunkCount = (int)Math.Ceiling((double)totalBytes / chunkSize);
        var progressReporter = new MonotonicProgressReporter(progress, totalBytes);

        using var semaphore = new SemaphoreSlim(AppUpdateConstants.ParallelDownloadConcurrency);

        var tasks = Enumerable.Range(0, chunkCount).Select(async chunkIndex =>
        {
            await semaphore.WaitAsync(cancelToken).ConfigureAwait(false);
            try
            {
                var start = chunkIndex * chunkSize;
                var end = Math.Min(start + chunkSize - 1, totalBytes - 1);
                var expectedChunkBytes = end - start + 1;

                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Range = new RangeHeaderValue(start, end);

                using var chunkResponse = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancelToken).ConfigureAwait(false);

                if (chunkResponse.StatusCode != HttpStatusCode.PartialContent)
                {
                    throw new InvalidOperationException(
                        $"Origin server returned status code {chunkResponse.StatusCode} instead of 206 Partial Content for range {start}-{end}.");
                }

                var chunkRange = chunkResponse.Content.Headers.ContentRange;
                if (chunkRange is null ||
                    !string.Equals(chunkRange.Unit, "bytes", StringComparison.OrdinalIgnoreCase) ||
                    chunkRange.From != start ||
                    chunkRange.To != end ||
                    (chunkRange.Length.HasValue && chunkRange.Length.Value != totalBytes))
                {
                    throw new InvalidOperationException(
                        $"Origin server returned invalid Content-Range ({chunkRange}) for requested range {start}-{end} with total size {totalBytes}.");
                }

                await using var chunkStream = await chunkResponse.Content.ReadAsStreamAsync(cancelToken).ConfigureAwait(false);

                var buffer = new byte[AppUpdateConstants.DefaultStreamBufferSize];
                var chunkBytesRead = 0L;
                int bytesRead;

                while ((bytesRead = await chunkStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancelToken).ConfigureAwait(false)) > 0)
                {
                    await RandomAccess.WriteAsync(
                        fileHandle,
                        buffer.AsMemory(0, bytesRead),
                        start + chunkBytesRead,
                        cancelToken).ConfigureAwait(false);

                    chunkBytesRead += bytesRead;
                    progressReporter.ReportBytesRead(bytesRead);
                }

                if (chunkBytesRead != expectedChunkBytes)
                {
                    throw new InvalidOperationException(
                        $"Chunk range {start}-{end} received {chunkBytesRead} bytes, expected {expectedChunkBytes}.");
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        progressReporter.Complete();
    }
}
