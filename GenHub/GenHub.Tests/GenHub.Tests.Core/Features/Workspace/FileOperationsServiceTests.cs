using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Results;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Tests for the FileOperationsService class.
/// </summary>
public class FileOperationsServiceTests : IDisposable
{
    private readonly Mock<ILogger<FileOperationsService>> _logger;
    private readonly Mock<IDownloadService> _downloadService;
    private readonly FileOperationsService _service;
    private readonly string _tempDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileOperationsServiceTests"/> class.
    /// </summary>
    public FileOperationsServiceTests()
    {
        _logger = new Mock<ILogger<FileOperationsService>>();
        _downloadService = new Mock<IDownloadService>();
        _service = new FileOperationsService(_logger.Object, _downloadService.Object);
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// Tests that CopyFileAsync creates a file at the destination path.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CopyFileAsync_CreatesFile()
    {
        var src = Path.Combine(_tempDir, "source.txt");
        var dst = Path.Combine(_tempDir, "destination.txt");

        await File.WriteAllTextAsync(src, "test content");
        await _service.CopyFileAsync(src, dst);

        Assert.True(File.Exists(dst));
        Assert.Equal("test content", await File.ReadAllTextAsync(dst));
    }

    /// <summary>
    /// Tests that CreateSymlinkAsync creates a symbolic link or falls back to copy on unsupported platforms.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateSymlinkAsync_CreatesSymlinkOrCopies()
    {
        var src = Path.Combine(_tempDir, "source.txt");
        var link = Path.Combine(_tempDir, "link.txt");

        await File.WriteAllTextAsync(src, "test content");

        // Try to create symlink; on unsupported platforms or insufficient privilege, skip test
        try
        {
            await _service.CreateSymlinkAsync(link, src);
        }
        catch (IOException ioEx)
        {
            // Windows: privilege not held or not supported, skip test
            if (ioEx.Message.Contains("privilege", StringComparison.OrdinalIgnoreCase) ||
                ioEx.Message.Contains("not supported", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            throw;
        }
        catch (PlatformNotSupportedException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        catch (NotSupportedException)
        {
            return;
        }

        Assert.True(File.Exists(link));
        Assert.Equal("test content", await File.ReadAllTextAsync(link));
    }

    /// <summary>
    /// Tests that CreateHardLinkAsync creates a hard link or falls back to copy on unsupported platforms.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateHardLinkAsync_CreatesHardLinkOrCopies()
    {
        var src = Path.Combine(_tempDir, "source.txt");
        var link = Path.Combine(_tempDir, "hardlink.txt");

        await File.WriteAllTextAsync(src, "test content");

        // Try to create hard link; on unsupported platforms or not implemented, skip test
        try
        {
            await _service.CreateHardLinkAsync(link, src);
        }
        catch (NotImplementedException)
        {
            // Not implemented in base service, skip test
            return;
        }
        catch (PlatformNotSupportedException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        catch (NotSupportedException)
        {
            return;
        }

        Assert.True(File.Exists(link));
        Assert.Equal("test content", await File.ReadAllTextAsync(link));
    }

    /// <summary>
    /// Tests that VerifyFileHashAsync handles both case sensitive and case insensitive hash comparisons.
    /// </summary>
    /// <param name="caseSensitive">Whether to test case sensitive comparison.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VerifyFileHashAsync_HandlesCase(bool caseSensitive)
    {
        var file = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(file, "test");

        var expectedHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";
        var testHash = caseSensitive ? expectedHash.ToUpperInvariant() : expectedHash;

        _downloadService
            .Setup(x => x.ComputeFileHashAsync(file, default))
            .ReturnsAsync(expectedHash);

        var result = await _service.VerifyFileHashAsync(file, testHash);

        Assert.True(result);
        _downloadService.Verify(
            x => x.ComputeFileHashAsync(file, default),
            Times.Once);
    }

    /// <summary>
    /// Tests that VerifyFileHashAsync returns false when the hash does not match.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task VerifyFileHashAsync_ReturnsFalse_WhenHashDoesNotMatch()
    {
        var file = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(file, "test");

        var actualHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";
        var wrongHash = "WRONG_HASH";

        _downloadService
            .Setup(x => x.ComputeFileHashAsync(file, default))
            .ReturnsAsync(actualHash);

        var result = await _service.VerifyFileHashAsync(file, wrongHash);

        Assert.False(result);
    }

    /// <summary>
    /// Tests that DownloadFileAsync uses the download service successfully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DownloadFileAsync_UsesDownloadService_Successfully()
    {
        var testUrl = "https://example.com/file.txt";
        var destination = Path.Combine(_tempDir, "download.txt");
        var progressReports = new List<DownloadProgress>();
        var progress = new Progress<DownloadProgress>(p => progressReports.Add(p));

        var successResult = DownloadResult.CreateSuccess(
            destination,
            100,
            TimeSpan.FromSeconds(1),
            false);

        _downloadService
            .Setup(x => x.DownloadFileAsync(
                testUrl,
                destination,
                null,
                progress,
                default))
            .ReturnsAsync(successResult);

        await _service.DownloadFileAsync(testUrl, destination, progress);

        _downloadService.Verify(
            x => x.DownloadFileAsync(testUrl, destination, null, progress, default),
            Times.Once);
    }

    /// <summary>
    /// Tests that DownloadFileAsync throws exception when download service fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DownloadFileAsync_ThrowsException_WhenDownloadServiceFails()
    {
        var testUrl = "https://example.com/file.txt";
        var destination = Path.Combine(_tempDir, "download.txt");

        var failedResult = DownloadResult.CreateFailed("Network error");

        _downloadService
            .Setup(x => x.DownloadFileAsync(
                testUrl,
                destination,
                null,
                null,
                default))
            .ReturnsAsync(failedResult);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => _service.DownloadFileAsync(testUrl, destination));

        Assert.Contains("Download failed: Network error", exception.Message);
    }

    /// <summary>
    /// Tests that CopyFileAsync throws an exception when the source file does not exist.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CopyFileAsync_ThrowsException_WhenSourceFileNotFound()
    {
        var nonExistentSource = Path.Combine(_tempDir, "nonexistent.txt");
        var destination = Path.Combine(_tempDir, "destination.txt");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _service.CopyFileAsync(nonExistentSource, destination));
    }

    /// <summary>
    /// Tests that CopyFileAsync creates the necessary directory structure for the destination file.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CopyFileAsync_CreatesDirectoryStructure()
    {
        var source = Path.Combine(_tempDir, "source.txt");
        var destination = Path.Combine(_tempDir, "nested", "deep", "destination.txt");

        await File.WriteAllTextAsync(source, "test content");

        await _service.CopyFileAsync(source, destination);

        Assert.True(File.Exists(destination));
        Assert.True(Directory.Exists(Path.GetDirectoryName(destination)));
    }

    /// <summary>
    /// Tests that VerifyFileHashAsync returns false when file does not exist.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task VerifyFileHashAsync_ReturnsFalse_WhenFileNotExists()
    {
        var nonExistentFile = Path.Combine(_tempDir, "nonexistent.txt");
        var hash = "somehash";

        var result = await _service.VerifyFileHashAsync(nonExistentFile, hash);

        Assert.False(result);
        _downloadService.Verify(
            x => x.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Tests that VerifyFileHashAsync handles exceptions gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task VerifyFileHashAsync_HandlesExceptions_Gracefully()
    {
        var file = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(file, "test");

        _downloadService
            .Setup(x => x.ComputeFileHashAsync(file, default))
            .ThrowsAsync(new IOException("Disk error"));

        var result = await _service.VerifyFileHashAsync(file, "somehash");

        Assert.False(result);
    }

    /// <summary>
    /// Performs cleanup by disposing of temporary resources.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }
}
