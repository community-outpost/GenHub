using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Features.AppUpdate.Services;
using Microsoft.Extensions.Logging;
using Moq;
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
                probeResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, totalBytes) { Unit = "bytes" };
                return probeResponse;
            }

            if (range is { From: { } from, To: { } to })
            {
                var length = (int)(to - from + 1);
                var chunkData = new byte[length];
                Array.Copy(sourceBytes, from, chunkData, 0, length);

                var chunkResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(chunkData),
                    RequestMessage = request,
                };
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
                // Probe response indicates 1MB file
                var probeResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent([smallBytes[0]]),
                    RequestMessage = request,
                };
                probeResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, smallBytes.Length) { Unit = "bytes" };
                return probeResponse;
            }

            if (range is not null)
            {
                Interlocked.Increment(ref chunkRequestsCount);
            }

            var fullResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(smallBytes),
                RequestMessage = request,
            };
            return fullResponse;
        });

        var downloader = new FastHttpClientFileDownloader(_mockLogger.Object, handler);
        var targetFile = Path.Combine(_tempDirectory, "small-file.bin");

        await downloader.DownloadFile("https://example.com/small.bin", targetFile, _ => { }, null, 30);

        Assert.True(File.Exists(targetFile));
        var downloadedBytes = await File.ReadAllBytesAsync(targetFile);
        Assert.Equal(smallBytes, downloadedBytes);
        Assert.Equal(0, chunkRequestsCount);
    }

    /// <summary>
    /// Tests that when the server ignores range headers (returning 200 OK on probe), the downloader streams directly without error.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFile_ServerIgnoresRange_ShouldStreamProbeResponseDirectlyAsync()
    {
        var fileBytes = new byte[1024 * 512]; // 512 KB
        new Random(1337).NextBytes(fileBytes);

        var handler = new TestHttpMessageHandler(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(fileBytes),
                RequestMessage = request,
            };
            return response;
        });

        var downloader = new FastHttpClientFileDownloader(_mockLogger.Object, handler);
        var targetFile = Path.Combine(_tempDirectory, "ignored-range.bin");

        await downloader.DownloadFile("https://example.com/file.bin", targetFile, _ => { }, null, 30);

        Assert.True(File.Exists(targetFile));
        var downloadedBytes = await File.ReadAllBytesAsync(targetFile);
        Assert.Equal(fileBytes, downloadedBytes);
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
}
