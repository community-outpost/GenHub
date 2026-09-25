using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
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
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private sealed record DownloadConnection(
        HttpResponseMessage Response,
        bool IsResumed,
        long ExistingBytes,
        long TotalBytes,
        long? ExpectedContentBytes = null);

    private sealed record ParallelDownloadContext(
        DownloadConfiguration Configuration,
        IDownloadUrlValidator Validator,
        Uri TargetUri,
        long TotalBytes,
        EntityTagHeaderValue? InitialEtag,
        DateTimeOffset? InitialLastModified);

    private sealed class ParallelProgressTracker(
        DownloadConfiguration configuration,
        IProgress<DownloadProgress>? progress,
        long totalBytes,
        string fileName,
        Stopwatch stopwatch,
        CancellationTokenSource cts)
    {
        private readonly object progressLock = new();
        private long downloadedBytes;
        private DateTime lastProgressReport = DateTime.UtcNow;

        public void OnBytesRead(int bytesRead)
        {
            cts.CancelAfter(configuration.Timeout);
            var currentDownloaded = Interlocked.Add(ref downloadedBytes, bytesRead);
            if (progress == null)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (now - lastProgressReport < configuration.ProgressReportingInterval && currentDownloaded != totalBytes)
            {
                return;
            }

            lock (progressLock)
            {
                if (now - lastProgressReport >= configuration.ProgressReportingInterval || currentDownloaded == totalBytes)
                {
                    ReportDownloadProgress(
                        progress,
                        currentDownloaded,
                        currentDownloaded,
                        totalBytes,
                        fileName,
                        configuration.Url,
                        stopwatch.Elapsed);
                    lastProgressReport = now;
                }
            }
        }
    }

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

    private static void ApplyDownloadHeaders(
        HttpRequestMessage request,
        DownloadConfiguration configuration,
        Uri targetUri)
    {
        request.Headers.Add("User-Agent", configuration.UserAgent);

        var shouldStripAuthorization = !string.Equals(targetUri.Host, configuration.Url.Host, StringComparison.OrdinalIgnoreCase)
            || (string.Equals(configuration.Url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && string.Equals(targetUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase));

        foreach (var header in configuration.Headers)
        {
            if (string.Equals(header.Key, "ETag", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (shouldStripAuthorization && string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            request.Headers.Add(header.Key, header.Value);
        }
    }

    private static HttpRequestMessage CreateRequest(DownloadConfiguration configuration, Uri url, long rangeStart = 0)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyDownloadHeaders(request, configuration, url);

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

    private static HttpRequestMessage CreateChunkRequest(
        DownloadConfiguration configuration,
        Uri targetUri,
        long start,
        long end,
        EntityTagHeaderValue? initialEtag,
        DateTimeOffset? initialLastModified)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, targetUri);
        ApplyDownloadHeaders(request, configuration, targetUri);
        request.Headers.Range = new RangeHeaderValue(start, end);

        if (initialEtag != null)
        {
            request.Headers.IfRange = new RangeConditionHeaderValue(initialEtag);
        }
        else if (initialLastModified.HasValue)
        {
            request.Headers.IfRange = new RangeConditionHeaderValue(initialLastModified.Value);
        }
        else if (TryGetETagHeader(configuration, out var configEtag) && TryParseEntityTag(configEtag, out var parsedEtag))
        {
            request.Headers.IfRange = new RangeConditionHeaderValue(parsedEtag);
        }

        return request;
    }

    private static bool TryGetETagHeader(DownloadConfiguration configuration, [NotNullWhen(true)] out string? etag)
    {
        etag = configuration.Headers
            .Where(header => string.Equals(header.Key, "ETag", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(header.Value))
            .Select(header => header.Value)
            .FirstOrDefault();

        return etag != null;
    }

    private static bool TryParseEntityTag(string raw, [NotNullWhen(true)] out EntityTagHeaderValue? entityTag)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            entityTag = null;
            return false;
        }

        if (EntityTagHeaderValue.TryParse(raw, out entityTag) && !entityTag.IsWeak)
        {
            return true;
        }

        var trimmed = raw.Trim();
        if (EntityTagHeaderValue.TryParse($"\"{trimmed.Trim('\"')}\"", out entityTag) && !entityTag.IsWeak)
        {
            return true;
        }

        entityTag = null;
        return false;
    }

    private static void ReportDownloadProgress(
        IProgress<DownloadProgress> progress,
        long downloadedBytes,
        long sessionBytes,
        long totalBytes,
        string fileName,
        Uri url,
        TimeSpan elapsed)
    {
        var elapsedSeconds = elapsed.TotalSeconds;
        var bytesPerSecond = elapsedSeconds > 0 ? (long)(sessionBytes / elapsedSeconds) : 0L;

        var downloadProgress = new DownloadProgress(
            downloadedBytes,
            totalBytes,
            fileName,
            url,
            bytesPerSecond,
            elapsed);

        progress.Report(downloadProgress);
    }

    private static async Task TryWriteETagSidecarAsync(DownloadConfiguration configuration, CancellationToken cancellationToken)
    {
        if (configuration.EnableResumption && TryGetETagHeader(configuration, out var etag))
        {
            try
            {
                await File.WriteAllTextAsync($"{configuration.DestinationPath}.etag", etag.Trim(), cancellationToken);
            }
            catch (Exception)
            {
                // Non-fatal if sidecar cannot be written
            }
        }
    }

    private static void ValidateResumedCompletion(DownloadConnection connection, long downloadedBytes, long receivedContentBytes)
    {
        if (!connection.IsResumed)
        {
            return;
        }

        if (connection.ExpectedContentBytes is long expectedBytes && receivedContentBytes != expectedBytes)
        {
            throw new InvalidDataException("Resumed response body does not match its declared range.");
        }

        if (connection.TotalBytes > 0 && downloadedBytes < connection.TotalBytes)
        {
            throw new InvalidDataException($"Resumed download ended early: expected {connection.TotalBytes} bytes, got {downloadedBytes}.");
        }
    }

    private static void ValidateResponseContentType(HttpResponseMessage response, string destinationPath)
    {
        var extension = Path.GetExtension(destinationPath);
        if (!DownloadDefaults.IsBinaryTargetExtension(extension))
        {
            return;
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Download server returned HTML ({mediaType}) instead of the expected binary content for '{Path.GetFileName(destinationPath)}'. " +
                "The link may have expired, requires interactive browser authentication, or was blocked.");
        }
    }

    private static bool SupportsByteRanges(HttpResponseMessage response)
    {
        return response.Headers.AcceptRanges.Any(r => string.Equals(r, "bytes", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasStrongETag(DownloadConfiguration configuration, DownloadConnection connection)
    {
        if (connection.Response.Headers.ETag is { IsWeak: false })
        {
            return true;
        }

        return TryGetETagHeader(configuration, out var configEtag)
            && TryParseEntityTag(configEtag, out var parsedEtag)
            && !parsedEtag.IsWeak;
    }

    private static bool HasStrongRepresentationValidator(DownloadConfiguration configuration, DownloadConnection connection)
    {
        return HasStrongETag(configuration, connection) || !string.IsNullOrWhiteSpace(configuration.ExpectedHash);
    }

    private static bool CanUseParallelDownload(DownloadConfiguration configuration, DownloadConnection connection)
    {
        return configuration.EnableParallelDownload
            && !connection.IsResumed
            && connection.ExistingBytes == 0
            && configuration.ParallelConcurrency > 1
            && connection.TotalBytes >= configuration.ParallelDownloadThresholdBytes
            && SupportsByteRanges(connection.Response)
            && HasStrongRepresentationValidator(configuration, connection);
    }

    private static (long TotalBytes, long? ExpectedContentBytes) ValidateAndCalculateRangeBytes(
        ContentRangeHeaderValue contentRange,
        HttpResponseMessage response,
        long existingBytes)
    {
        var totalBytes = contentRange.Length
            ?? (existingBytes + (response.Content.Headers.ContentLength ?? 0));

        long? expectedContentBytes = null;
        if (contentRange.To.HasValue)
        {
            var calculatedExpected = contentRange.To.Value - existingBytes + 1;
            if (calculatedExpected <= 0 || (contentRange.Length.HasValue && contentRange.To.Value >= contentRange.Length.Value))
            {
                throw new InvalidDataException("Resumed response content range is invalid.");
            }

            if (response.Content.Headers.ContentLength is long contentLength && contentLength != calculatedExpected)
            {
                throw new InvalidDataException("Resumed response Content-Length does not match Content-Range.");
            }

            expectedContentBytes = calculatedExpected;
        }

        return (totalBytes, expectedContentBytes);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Non-fatal cleanup
        }
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

        await TryWriteETagSidecarAsync(configuration, cts.Token);

        var receivedContentBytes = 0L;
        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, cts.Token)) > 0)
        {
            if (connection.ExpectedContentBytes is long expectedContentBytes &&
                bytesRead > expectedContentBytes - receivedContentBytes)
            {
                throw new InvalidDataException("Resumed response body exceeds its declared range.");
            }

            cts.CancelAfter(configuration.Timeout);
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token);
            downloadedBytes += bytesRead;
            receivedContentBytes += bytesRead;

            var now = DateTime.UtcNow;
            if (progress != null && (now - lastProgressReport >= configuration.ProgressReportingInterval || downloadedBytes == connection.TotalBytes))
            {
                ReportDownloadProgress(
                    progress,
                    downloadedBytes,
                    downloadedBytes - connection.ExistingBytes,
                    connection.TotalBytes,
                    fileName,
                    configuration.Url,
                    stopwatch.Elapsed);
                lastProgressReport = now;
            }
        }

        ValidateResumedCompletion(connection, downloadedBytes, receivedContentBytes);

        return downloadedBytes;
    }

    private static void ValidateChunkResponse(
        HttpResponseMessage chunkResponse,
        ParallelDownloadContext context,
        long start,
        long end)
    {
        if (chunkResponse.StatusCode != HttpStatusCode.PartialContent)
        {
            throw new InvalidOperationException(
                $"Server returned status code {chunkResponse.StatusCode} instead of 206 Partial Content for range {start}-{end}.");
        }

        if (context.InitialEtag != null &&
            chunkResponse.Headers.ETag != null &&
            !chunkResponse.Headers.ETag.Equals(context.InitialEtag))
        {
            throw new InvalidOperationException(
                $"Server returned chunk with mismatched ETag ({chunkResponse.Headers.ETag}) instead of expected {context.InitialEtag}.");
        }

        var chunkRange = chunkResponse.Content.Headers.ContentRange;
        if (chunkRange is null ||
            !string.Equals(chunkRange.Unit, "bytes", StringComparison.OrdinalIgnoreCase) ||
            chunkRange.From != start ||
            chunkRange.To != end ||
            chunkRange.Length != context.TotalBytes)
        {
            throw new InvalidOperationException(
                $"Server returned invalid Content-Range ({chunkRange}) for requested range {start}-{end} (expected total bytes: {context.TotalBytes}).");
        }

        ValidateResponseContentType(chunkResponse, context.Configuration.DestinationPath);
    }

    private static async Task CopyChunkToHandleAsync(
        HttpResponseMessage chunkResponse,
        SafeFileHandle fileHandle,
        long start,
        long expectedChunkBytes,
        int bufferSize,
        ParallelProgressTracker progressTracker,
        CancellationToken token)
    {
        await using var chunkStream = await chunkResponse.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var buffer = new byte[bufferSize];
        var chunkBytesRead = 0L;
        int bytesRead;

        while ((bytesRead = await chunkStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false)) > 0)
        {
            await RandomAccess.WriteAsync(
                fileHandle,
                buffer.AsMemory(0, bytesRead),
                start + chunkBytesRead,
                token).ConfigureAwait(false);

            chunkBytesRead += bytesRead;
            progressTracker.OnBytesRead(bytesRead);
        }

        if (chunkBytesRead != expectedChunkBytes)
        {
            throw new InvalidDataException(
                $"Chunk range {start}-{start + expectedChunkBytes - 1} received {chunkBytesRead} bytes, expected {expectedChunkBytes}.");
        }
    }

    private async Task<HttpResponseMessage> SendChunkRequestAsync(
        ParallelDownloadContext context,
        long start,
        long end,
        CancellationToken token)
    {
        if (!context.Configuration.ValidateRedirectsManually)
        {
            using var request = CreateChunkRequest(
                context.Configuration,
                context.TargetUri,
                start,
                end,
                context.InitialEtag,
                context.InitialLastModified);
            return await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                token).ConfigureAwait(false);
        }

        var validated = await SsrfSafeHttpHelper.SendWithValidatedRedirectsAsync(
            httpClient,
            uri => CreateChunkRequest(
                context.Configuration,
                uri,
                start,
                end,
                context.InitialEtag,
                context.InitialLastModified),
            context.TargetUri,
            DownloadDefaults.MaxRedirects,
            context.Validator,
            token).ConfigureAwait(false);
        return validated.Response;
    }

    private async Task<long> DownloadParallelChunksAsync(
        ParallelDownloadContext context,
        IProgress<DownloadProgress>? progress,
        Stopwatch stopwatch,
        CancellationTokenSource cts)
    {
        var configuration = context.Configuration;
        var fileName = Path.GetFileName(configuration.DestinationPath);
        var concurrency = Math.Clamp(configuration.ParallelConcurrency, 2, DownloadDefaults.MaxParallelChunkConcurrency);
        var chunkSize = DownloadDefaults.ParallelDownloadChunkSizeBytes;
        var chunkCount = (int)Math.Ceiling((double)context.TotalBytes / chunkSize);

        logger.LogInformation(
            "Downloading {FileName} ({TotalBytes:N0} bytes) via parallel chunk mode ({Concurrency} connections, {ChunkCount} chunks of {ChunkSize:N0} bytes)",
            fileName,
            context.TotalBytes,
            concurrency,
            chunkCount,
            chunkSize);

        using (var fileHandle = File.OpenHandle(
            configuration.DestinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.ReadWrite,
            FileOptions.Asynchronous))
        {
            RandomAccess.SetLength(fileHandle, context.TotalBytes);
            await TryWriteETagSidecarAsync(configuration, cts.Token).ConfigureAwait(false);

            var progressTracker = new ParallelProgressTracker(
                configuration,
                progress,
                context.TotalBytes,
                fileName,
                stopwatch,
                cts);
            var bufferSize = Math.Max(configuration.BufferSize, DownloadDefaults.ParallelChunkBufferSizeBytes);

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = concurrency,
                CancellationToken = cts.Token,
            };

            await Parallel.ForEachAsync(Enumerable.Range(0, chunkCount), parallelOptions, async (chunkIndex, token) =>
            {
                var start = (long)chunkIndex * chunkSize;
                var end = Math.Min(start + chunkSize - 1, context.TotalBytes - 1);
                var expectedChunkBytes = end - start + 1;

                using var chunkResponse = await SendChunkRequestAsync(
                    context,
                    start,
                    end,
                    token).ConfigureAwait(false);

                ValidateChunkResponse(chunkResponse, context, start, end);

                await CopyChunkToHandleAsync(
                    chunkResponse,
                    fileHandle,
                    start,
                    expectedChunkBytes,
                    bufferSize,
                    progressTracker,
                    token).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        return context.TotalBytes;
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
        DownloadConfiguration configuration,
        IDownloadUrlValidator validator,
        long rangeStart,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        if (!configuration.ValidateRedirectsManually)
        {
            using var request = CreateRequest(configuration, configuration.Url, rangeStart);
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        else
        {
            var validated = await SsrfSafeHttpHelper.SendWithValidatedRedirectsAsync(
                httpClient,
                uri => CreateRequest(configuration, uri, rangeStart),
                configuration.Url,
                DownloadDefaults.MaxRedirects,
                validator,
                cancellationToken);
            response = validated.Response;
        }

        return await ResolveGoogleDriveConfirmationIfNeededAsync(response, configuration, validator, cancellationToken);
    }

    private async Task<DownloadResult> DownloadWithRetryAsync(
        DownloadConfiguration configuration,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, configuration.MaxRetryAttempts);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await PerformDownloadAsync(configuration, progress, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (InvalidDataException ex)
            {
                logger.LogError(ex, "Download for {Url} failed with non-retryable invalid data error: {Message}", configuration.Url, ex.Message);
                return DownloadResult.CreateFailure(ex.Message, 0, TimeSpan.Zero);
            }
            catch (Exception ex)
            {
                if (attempt == maxAttempts)
                {
                    logger.LogError(ex, "Download failed after {Attempts} attempts for {Url}", maxAttempts, configuration.Url);
                    return DownloadResult.CreateFailure(ex.Message, 0, TimeSpan.Zero);
                }

                logger.LogWarning(ex, "Download attempt {Attempt} failed for {Url}, retrying...", attempt, configuration.Url);
                await Task.Delay(configuration.RetryDelay, cancellationToken);
            }
        }

        return DownloadResult.CreateFailure("Download failed.", 0, TimeSpan.Zero);
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

        var etagSidecarPath = $"{configuration.DestinationPath}.etag";
        var hasMatchingSidecar = TryGetETagHeader(configuration, out var configEtag)
            && TryParseEntityTag(configEtag, out var parsedConfigEtag)
            && TryReadSidecarEtag(etagSidecarPath, out var parsedSidecarEtag)
            && string.Equals(parsedConfigEtag.Tag, parsedSidecarEtag.Tag, StringComparison.Ordinal);

        var existingBytes = (configuration.EnableResumption
            && destFileInfo.Exists
            && hasMatchingSidecar)
            ? destFileInfo.Length
            : 0L;

        if (existingBytes == 0)
        {
            TryDeleteFile(etagSidecarPath);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(configuration.Timeout);
        var validator = urlValidator ?? new DownloadUrlValidator();

        var connection = await EstablishDownloadConnectionAsync(configuration, validator, existingBytes, cts);

        if (CanUseParallelDownload(configuration, connection))
        {
            var parallelContext = new ParallelDownloadContext(
                configuration,
                validator,
                connection.Response.RequestMessage?.RequestUri ?? configuration.Url,
                connection.TotalBytes,
                connection.Response.Headers.ETag,
                connection.Response.Content.Headers.LastModified);
            connection.Response.Dispose();

            try
            {
                var stopwatch = Stopwatch.StartNew();
                var downloadedBytes = await DownloadParallelChunksAsync(
                    parallelContext,
                    progress,
                    stopwatch,
                    cts).ConfigureAwait(false);
                stopwatch.Stop();

                return await FinalizeDownloadAsync(
                    configuration,
                    downloadedBytes,
                    stopwatch.Elapsed,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Parallel chunk download failed for {Url}. Falling back to sequential download.", configuration.Url);
                TryDeleteFile(configuration.DestinationPath);
                TryDeleteFile(etagSidecarPath);
                cts.CancelAfter(configuration.Timeout);

                connection = await EstablishDownloadConnectionAsync(configuration, validator, 0, cts);
            }
        }

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
            TryDeleteFile($"{configuration.DestinationPath}.etag");
        }

        var response = await SendRequestAsync(configuration, validator, 0, cts.Token);
        try
        {
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentRange != null)
            {
                response.EnsureSuccessStatusCode();
                throw new HttpRequestException(
                    $"Expected HTTP 200 OK without Content-Range for full download, but received status {response.StatusCode}.",
                    null,
                    response.StatusCode);
            }

            ValidateResponseContentType(response, configuration.DestinationPath);
            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            return new DownloadConnection(response, false, 0, totalBytes);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private DownloadConnection? HandlePartialContentResponse(
        HttpResponseMessage response,
        DownloadConfiguration configuration,
        long existingBytes)
    {
        var contentRange = response.Content.Headers.ContentRange;
        if (contentRange?.From == existingBytes &&
            string.Equals(contentRange.Unit, "bytes", StringComparison.OrdinalIgnoreCase))
        {
            ValidateResponseContentType(response, configuration.DestinationPath);
            var (totalBytes, expectedContentBytes) = ValidateAndCalculateRangeBytes(contentRange, response, existingBytes);
            return new DownloadConnection(response, true, existingBytes, totalBytes, expectedContentBytes);
        }

        logger.LogWarning("Range {Range} mismatch on {Url}; expected {Expected}. Restarting download.", contentRange?.From, configuration.Url, existingBytes);
        return null;
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
                var partial = HandlePartialContentResponse(response, configuration, existingBytes);
                if (partial != null)
                {
                    return partial;
                }
            }
            else if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                TryDeleteFile($"{configuration.DestinationPath}.etag");
                logger.LogWarning("Range {Range} not satisfiable for {Url}. Restarting download from scratch.", existingBytes, configuration.Url);
            }
            else if (response.IsSuccessStatusCode)
            {
                // Server responded 200 OK: ignored Range header and sent full content
                TryDeleteFile($"{configuration.DestinationPath}.etag");
                ValidateResponseContentType(response, configuration.DestinationPath);
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
        TryDeleteFile($"{configuration.DestinationPath}.etag");

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

    private bool TryReadSidecarEtag(string sidecarPath, [NotNullWhen(true)] out EntityTagHeaderValue? parsedEtag)
    {
        parsedEtag = null;
        try
        {
            if (File.Exists(sidecarPath))
            {
                var content = File.ReadAllText(sidecarPath).Trim();
                return TryParseEntityTag(content, out parsedEtag);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read ETag sidecar file {FilePath}; treating as stale.", sidecarPath);
            TryDeleteFile(sidecarPath);
        }

        return false;
    }

    private static string? TryExtractConfirmationUrl(string html, Uri? requestUri)
    {
        var confirmMatch = Regex.Match(html, "href=\"(/uc\\?export=download[^\"]+confirm=[^\"]+)\"", RegexOptions.IgnoreCase, RegexTimeout);
        if (confirmMatch.Success)
        {
            var relativeUrl = confirmMatch.Groups[1].Value.Replace("&amp;", "&");
            var baseUri = requestUri ?? new Uri(Uri.UriSchemeHttps + "://drive.google.com");
            return new Uri(baseUri, relativeUrl).ToString();
        }

        return TryExtractFormActionUrl(html);
    }

    private static string? TryExtractFormActionUrl(string html)
    {
        var actionMatch = Regex.Match(html, "action=\"(https://drive\\.usercontent\\.google\\.com/download[^\"]*)\"", RegexOptions.IgnoreCase, RegexTimeout);
        if (!actionMatch.Success)
        {
            return null;
        }

        var action = actionMatch.Groups[1].Value.Replace("&amp;", "&");
        var inputMatches = Regex.Matches(html, "<input[^>]+type=\"hidden\"[^>]+name=\"([^\"]+)\"[^>]+value=\"([^\"]*)\"", RegexOptions.IgnoreCase, RegexTimeout);
        var queryParams = inputMatches
            .Select(m => $"{Uri.EscapeDataString(m.Groups[1].Value)}={Uri.EscapeDataString(m.Groups[2].Value)}")
            .ToList();

        if (queryParams.Count > 0)
        {
            var separator = action.Contains('?') ? "&" : "?";
            return $"{action}{separator}{string.Join("&", queryParams)}";
        }

        return action;
    }

    private async Task<HttpResponseMessage> ResolveGoogleDriveConfirmationIfNeededAsync(
        HttpResponseMessage initialResponse,
        DownloadConfiguration configuration,
        IDownloadUrlValidator validator,
        CancellationToken cancellationToken)
    {
        var mediaType = initialResponse.Content.Headers.ContentType?.MediaType;
        var uri = initialResponse.RequestMessage?.RequestUri?.ToString() ?? configuration.Url?.ToString() ?? string.Empty;
        var isGoogle = uri.Contains("drive.google.com", StringComparison.OrdinalIgnoreCase) ||
                       uri.Contains("docs.google.com", StringComparison.OrdinalIgnoreCase) ||
                       uri.Contains("drive.usercontent.google.com", StringComparison.OrdinalIgnoreCase);

        if (!isGoogle || !string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase))
        {
            return initialResponse;
        }

        var html = await initialResponse.Content.ReadAsStringAsync(cancellationToken);
        initialResponse.Dispose();

        var confirmUrl = TryExtractConfirmationUrl(html, initialResponse.RequestMessage?.RequestUri ?? configuration.Url);
        if (confirmUrl != null)
        {
            logger.LogInformation("Following Google Drive download confirmation");
            var confirmedConfig = new DownloadConfiguration
            {
                Url = new Uri(confirmUrl),
                DestinationPath = configuration.DestinationPath,
                BufferSize = configuration.BufferSize,
                Timeout = configuration.Timeout,
                UserAgent = configuration.UserAgent,
                OverwriteExisting = configuration.OverwriteExisting,
                ExpectedHash = configuration.ExpectedHash,
                MaxRetryAttempts = configuration.MaxRetryAttempts,
                RetryDelay = configuration.RetryDelay,
                ValidateRedirectsManually = configuration.ValidateRedirectsManually,
            };
            return await SendRequestAsync(confirmedConfig, validator, 0, cancellationToken);
        }

        if (html.Contains("quota", StringComparison.OrdinalIgnoreCase) || html.Contains("too many users", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Google Drive download quota exceeded for this file.");
        }

        throw new InvalidOperationException("Google Drive returned an HTML page instead of the expected file download.");
    }
}
