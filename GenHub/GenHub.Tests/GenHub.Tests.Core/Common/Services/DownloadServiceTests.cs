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
            };

            // Act
            var result = await service.DownloadFileAsync(config);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(fileContent.Length, result.BytesDownloaded);
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
    /// Verifies that a hash mismatch causes the download to fail and deletes the file.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_HashMismatch_FailsAndDeleteFileAsync()
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
                ExpectedHash = "invalid_hash",
            };

            // Act
            var result = await service.DownloadFileAsync(config);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Hash verification failed", result.FirstError);
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
    /// Verifies that an HTTP error triggers retries and ultimately fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_HttpError_RetriesAndFailsAsync()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var service = CreateService(handler.Object, out _);
        var tempFile = Path.GetTempFileName();

        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/file.bin"),
                DestinationPath = tempFile,
                MaxRetryAttempts = 3,
                RetryDelay = TimeSpan.FromMilliseconds(10),
            };

            // Act
            var result = await service.DownloadFileAsync(config);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("500", result.FirstError);
            handler.Protected().Verify(
                "SendAsync",
                Times.Exactly(3), // 1 initial + 2 retries
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

    /// <summary>
    /// Verifies that the download throws OperationCanceledException when cancellation is requested.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_CancellationRequested_ThrowsOperationCanceledExceptionAsync()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((_, ct) => Task.FromCanceled<HttpResponseMessage>(ct));

        var service = CreateService(handler.Object, out _);
        var tempFile = Path.GetTempFileName();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/file.bin"),
                DestinationPath = tempFile,
            };

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.DownloadFileAsync(config, cancellationToken: cts.Token));
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
    /// Verifies that progress reporting works as expected.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_ReportsProgress_SuccessAsync()
    {
        // Arrange
        var fileContent = new byte[1024];
        new Random().NextBytes(fileContent);

        var handler = new Mock<HttpMessageHandler>();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(fileContent),
        };
        response.Content.Headers.ContentLength = fileContent.Length;

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        var service = CreateService(handler.Object, out _);
        var tempFile = Path.GetTempFileName();
        var progressReports = new List<DownloadProgress>();
        var progress = new Progress<DownloadProgress>(p => progressReports.Add(p));

        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/file.bin"),
                DestinationPath = tempFile,
                BufferSize = 256,
                ProgressReportingInterval = TimeSpan.Zero,
            };

            // Act
            var result = await service.DownloadFileAsync(config, progress: progress);

            // Assert
            Assert.True(result.Success);
            Assert.True(progressReports.Count > 0);
            var lastReport = progressReports.Last();
            Assert.Equal(fileContent.Length, lastReport.BytesReceived);
            Assert.Equal(fileContent.Length, lastReport.TotalBytes);
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
    /// Verifies that ComputeFileHashAsync calculates the SHA-256 hash properly.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ComputeFileHashAsync_CalculatesSha256CorrectlyAsync()
    {
        // Arrange
        var bytes = "Test string for hashing"u8.ToArray();
        var tempFile = Path.GetTempFileName();
        await File.WriteAllBytesAsync(tempFile, bytes);

        var handler = new Mock<HttpMessageHandler>();
        try
        {
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
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_WithExistingPartialFile_ResumesDownloadViaRangeAsync()
    {
        // Arrange: partial file has first 3 bytes [1, 2, 3]
        var existingContent = new byte[] { 1, 2, 3 };
        var remainingContent = new byte[] { 4, 5 };
        var tempFile = Path.GetTempFileName();
        File.WriteAllBytes(tempFile, existingContent);
        File.WriteAllText($"{tempFile}.etag", "\"sample-etag\"");

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
                Headers = { { "ETag", "\"sample-etag\"" } },
            };

            // Act
            var result = await service.DownloadFileAsync(config);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(capturedRequest);
            Assert.NotNull(capturedRequest.Headers.Range);
            Assert.Equal(3, capturedRequest.Headers.Range.Ranges.First().From);
            Assert.NotNull(capturedRequest.Headers.IfRange);
            Assert.Equal("\"sample-etag\"", capturedRequest.Headers.IfRange.EntityTag?.Tag);
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
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
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
                Headers = { { "ETag", "\"sample-etag\"" } },
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
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_WithExistingPartialFile_ServerReturns416_RetriesFromScratchAsync()
    {
        // Arrange: existing file is larger than server resource
        var tempFile = Path.GetTempFileName();
        File.WriteAllBytes(tempFile, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        File.WriteAllText($"{tempFile}.etag", "\"sample-etag\"");
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
                Headers = { { "ETag", "\"sample-etag\"" } },
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
    /// Verifies that when an existing partial file exists but no ETag header is provided, resumption is skipped.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_WithExistingPartialFile_WithoutETag_OverwritesFromBeginningAsync()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllBytes(tempFile, new byte[] { 99, 99, 99 });
        var fullContent = new byte[] { 1, 2, 3, 4, 5 };

        HttpRequestMessage? capturedRequest = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(fullContent),
            });

        var service = CreateService(handler.Object, out _);

        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/no-etag.bin"),
                DestinationPath = tempFile,
                EnableResumption = true,
            };

            // Act
            var result = await service.DownloadFileAsync(config);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(capturedRequest);
            Assert.Null(capturedRequest.Headers.Range);
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
    /// Verifies that supplying an ETag header in configuration does not throw an InvalidOperationException.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_WithETagHeader_DoesNotThrowMisusedHeaderExceptionAsync()
    {
        var tempFile = Path.GetTempFileName();
        var fullContent = new byte[] { 1, 2, 3 };

        HttpRequestMessage? capturedRequest = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(fullContent),
            });

        var service = CreateService(handler.Object, out _);

        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/etag-test.bin"),
                DestinationPath = tempFile,
                Headers = { { "ETag", "\"test-etag\"" } },
            };

            var result = await service.DownloadFileAsync(config);

            Assert.True(result.Success);
            Assert.NotNull(capturedRequest);
            Assert.False(capturedRequest.Headers.Contains("ETag"));
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
    /// Verifies that when 206 Partial Content is returned but ContentRange.From does not match existing bytes,
    /// the service retries from scratch.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_WithPartialContentAndContentRangeMismatch_DeletesAndRestartsAsync()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllBytes(tempFile, new byte[] { 1, 2, 3 });
        File.WriteAllText($"{tempFile}.etag", "\"etag-1\"");
        var fullContent = new byte[] { 1, 2, 3, 4, 5 };

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
                    // Server returns 206 but ContentRange From is 0 (mismatch with 3)
                    var badResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                    {
                        Content = new ByteArrayContent(new byte[] { 9, 9 }),
                    };
                    badResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 1, 5);
                    return badResponse;
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
                Url = new Uri("http://test/range-mismatch.bin"),
                DestinationPath = tempFile,
                EnableResumption = true,
                Headers = { { "ETag", "\"etag-1\"" } },
            };

            var result = await service.DownloadFileAsync(config);

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
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
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
            Assert.True(result.HashVerified);
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

    /// <summary>
    /// Verifies that when a resumed download ends early before total bytes are reached, the attempt fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_ResumedDownloadEndsEarly_FailsAsync()
    {
        // Arrange: partial file has 3 bytes, server announces range 3-4/5 (2 bytes) but stream only yields 1 byte
        var existingContent = new byte[] { 1, 2, 3 };
        var truncatedContent = new byte[] { 4 }; // missing byte 5
        var tempFile = Path.GetTempFileName();
        File.WriteAllBytes(tempFile, existingContent);
        File.WriteAllText($"{tempFile}.etag", "\"sample-etag\"");

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new CustomStreamingContent(truncatedContent, 2),
                };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(3, 4, 5);
                return response;
            });

        var service = CreateService(handler.Object, out _);

        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/truncated-resume.bin"),
                DestinationPath = tempFile,
                EnableResumption = true,
                MaxRetryAttempts = 1,
                Headers = { { "ETag", "\"sample-etag\"" } },
            };

            // Act
            var result = await service.DownloadFileAsync(config);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Resumed response body does not match its declared range", result.FirstError);
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
    /// Verifies that an unquoted ETag header in configuration is parsed correctly and formatted into If-Range.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_WithUnquotedETagHeader_SendsQuotedIfRangeAsync()
    {
        var existingContent = new byte[] { 1, 2, 3 };
        var remainingContent = new byte[] { 4, 5 };
        var tempFile = Path.GetTempFileName();
        File.WriteAllBytes(tempFile, existingContent);
        File.WriteAllText($"{tempFile}.etag", "\"raw-hex-etag-value\"");

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
                Url = new Uri("http://test/unquoted-etag.bin"),
                DestinationPath = tempFile,
                EnableResumption = true,
                Headers = { { "etag", "raw-hex-etag-value" } }, // unquoted, lower-case key
            };

            // Act
            var result = await service.DownloadFileAsync(config);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(capturedRequest);
            Assert.NotNull(capturedRequest.Headers.IfRange);
            Assert.Equal("\"raw-hex-etag-value\"", capturedRequest.Headers.IfRange.EntityTag?.Tag);
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
    /// Verifies that when the server returns HTML Content-Type for a binary file (such as .zip),
    /// the download fails immediately with an InvalidDataException error message without unnecessary retries.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_ServerReturnsHtmlForZipTarget_FailsImmediatelyAsync()
    {
        var htmlContent = "<!DOCTYPE html><html><body>Sign In</body></html>"u8.ToArray();
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
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(htmlContent),
                };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
                return response;
            });

        var service = CreateService(handler.Object, out _);
        var tempZip = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.zip");

        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/archive.zip"),
                DestinationPath = tempZip,
                MaxRetryAttempts = 3,
            };

            // Act
            var result = await service.DownloadFileAsync(config);

            // Assert: Failed immediately on 1st attempt, rejected HTML, did not write corrupt zip
            Assert.False(result.Success);
            Assert.Equal(1, requestCount);
            Assert.Contains("Download server returned HTML (text/html)", result.FirstError);
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }
        }
    }

    /// <summary>
    /// Verifies that when an existing partial file exists with ETag config but without matching sidecar, resumption is skipped.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_WithExistingPartialFile_WithoutSidecarEtag_OverwritesFromBeginningAsync()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllBytes(tempFile, new byte[] { 99, 99, 99 });
        var fullContent = new byte[] { 1, 2, 3, 4, 5 };

        HttpRequestMessage? capturedRequest = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(fullContent),
            });

        var service = CreateService(handler.Object, out _);

        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/no-sidecar.bin"),
                DestinationPath = tempFile,
                EnableResumption = true,
                Headers = { { "ETag", "\"sample-etag\"" } },
            };

            var result = await service.DownloadFileAsync(config);

            Assert.True(result.Success);
            Assert.NotNull(capturedRequest);
            Assert.Null(capturedRequest.Headers.Range);
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
    /// Verifies that when a resumed 206 response yields more bytes than declared by Content-Range, the operation fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_ResumedContentExceedsDeclaredRange_FailsAsync()
    {
        var existingContent = new byte[] { 1, 2, 3 };
        var overflowingContent = new byte[] { 4, 5, 6, 7 }; // 4 bytes instead of declared 2 bytes (3..4)
        var tempFile = Path.GetTempFileName();
        File.WriteAllBytes(tempFile, existingContent);
        File.WriteAllText($"{tempFile}.etag", "\"sample-etag\"");

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new CustomStreamingContent(overflowingContent, 2),
                };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(3, 4, 5);
                return response;
            });

        var service = CreateService(handler.Object, out _);

        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/overflow-resume.bin"),
                DestinationPath = tempFile,
                EnableResumption = true,
                MaxRetryAttempts = 1,
                Headers = { { "ETag", "\"sample-etag\"" } },
            };

            var result = await service.DownloadFileAsync(config);

            Assert.False(result.Success);
            Assert.Contains("Resumed response body exceeds its declared range", result.FirstError);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }

            if (File.Exists($"{tempFile}.etag"))
            {
                File.Delete($"{tempFile}.etag");
            }
        }
    }

    /// <summary>
    /// Verifies that when a resumed partial download response provides a Content-Length header
    /// that does not match the byte range declared in Content-Range, the download fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_ResumedContentLengthMismatch_FailsAsync()
    {
        var existingContent = new byte[] { 1, 2, 3 };
        var payload = new byte[] { 4 };
        var tempFile = Path.GetTempFileName();
        File.WriteAllBytes(tempFile, existingContent);
        File.WriteAllText($"{tempFile}.etag", "\"sample-etag\"");

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(payload),
                };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(3, 4, 5);
                return response;
            });

        var service = CreateService(handler.Object, out _);

        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/mismatch-resume.bin"),
                DestinationPath = tempFile,
                EnableResumption = true,
                MaxRetryAttempts = 1,
                Headers = { { "ETag", "\"sample-etag\"" } },
            };

            var result = await service.DownloadFileAsync(config);

            Assert.False(result.Success);
            Assert.Contains("Resumed response Content-Length does not match Content-Range", result.FirstError);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }

            if (File.Exists($"{tempFile}.etag"))
            {
                File.Delete($"{tempFile}.etag");
            }
        }
    }

    /// <summary>
    /// Verifies that when a file meets the parallel download threshold and server supports byte ranges,
    /// it downloads the file using parallel chunks.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_WhenServerSupportsRangesAndFileIsLarge_DownloadsInParallelChunksAsync()
    {
        const int totalBytes = 16 * 1024 * 1024;
        var chunk1Data = new byte[8 * 1024 * 1024];
        var chunk2Data = new byte[8 * 1024 * 1024];
        Array.Fill(chunk1Data, (byte)1);
        Array.Fill(chunk2Data, (byte)2);

        var tempFile = Path.Combine(Path.GetTempPath(), $"parallel_{Guid.NewGuid():N}.bin");
        var chunkRequestsCount = 0;

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                if (request.Headers.Range == null)
                {
                    var initialResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(Array.Empty<byte>()),
                    };
                    initialResponse.Content.Headers.ContentLength = totalBytes;
                    initialResponse.Headers.AcceptRanges.Add("bytes");
                    initialResponse.Headers.ETag = new EntityTagHeaderValue("\"etag-12345\"");
                    return initialResponse;
                }

                Interlocked.Increment(ref chunkRequestsCount);
                var range = request.Headers.Range.Ranges.First();
                var from = range.From!.Value;
                var to = range.To!.Value;

                var data = from == 0 ? chunk1Data : chunk2Data;
                var chunkResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(data),
                };
                chunkResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, totalBytes);
                return chunkResponse;
            });

        var service = CreateService(handler.Object, out _);

        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/largefile.bin"),
                DestinationPath = tempFile,
                EnableParallelDownload = true,
                ParallelConcurrency = 2,
            };

            var result = await service.DownloadFileAsync(config);

            Assert.True(result.Success);
            Assert.Equal(totalBytes, result.BytesDownloaded);
            Assert.Equal(2, chunkRequestsCount);

            var written = File.ReadAllBytes(tempFile);
            Assert.Equal(totalBytes, written.Length);
            Assert.Equal(1, written[0]);
            Assert.Equal(2, written[^1]);
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
    /// Verifies that when parallel chunk download fails, the service falls back gracefully to sequential download.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_WhenParallelChunkFails_FallsBackToSequentialDownloadAsync()
    {
        const int totalBytes = 16 * 1024 * 1024;
        var fullData = new byte[totalBytes];
        Array.Fill(fullData, (byte)7);

        var tempFile = Path.Combine(Path.GetTempPath(), $"fallback_{Guid.NewGuid():N}.bin");
        var attempts = 0;

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                if (request.Headers.Range != null)
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }

                attempts++;
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(fullData),
                };
                response.Content.Headers.ContentLength = totalBytes;
                if (attempts == 1)
                {
                    response.Headers.AcceptRanges.Add("bytes");
                    response.Headers.ETag = new EntityTagHeaderValue("\"etag-fallback\"");
                }

                return response;
            });

        var service = CreateService(handler.Object, out _);

        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/fallback.bin"),
                DestinationPath = tempFile,
                EnableParallelDownload = true,
                ParallelConcurrency = 2,
            };

            var result = await service.DownloadFileAsync(config);

            Assert.True(result.Success);
            Assert.Equal(totalBytes, result.BytesDownloaded);
            Assert.True(File.Exists(tempFile));
            Assert.Equal(totalBytes, new FileInfo(tempFile).Length);
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
    /// Verifies that if an un-ranged GET returns 206 Partial Content or Content-Range, the download fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_WhenUnrangedRequestReceives206_FailsAsync()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"partial_{Guid.NewGuid():N}.bin");
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(new byte[] { 1, 2, 3 }),
                };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 2, 100);
                return response;
            });

        var service = CreateService(handler.Object, out _);

        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/file.bin"),
                DestinationPath = tempFile,
                MaxRetryAttempts = 1,
            };

            var result = await service.DownloadFileAsync(config);

            Assert.False(result.Success);
            Assert.Contains("Expected HTTP 200 OK without Content-Range for full download", result.FirstError);
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
    /// Verifies that parallel chunk requests send If-Range matching the initial connection's ETag.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_ParallelDownload_PassesETagAsIfRangeToChunksAsync()
    {
        const int totalBytes = 16 * 1024 * 1024;
        var chunk1Data = new byte[8 * 1024 * 1024];
        var chunk2Data = new byte[8 * 1024 * 1024];
        var tempFile = Path.Combine(Path.GetTempPath(), $"ifrange_{Guid.NewGuid():N}.bin");
        var chunkIfRanges = new List<string?>();

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                if (request.Headers.Range == null)
                {
                    var initialResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(Array.Empty<byte>()),
                    };
                    initialResponse.Content.Headers.ContentLength = totalBytes;
                    initialResponse.Headers.AcceptRanges.Add("bytes");
                    initialResponse.Headers.ETag = new EntityTagHeaderValue("\"etag-12345\"");
                    return initialResponse;
                }

                lock (chunkIfRanges)
                {
                    chunkIfRanges.Add(request.Headers.IfRange?.EntityTag?.Tag);
                }

                var range = request.Headers.Range.Ranges.First();
                var from = range.From!.Value;
                var to = range.To!.Value;

                var data = from == 0 ? chunk1Data : chunk2Data;
                var chunkResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(data),
                };
                chunkResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, totalBytes);
                return chunkResponse;
            });

        var service = CreateService(handler.Object, out _);

        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/largefile.bin"),
                DestinationPath = tempFile,
                EnableParallelDownload = true,
                ParallelConcurrency = 2,
            };

            var result = await service.DownloadFileAsync(config);

            Assert.True(result.Success);
            Assert.Equal(2, chunkIfRanges.Count);
            Assert.All(chunkIfRanges, tag => Assert.Equal("\"etag-12345\"", tag));
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
    /// Verifies that if a parallel chunk returns a Content-Range length that mismatches totalBytes, fallback to sequential download occurs.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_WhenChunkContentRangeLengthMismatchesTotalBytes_FallsBackToSequentialAsync()
    {
        const int totalBytes = 16 * 1024 * 1024;
        var fullPayload = new byte[totalBytes];
        Array.Fill(fullPayload, (byte)7);

        var tempFile = Path.Combine(Path.GetTempPath(), $"mismatch_{Guid.NewGuid():N}.bin");
        var sequentialRequested = false;

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                if (request.Headers.Range == null)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(sequentialRequested ? fullPayload : Array.Empty<byte>()),
                    };
                    response.Content.Headers.ContentLength = totalBytes;
                    response.Headers.AcceptRanges.Add("bytes");
                    response.Headers.ETag = new EntityTagHeaderValue("\"etag-mismatch\"");
                    sequentialRequested = true;
                    return response;
                }

                // Return a chunk where Content-Range total length disagrees with totalBytes
                var range = request.Headers.Range.Ranges.First();
                var from = range.From!.Value;
                var to = range.To!.Value;
                var chunkData = new byte[to - from + 1];

                var chunkResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(chunkData),
                };

                // Disagreeing total length (totalBytes + 1000)
                chunkResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, totalBytes + 1000);
                return chunkResponse;
            });

        var service = CreateService(handler.Object, out _);

        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/mismatchedfile.bin"),
                DestinationPath = tempFile,
                EnableParallelDownload = true,
                ParallelConcurrency = 2,
            };

            var result = await service.DownloadFileAsync(config);

            Assert.True(result.Success);
            Assert.True(sequentialRequested);
            Assert.Equal(totalBytes, result.BytesDownloaded);
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
    /// Verifies that when redirecting from HTTPS to HTTP on the same host, the Authorization header is stripped.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_WhenRedirectedFromHttpsToHttpOnSameHost_StripsAuthorizationHeaderAsync()
    {
        const int totalBytes = 16 * 1024 * 1024;
        var chunk1Data = new byte[8 * 1024 * 1024];
        var chunk2Data = new byte[8 * 1024 * 1024];
        var tempFile = Path.Combine(Path.GetTempPath(), $"auth_strip_{Guid.NewGuid():N}.bin");
        var chunkAuthorizationHeaders = new List<string?>();

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                if (request.Headers.Range == null)
                {
                    var initialResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(Array.Empty<byte>()),
                    };
                    initialResponse.Content.Headers.ContentLength = totalBytes;
                    initialResponse.Headers.AcceptRanges.Add("bytes");
                    initialResponse.Headers.ETag = new EntityTagHeaderValue("\"etag-auth\"");

                    // Simulate redirect from https to http on same host
                    initialResponse.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://secure.example.com/file.bin");
                    return initialResponse;
                }

                lock (chunkAuthorizationHeaders)
                {
                    chunkAuthorizationHeaders.Add(request.Headers.Authorization?.ToString());
                }

                var range = request.Headers.Range.Ranges.First();
                var from = range.From!.Value;
                var to = range.To!.Value;
                var data = from == 0 ? chunk1Data : chunk2Data;

                var chunkResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(data),
                };
                chunkResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, totalBytes);
                return chunkResponse;
            });

        var service = CreateService(handler.Object, out _);

        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("https://secure.example.com/file.bin"),
                DestinationPath = tempFile,
                EnableParallelDownload = true,
                ParallelConcurrency = 2,
            };
            config.Headers["Authorization"] = "Bearer secret-token";

            var result = await service.DownloadFileAsync(config);

            Assert.True(result.Success);
            Assert.Equal(2, chunkAuthorizationHeaders.Count);
            Assert.All(chunkAuthorizationHeaders, auth => Assert.Null(auth));
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
    /// Verifies that when server supports ranges but lacks a strong representation validator and ExpectedHash is unset, sequential download is used.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_WhenServerSupportsRangesButLacksRepresentationValidator_UsesSequentialDownloadAsync()
    {
        const int totalBytes = 16 * 1024 * 1024;
        var fullPayload = new byte[totalBytes];
        Array.Fill(fullPayload, (byte)9);

        var tempFile = Path.Combine(Path.GetTempPath(), $"no_validator_{Guid.NewGuid():N}.bin");
        var chunkRangeRequested = false;

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                if (request.Headers.Range != null)
                {
                    chunkRangeRequested = true;
                }

                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(fullPayload),
                };
                response.Content.Headers.ContentLength = totalBytes;
                response.Headers.AcceptRanges.Add("bytes");
                return response;
            });

        var service = CreateService(handler.Object, out _);

        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/unvalidated.bin"),
                DestinationPath = tempFile,
                EnableParallelDownload = true,
                ParallelConcurrency = 2,
            };

            var result = await service.DownloadFileAsync(config);

            Assert.True(result.Success);
            Assert.False(chunkRangeRequested);
            Assert.Equal(totalBytes, result.BytesDownloaded);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private sealed class CustomStreamingContent(byte[] data, long? declaredContentLength) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(data, 0, data.Length);

        protected override bool TryComputeLength(out long length)
        {
            if (declaredContentLength.HasValue)
            {
                length = declaredContentLength.Value;
                return true;
            }

            length = 0;
            return false;
        }
    }
}
