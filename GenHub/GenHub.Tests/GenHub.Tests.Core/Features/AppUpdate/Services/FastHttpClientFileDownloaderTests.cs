using GenHub.Core.Constants;
using GenHub.Features.AppUpdate.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.AppUpdate.Services;

/// <summary>
/// Unit tests for <see cref="FastHttpClientFileDownloader"/>.
/// </summary>
public class FastHttpClientFileDownloaderTests : IDisposable
{
    private sealed class TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handlerFunc) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
            }

            return Task.FromResult(handlerFunc(request));
        }
    }

    private readonly Mock<ILogger<FastHttpClientFileDownloader>> _mockLogger = new();
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"genhub-downloader-tests-{Guid.NewGuid():N}");

    /// <summary>
    /// Initializes a new instance of the <see cref="FastHttpClientFileDownloaderTests"/> class.
    /// </summary>
    public FastHttpClientFileDownloaderTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// Disposes test resources and cleans up temporary directories.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
                // Ignore test directory cleanup failures
            }
        }
    }

    /// <summary>
    /// Tests that the downloader can be initialized with and without a logger.
    /// </summary>
    [Fact]
    public void Constructor_ShouldInitializeSuccessfully()
    {
        var downloaderWithoutLogger = new FastHttpClientFileDownloader();
        var downloaderWithLogger = new FastHttpClientFileDownloader(_mockLogger.Object);

        Assert.NotNull(downloaderWithoutLogger);
        Assert.NotNull(downloaderWithLogger);
    }

    /// <summary>
    /// Tests that DownloadFile throws ArgumentException when URL is invalid.
    /// </summary>
    /// <param name="invalidUrl">The invalid URL string.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DownloadFile_WithInvalidUrl_ShouldThrowArgumentExceptionAsync(string? invalidUrl)
    {
        var downloader = new FastHttpClientFileDownloader(_mockLogger.Object);
        var targetFile = Path.Combine(_tempDirectory, "test.tmp");

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => downloader.DownloadFile(invalidUrl!, targetFile, _ => { }, null, 30));
    }

    /// <summary>
    /// Tests that DownloadFile throws ArgumentException when target file path is invalid.
    /// </summary>
    /// <param name="invalidTargetFile">The invalid target file path string.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DownloadFile_WithInvalidTargetFile_ShouldThrowArgumentExceptionAsync(string? invalidTargetFile)
    {
        var downloader = new FastHttpClientFileDownloader(_mockLogger.Object);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => downloader.DownloadFile("https://example.com/file.zip", invalidTargetFile!, _ => { }, null, 30));
    }

    /// <summary>
    /// Tests that parallel chunk downloading correctly assembles multi-chunk files and reports progress monotonically.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFile_ParallelRange_ValidAssembly_ShouldDownloadAndVerifyContentAsync()
    {
        // 6 MB file (3 chunks of 2 MB)
        var totalBytes = AppUpdateConstants.DownloadChunkSizeBytes * 3;
        var sourceBytes = new byte[totalBytes];
        new Random(42).NextBytes(sourceBytes);

        var progressHistory = new ConcurrentQueue<int>();

        var handler = new TestHttpMessageHandler(request =>
        {
            var range = request.Headers.Range?.Ranges.FirstOrDefault();
            if (range is { From: 0, To: 0 })
            {
                // Probe request
                var probeResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent([sourceBytes[0]]),
                    RequestMessage = request,
                };
                probeResponse.Headers.ETag = new EntityTagHeaderValue("\"test-etag\"");
                probeResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, totalBytes) { Unit = "bytes" };
                return probeResponse;
            }

            if (range is { From: { } from, To: { } to })
            {
                Assert.NotNull(request.Headers.IfRange);
                Assert.Equal("\"test-etag\"", request.Headers.IfRange.EntityTag?.Tag);

                var length = (int)(to - from + 1);
                var chunkData = new byte[length];
                Array.Copy(sourceBytes, from, chunkData, 0, length);

                var chunkResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(chunkData),
                    RequestMessage = request,
                };
                chunkResponse.Headers.ETag = new EntityTagHeaderValue("\"test-etag\"");
                chunkResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, totalBytes) { Unit = "bytes" };
                return chunkResponse;
            }

            var fullResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(sourceBytes),
                RequestMessage = request,
            };
            return fullResponse;
        });

        var downloader = new FastHttpClientFileDownloader(_mockLogger.Object, handler);
        var targetFile = Path.Combine(_tempDirectory, "parallel-output.bin");

        await downloader.DownloadFile(
            "https://github.com/community-outpost/GenHub/releases/download/v1.0.0/test.bin",
            targetFile,
            progressHistory.Enqueue,
            null,
            30);

        Assert.True(File.Exists(targetFile));
        var downloadedBytes = await File.ReadAllBytesAsync(targetFile);
        Assert.Equal(sourceBytes, downloadedBytes);

        var progressList = progressHistory.ToList();
        Assert.NotEmpty(progressList);
        Assert.Equal(100, progressList.Last());
    }

    /// <summary>
    /// Tests that small files below the parallel threshold use single-stream mode without chunking.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFile_SmallFileBelowThreshold_ShouldUseSingleStreamAsync()
    {
        var smallBytes = new byte[1024 * 1024]; // 1 MB
        new Random(42).NextBytes(smallBytes);

        var chunkRequestsCount = 0;

        var handler = new TestHttpMessageHandler(request =>
        {
            var range = request.Headers.Range?.Ranges.FirstOrDefault();
            if (range is { From: 0, To: 0 })
            {
                var probeResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent([smallBytes[0]]),
                    RequestMessage = request,
                };
                probeResponse.Headers.ETag = new EntityTagHeaderValue("\"test-etag\"");
                probeResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, smallBytes.Length) { Unit = "bytes" };
                return probeResponse;
            }

            if (range is not null)
            {
                Interlocked.Increment(ref chunkRequestsCount);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(smallBytes),
                RequestMessage = request,
            };
        });

        var downloader = new FastHttpClientFileDownloader(_mockLogger.Object, handler);
        var targetFile = Path.Combine(_tempDirectory, "small-file.bin");

        await downloader.DownloadFile("https://example.com/small.bin", targetFile, _ => { }, null, 30);

        Assert.True(File.Exists(targetFile));
        Assert.Equal(smallBytes, await File.ReadAllBytesAsync(targetFile));
        Assert.Equal(0, chunkRequestsCount);
    }

    /// <summary>
    /// Tests that HTML web page responses from origins throw InvalidDataException to prevent saving corrupt files.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFile_HtmlResponse_ShouldThrowInvalidDataExceptionAsync()
    {
        const string htmlPayload = "<!DOCTYPE html><html><head><title>Login</title></head><body>Login required</body></html>";
        var handler = new TestHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(htmlPayload, System.Text.Encoding.UTF8, "text/html"),
        });

        var downloader = new FastHttpClientFileDownloader(_mockLogger.Object, handler);
        var targetFile = Path.Combine(_tempDirectory, "html-response.bin");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => downloader.DownloadFile("https://example.com/download.zip", targetFile, _ => { }, null, 30));
    }

    /// <summary>
    /// Tests that when a chunk response returns an invalid Content-Range header, the downloader falls back to single-stream.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFile_InvalidContentRange_ShouldFallbackToSingleStreamAsync()
    {
        var totalBytes = AppUpdateConstants.DownloadChunkSizeBytes * 2;
        var sourceBytes = new byte[totalBytes];
        new Random(77).NextBytes(sourceBytes);

        var handler = new TestHttpMessageHandler(request =>
        {
            var range = request.Headers.Range?.Ranges.FirstOrDefault();
            if (range is { From: 0, To: 0 })
            {
                // Probe response
                var probeResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent([sourceBytes[0]]),
                    RequestMessage = request,
                };
                probeResponse.Headers.ETag = new EntityTagHeaderValue("\"test-etag\"");
                probeResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, totalBytes) { Unit = "bytes" };
                return probeResponse;
            }

            if (range is not null)
            {
                // Return mismatched Content-Range
                var badResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(new byte[100]),
                    RequestMessage = request,
                };
                badResponse.Headers.ETag = new EntityTagHeaderValue("\"test-etag\"");
                badResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(999, 1098, totalBytes) { Unit = "bytes" };
                return badResponse;
            }

            // Fallback path sends full payload
            var fullResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(sourceBytes),
                RequestMessage = request,
            };
            return fullResponse;
        });

        var downloader = new FastHttpClientFileDownloader(_mockLogger.Object, handler);
        var targetFile = Path.Combine(_tempDirectory, "fallback-invalid-range.bin");

        await downloader.DownloadFile("https://example.com/large.bin", targetFile, _ => { }, null, 30);

        Assert.True(File.Exists(targetFile));
        var downloadedBytes = await File.ReadAllBytesAsync(targetFile);
        Assert.Equal(sourceBytes, downloadedBytes);
    }

    /// <summary>
    /// Tests that when a chunk response streams fewer bytes than requested, the downloader falls back to single-stream.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFile_ShortChunkStream_ShouldFallbackToSingleStreamAsync()
    {
        var totalBytes = AppUpdateConstants.DownloadChunkSizeBytes * 2;
        var sourceBytes = new byte[totalBytes];
        new Random(99).NextBytes(sourceBytes);

        var handler = new TestHttpMessageHandler(request =>
        {
            var range = request.Headers.Range?.Ranges.FirstOrDefault();
            if (range is { From: 0, To: 0 })
            {
                // Probe response
                var probeResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent([sourceBytes[0]]),
                    RequestMessage = request,
                };
                probeResponse.Headers.ETag = new EntityTagHeaderValue("\"test-etag\"");
                probeResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, totalBytes) { Unit = "bytes" };
                return probeResponse;
            }

            if (range is { From: { } from, To: { } to })
            {
                // Return short stream (100 bytes instead of expected chunk length)
                var shortResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(new byte[100]),
                    RequestMessage = request,
                };
                shortResponse.Headers.ETag = new EntityTagHeaderValue("\"test-etag\"");
                shortResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, totalBytes) { Unit = "bytes" };
                return shortResponse;
            }

            // Fallback path sends full payload
            var fullResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(sourceBytes),
                RequestMessage = request,
            };
            return fullResponse;
        });

        var downloader = new FastHttpClientFileDownloader(_mockLogger.Object, handler);
        var targetFile = Path.Combine(_tempDirectory, "fallback-short-chunk.bin");

        await downloader.DownloadFile("https://example.com/large.bin", targetFile, _ => { }, null, 30);

        Assert.True(File.Exists(targetFile));
        var downloadedBytes = await File.ReadAllBytesAsync(targetFile);
        Assert.Equal(sourceBytes, downloadedBytes);
    }

    /// <summary>
    /// Tests that progress reporting is strictly monotonic (never moves backward) and throttled to at most 101 updates.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFile_ProgressReporting_ShouldBeStrictlyMonotonicAndThrottledAsync()
    {
        var totalBytes = AppUpdateConstants.DownloadChunkSizeBytes * 3; // 24 MB
        var sourceBytes = new byte[totalBytes];

        var progressHistory = new ConcurrentQueue<int>();

        var handler = new TestHttpMessageHandler(request =>
        {
            var range = request.Headers.Range?.Ranges.FirstOrDefault();
            if (range is { From: 0, To: 0 })
            {
                var probeResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent([0]),
                    RequestMessage = request,
                };
                probeResponse.Headers.ETag = new EntityTagHeaderValue("\"test-etag\"");
                probeResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, totalBytes) { Unit = "bytes" };
                return probeResponse;
            }

            if (range is { From: { } from, To: { } to })
            {
                var length = (int)(to - from + 1);
                var chunkResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(new byte[length]),
                    RequestMessage = request,
                };
                chunkResponse.Headers.ETag = new EntityTagHeaderValue("\"test-etag\"");
                chunkResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, totalBytes) { Unit = "bytes" };
                return chunkResponse;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(sourceBytes),
                RequestMessage = request,
            };
        });

        var downloader = new FastHttpClientFileDownloader(_mockLogger.Object, handler);
        var targetFile = Path.Combine(_tempDirectory, "progress-test.bin");

        await downloader.DownloadFile("https://example.com/file.bin", targetFile, progressHistory.Enqueue, null, 30);

        var progressList = progressHistory.ToList();

        Assert.NotEmpty(progressList);
        Assert.Equal(100, progressList.Last());

        // Verify strictly monotonic ordering (each progress event >= previous)
        for (var i = 1; i < progressList.Count; i++)
        {
            Assert.True(progressList[i] >= progressList[i - 1], $"Progress moved backward from {progressList[i - 1]} to {progressList[i]}");
        }

        // Verify throttling: no more than 101 progress updates (0 to 100)
        Assert.True(progressList.Count <= 101, $"Progress was called {progressList.Count} times, exceeding maximum throttled limit of 101");
    }

    /// <summary>
    /// Tests that cancellation tokens are properly observed and propagated.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFile_WhenCancelled_ShouldThrowOperationCanceledExceptionAsync()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new TestHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.OK));
        var downloader = new FastHttpClientFileDownloader(_mockLogger.Object, handler);
        var targetFile = Path.Combine(_tempDirectory, "canceled.bin");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => downloader.DownloadFile("https://example.com/file.bin", targetFile, _ => { }, null, 30, cts.Token));
    }

    /// <summary>
    /// Tests that when redirected to a cross-origin storage host, the Authorization header is omitted from chunk requests.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFile_WhenRedirectedToCrossOriginCdn_ShouldStripAuthorizationHeaderOnChunksAsync()
    {
        var totalBytes = AppUpdateConstants.DownloadChunkSizeBytes * 2;
        var sourceBytes = new byte[totalBytes];
        new Random(42).NextBytes(sourceBytes);

        var chunkAuthHeadersPresent = 0;

        var handler = new TestHttpMessageHandler(request =>
        {
            var range = request.Headers.Range?.Ranges.FirstOrDefault();
            if (range is { From: 0, To: 0 })
            {
                var probeResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent([sourceBytes[0]]),
                    RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://cdn.blob.core.windows.net/artifacts/file.zip"),
                };
                probeResponse.Headers.ETag = new EntityTagHeaderValue("\"test-etag\"");
                probeResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, totalBytes) { Unit = "bytes" };
                return probeResponse;
            }

            if (range is { From: { } from, To: { } to })
            {
                if (request.Headers.Contains("Authorization"))
                {
                    Interlocked.Increment(ref chunkAuthHeadersPresent);
                }

                var length = (int)(to - from + 1);
                var chunkData = new byte[length];
                Array.Copy(sourceBytes, from, chunkData, 0, length);

                var chunkResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(chunkData),
                    RequestMessage = request,
                };
                chunkResponse.Headers.ETag = new EntityTagHeaderValue("\"test-etag\"");
                chunkResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, totalBytes) { Unit = "bytes" };
                return chunkResponse;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(sourceBytes),
                RequestMessage = request,
            };
        });

        var downloader = new FastHttpClientFileDownloader(_mockLogger.Object, handler);
        var targetFile = Path.Combine(_tempDirectory, "cross-origin-test.bin");
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer secret-token",
        };

        await downloader.DownloadFile(
            "https://github.com/community-outpost/GenHub/releases/download/v1.0.0/test.bin",
            targetFile,
            _ => { },
            headers,
            30);

        Assert.True(File.Exists(targetFile));
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(targetFile));
        Assert.Equal(0, chunkAuthHeadersPresent);
    }

    /// <summary>
    /// Tests that when the probe response lacks a strong validator (no ETag, no LastModified),
    /// parallel download is disabled and the downloader falls back to single-stream mode.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFile_NoValidator_ShouldDisableParallelAndUseSingleStreamAsync()
    {
        var totalBytes = AppUpdateConstants.DownloadChunkSizeBytes * 2;
        var sourceBytes = new byte[totalBytes];
        new Random(55).NextBytes(sourceBytes);

        var chunkRequestsCount = 0;

        var handler = new TestHttpMessageHandler(request =>
        {
            var range = request.Headers.Range?.Ranges.FirstOrDefault();
            if (range is { From: 0, To: 0 })
            {
                // Probe response with NO ETag and NO LastModified
                var probeResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent([sourceBytes[0]]),
                    RequestMessage = request,
                };
                probeResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, totalBytes) { Unit = "bytes" };
                return probeResponse;
            }

            if (range is not null)
            {
                Interlocked.Increment(ref chunkRequestsCount);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(sourceBytes),
                RequestMessage = request,
            };
        });

        var downloader = new FastHttpClientFileDownloader(_mockLogger.Object, handler);
        var targetFile = Path.Combine(_tempDirectory, "no-validator.bin");

        await downloader.DownloadFile("https://example.com/no-validator.bin", targetFile, _ => { }, null, 30);

        Assert.True(File.Exists(targetFile));
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(targetFile));
        Assert.Equal(0, chunkRequestsCount);
    }

    /// <summary>
    /// Tests that when a chunk response returns a mismatched ETag validator,
    /// parallel mode is aborted and falls back to single-stream download.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFile_ParallelRange_ChunkValidatorMismatch_ShouldFallbackToSingleStreamAsync()
    {
        var totalBytes = AppUpdateConstants.DownloadChunkSizeBytes * 2;
        var sourceBytes = new byte[totalBytes];
        new Random(88).NextBytes(sourceBytes);

        var handler = new TestHttpMessageHandler(request =>
        {
            var range = request.Headers.Range?.Ranges.FirstOrDefault();
            if (range is { From: 0, To: 0 })
            {
                var probeResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent([sourceBytes[0]]),
                    RequestMessage = request,
                };
                probeResponse.Headers.ETag = new EntityTagHeaderValue("\"probe-etag\"");
                probeResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, totalBytes) { Unit = "bytes" };
                return probeResponse;
            }

            if (range is { From: { } from, To: { } to })
            {
                // Return mismatched ETag (e.g., origin updated resource mid-flight)
                var chunkResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(new byte[to - from + 1]),
                    RequestMessage = request,
                };
                chunkResponse.Headers.ETag = new EntityTagHeaderValue("\"modified-etag\"");
                chunkResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, totalBytes) { Unit = "bytes" };
                return chunkResponse;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(sourceBytes),
                RequestMessage = request,
            };
        });

        var downloader = new FastHttpClientFileDownloader(_mockLogger.Object, handler);
        var targetFile = Path.Combine(_tempDirectory, "mismatched-etag.bin");

        await downloader.DownloadFile("https://example.com/mismatched-etag.bin", targetFile, _ => { }, null, 30);

        Assert.True(File.Exists(targetFile));
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(targetFile));
    }

    /// <summary>
    /// Tests that an unsafe initial download URL (e.g. private network) throws SecurityException.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFile_WithUnsafeInitialUrl_ShouldThrowSecurityExceptionAsync()
    {
        var downloader = new FastHttpClientFileDownloader(_mockLogger.Object);
        var targetFile = Path.Combine(_tempDirectory, "unsafe-url.bin");

        await Assert.ThrowsAsync<SecurityException>(
            () => downloader.DownloadFile("http://192.168.1.1/exploit.bin", targetFile, _ => { }, null, 30));

        Assert.False(File.Exists(targetFile));
    }

    /// <summary>
    /// Tests that an unsafe redirect target throws SecurityException and does not fall back to default downloader.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFile_WithUnsafeRedirectTarget_ShouldThrowSecurityExceptionAndNotFallbackAsync()
    {
        var handler = new TestHttpMessageHandler(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4]),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://192.168.1.1/private-exploit.bin"),
            };
            return response;
        });

        var downloader = new FastHttpClientFileDownloader(_mockLogger.Object, handler);
        var targetFile = Path.Combine(_tempDirectory, "unsafe-redirect.bin");

        await Assert.ThrowsAsync<SecurityException>(
            () => downloader.DownloadFile("https://example.com/file.bin", targetFile, _ => { }, null, 30));

        Assert.False(File.Exists(targetFile));
    }

    /// <summary>
    /// Tests that a loopback URL is rejected with a SecurityException.
    /// </summary>
    /// <param name="loopbackUrl">The loopback URL to test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Theory]
    [InlineData("http://127.0.0.1:8080/releases.win.json")]
    [InlineData("https://127.0.0.1:8443/releases.win.json")]
    [InlineData("http://localhost:8080/releases.win.json")]
    [InlineData("https://localhost:8443/releases.win.json")]
    public async Task DownloadFile_WithLoopbackUrl_ThrowsSecurityExceptionAsync(string loopbackUrl)
    {
        var downloader = new FastHttpClientFileDownloader(_mockLogger.Object);
        var targetFile = Path.Combine(_tempDirectory, "loopback.bin");

        await Assert.ThrowsAsync<SecurityException>(
            () => downloader.DownloadFile(loopbackUrl, targetFile, _ => { }, null, 30));
    }

    /// <summary>
    /// Tests that when DNS resolution fails due to an unsafe address (wrapped in HttpRequestException),
    /// the exception is not swallowed by the fallback block and base.DownloadFile is not invoked.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFile_UnsafeAddressInHttpRequestException_ShouldNotFallbackToBaseDownloaderAndRethrowAsync()
    {
        var handler = new TestHttpMessageHandler(_ =>
            throw new HttpRequestException("Connection failed", new SecurityException("Host 'example.com' resolved to an unsafe or reserved IP address.")));

        var downloader = new FastHttpClientFileDownloader(_mockLogger.Object, handler);
        var targetFile = Path.Combine(_tempDirectory, "ssrf-dns.bin");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => downloader.DownloadFile("https://example.com/file.bin", targetFile, _ => { }, null, 30));

        Assert.IsType<SecurityException>(ex.InnerException);
        Assert.False(File.Exists(targetFile));
    }

    /// <summary>
    /// Tests that an AggregateException containing a SecurityException is not caught by the fallback catch block.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFile_AggregateSecurityException_ShouldNotFallbackToBaseDownloaderAsync()
    {
        var handler = new TestHttpMessageHandler(_ =>
            throw new AggregateException(new SecurityException("Blocked by security validation.")));

        var downloader = new FastHttpClientFileDownloader(_mockLogger.Object, handler);
        var targetFile = Path.Combine(_tempDirectory, "ssrf-agg.bin");

        await Assert.ThrowsAsync<AggregateException>(
            () => downloader.DownloadFile("https://example.com/file.bin", targetFile, _ => { }, null, 30));

        Assert.False(File.Exists(targetFile));
    }
}
