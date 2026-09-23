using GenHub.Common.Services;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http.Headers;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>
/// Contains unit tests for the <see cref="DownloadService"/> class.
/// </summary>
public class DownloadServiceTests
{
    /// <summary>
    /// Creates a <see cref="DownloadService"/> instance with a mocked <see cref="ILogger{DownloadService}"/> and <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="handler">The HTTP message handler to use.</param>
    /// <param name="loggerMock">The mock logger output.</param>
    /// <param name="hashProvider">The hash provider to use (optional).</param>
    /// <returns>A new <see cref="DownloadService"/> instance.</returns>
    public static DownloadService CreateService(HttpMessageHandler handler, out Mock<ILogger<DownloadService>> loggerMock, IFileHashProvider? hashProvider = null)
    {
        loggerMock = new Mock<ILogger<DownloadService>>();
        var httpClient = new HttpClient(handler);
        var hashProviderInstance = hashProvider ?? new Sha256HashProvider();
        return new DownloadService(loggerMock.Object, httpClient, hashProviderInstance);
    }

    /// <summary>
    /// Verifies that a successful download writes the file and returns a successful result.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_SuccessfulDownload_WritesFileAndReturnsSuccessAsync()
    {
        // Arrange
        var fileContent = new byte[] { 1, 2, 3, 4, 5 };
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(fileContent),
            });
        var service = CreateService(handler.Object, out _);
        var tempFile = Path.GetTempFileName();
        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/file.bin"),
                DestinationPath = tempFile,
                OverwriteExisting = true,
            };

            // Act
            var result = await service.DownloadFileAsync(config);

            // Assert
            Assert.True(result.Success);
            Assert.True(File.Exists(tempFile));
            Assert.Equal(fileContent, File.ReadAllBytes(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Verifies that validated redirects follow each hop and download the final target.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_WithValidatedRedirects_FollowsRedirectChainAsync()
    {
        // Arrange
        var fileContent = new byte[] { 1, 2, 3, 4, 5 };
        var redirect = new HttpResponseMessage(HttpStatusCode.Found);
        redirect.Headers.Location = new Uri("http://test/final.bin");
        var responses = new Queue<HttpResponseMessage>(
        [
            redirect,
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fileContent) },
        ]);
        int requestsSent = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback(() => requestsSent++)
            .ReturnsAsync((HttpRequestMessage _, CancellationToken __) => responses.Dequeue());
        var validator = new Mock<IDownloadUrlValidator>();
        validator.Setup(v => v.IsSafeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var service = new DownloadService(
            Mock.Of<ILogger<DownloadService>>(),
            new HttpClient(handler.Object),
            new Sha256HashProvider(),
            validator.Object);
        var tempFile = Path.GetTempFileName();
        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/file.bin"),
                DestinationPath = tempFile,
                ValidateRedirectsManually = true,
            };

            // Act
            var result = await service.DownloadFileAsync(config);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(fileContent, File.ReadAllBytes(tempFile));
            Assert.Equal(2, requestsSent);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Verifies that validated redirects block unsafe targets without sending any request.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_WithBlockedTarget_ReturnsFailureAsync()
    {
        // Arrange
        int requestsSent = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback(() => requestsSent++)
            .ReturnsAsync((HttpRequestMessage _, CancellationToken __) => new HttpResponseMessage(HttpStatusCode.OK));
        var validator = new Mock<IDownloadUrlValidator>();
        validator.Setup(v => v.IsSafeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var service = new DownloadService(
            Mock.Of<ILogger<DownloadService>>(),
            new HttpClient(handler.Object),
            new Sha256HashProvider(),
            validator.Object);
        var tempFile = Path.GetTempFileName();
        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://192.168.1.9/file.bin"),
                DestinationPath = tempFile,
                ValidateRedirectsManually = true,
                RetryDelay = TimeSpan.Zero,
            };

            // Act
            var result = await service.DownloadFileAsync(config);

            // Assert
            Assert.False(result.Success);
            Assert.Equal(0, requestsSent);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Verifies that hash verification fails and deletes the file if the hash does not match.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_HashVerification_FailsOnWrongHashAsync()
    {
        // Arrange
        var fileContent = new byte[] { 1, 2, 3 };
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage _, CancellationToken __) =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(fileContent),
                });
        var service = CreateService(handler.Object, out _);
        var tempFile = Path.GetTempFileName();
        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/file.bin"),
                DestinationPath = tempFile,
                ExpectedHash = "deadbeef",
            };

            // Act
            var result = await service.DownloadFileAsync(config);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Hash verification failed", result.AllErrors);

            // File should be deleted by the service if hash fails
            Assert.False(File.Exists(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Verifies that the download service retries on failure and returns a failed result after max attempts.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_RetriesOnFailure_AndReturnsFailedResultAsync()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        int callCount = 0;
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback(() => callCount++)
            .ThrowsAsync(new HttpRequestException("Network error"));
        var service = CreateService(handler.Object, out _);
        var tempFile = Path.GetTempFileName();
        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/file.bin"),
                DestinationPath = tempFile,
                MaxRetryAttempts = 2,
                RetryDelay = TimeSpan.Zero,
            };

            // Act
            var result = await service.DownloadFileAsync(config);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.Success);
            Assert.Contains("Download failed after", result.AllErrors);
            Assert.Equal(2, callCount);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Verifies that ComputeFileHashAsync returns the correct SHA256 hash for a file.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ComputeFileHashAsync_ReturnsCorrectHashAsync()
    {
        // Arrange
        var bytes = new byte[] { 1, 2, 3, 4 };
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, bytes);
            var handler = new Mock<HttpMessageHandler>();
            var hashProvider = new Sha256HashProvider();
            var service = CreateService(handler.Object, out _, hashProvider);

            // Act
            var hash = await service.ComputeFileHashAsync(tempFile);

            // Assert
            var expected = BitConverter.ToString(System.Security.Cryptography.SHA256.HashData(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            Assert.Equal(expected, hash);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Verifies that when a partial file exists, the service sends an HTTP Range request and resumes via 206 Partial Content.
    /// </summary>
    [Fact]
    public async Task DownloadFileAsync_WithExistingPartialFile_ResumesDownloadViaRangeAsync()
    {
        // Arrange: partial file has first 3 bytes [1, 2, 3]
        var existingContent = new byte[] { 1, 2, 3 };
        var remainingContent = new byte[] { 4, 5 };
        var tempFile = Path.GetTempFileName();
        File.WriteAllBytes(tempFile, existingContent);

        HttpRequestMessage? capturedRequest = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(() =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(remainingContent),
                };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(3, 4, 5);
                return response;
            });

        var service = CreateService(handler.Object, out _);

        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/resume.bin"),
                DestinationPath = tempFile,
                EnableResumption = true,
            };

            // Act
            var result = await service.DownloadFileAsync(config);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(capturedRequest);
            Assert.NotNull(capturedRequest.Headers.Range);
            Assert.Equal(3, capturedRequest.Headers.Range.Ranges.First().From);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, File.ReadAllBytes(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Verifies that when a server does not support Range and returns 200 OK, the partial file is cleanly overwritten.
    /// </summary>
    [Fact]
    public async Task DownloadFileAsync_WithExistingPartialFile_ServerReturns200_OverwritesFromBeginningAsync()
    {
        // Arrange: existing file has stale data [99, 99, 99]
        var tempFile = Path.GetTempFileName();
        File.WriteAllBytes(tempFile, new byte[] { 99, 99, 99 });
        var fullContent = new byte[] { 1, 2, 3, 4, 5 };

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(fullContent),
            });

        var service = CreateService(handler.Object, out _);

        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/no-range.bin"),
                DestinationPath = tempFile,
                EnableResumption = true,
            };

            // Act
            var result = await service.DownloadFileAsync(config);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(fullContent, File.ReadAllBytes(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Verifies that when the server returns 416 Range Not Satisfiable, the service deletes the stale file and restarts.
    /// </summary>
    [Fact]
    public async Task DownloadFileAsync_WithExistingPartialFile_ServerReturns416_RetriesFromScratchAsync()
    {
        // Arrange: existing file is larger than server resource
        var tempFile = Path.GetTempFileName();
        File.WriteAllBytes(tempFile, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        var fullContent = new byte[] { 1, 2, 3 };

        int requestCount = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                requestCount++;
                if (requestCount == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(fullContent),
                };
            });

        var service = CreateService(handler.Object, out _);

        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/range-416.bin"),
                DestinationPath = tempFile,
                EnableResumption = true,
            };

            // Act
            var result = await service.DownloadFileAsync(config);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(2, requestCount);
            Assert.Equal(fullContent, File.ReadAllBytes(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Verifies that when a file already exists with matching expected hash, download is skipped immediately.
    /// </summary>
    [Fact]
    public async Task DownloadFileAsync_FileExistsWithMatchingHash_SkipsDownloadAsync()
    {
        // Arrange
        var content = new byte[] { 10, 20, 30, 40 };
        var tempFile = Path.GetTempFileName();
        File.WriteAllBytes(tempFile, content);

        var hashProvider = new Sha256HashProvider();
        var expectedHash = await hashProvider.ComputeFileHashAsync(tempFile);

        var handler = new Mock<HttpMessageHandler>();
        var service = CreateService(handler.Object, out _, hashProvider);

        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/existing-match.bin"),
                DestinationPath = tempFile,
                ExpectedHash = expectedHash,
            };

            // Act
            var result = await service.DownloadFileAsync(config);

            // Assert
            Assert.True(result.Success);
            Assert.True(result.IsSkipped);
            handler.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
