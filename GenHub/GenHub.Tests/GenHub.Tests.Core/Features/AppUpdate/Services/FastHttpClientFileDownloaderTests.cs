using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Features.AppUpdate.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.AppUpdate.Services;

/// <summary>
/// Unit tests for <see cref="FastHttpClientFileDownloader"/>.
/// </summary>
public class FastHttpClientFileDownloaderTests
{
    private readonly Mock<ILogger<FastHttpClientFileDownloader>> _mockLogger = new();

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
        var targetFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}.tmp");

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
}
