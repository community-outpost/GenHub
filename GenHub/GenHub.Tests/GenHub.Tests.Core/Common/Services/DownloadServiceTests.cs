using System.Net;
using GenHub.Common.Services;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

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
    public async Task DownloadFileAsync_SuccessfulDownload_WritesFileAndReturnsSuccess()
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
    /// Verifies that hash verification fails and deletes the file if the hash does not match.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadFileAsync_HashVerification_FailsOnWrongHash()
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
    public async Task DownloadFileAsync_RetriesOnFailure_AndReturnsFailedResult()
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
    public async Task ComputeFileHashAsync_ReturnsCorrectHash()
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
}
