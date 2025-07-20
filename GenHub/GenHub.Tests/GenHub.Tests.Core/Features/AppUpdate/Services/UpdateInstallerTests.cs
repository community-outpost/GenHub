using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Results;
using GenHub.Features.AppUpdate.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.AppUpdate.Services;

/// <summary>
/// Tests for <see cref="BaseUpdateInstaller"/> and platform implementations.
/// </summary>
public class UpdateInstallerTests : IDisposable
{
    private readonly Mock<ILogger<TestUpdateInstaller>> _mockLogger;
    private readonly Mock<IDownloadService> _mockDownloadService;
    private readonly string _tempDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateInstallerTests"/> class.
    /// </summary>
    public UpdateInstallerTests()
    {
        _mockLogger = new Mock<ILogger<TestUpdateInstaller>>();
        _mockDownloadService = new Mock<IDownloadService>();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "UpdateInstallerTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// Tests that constructor with valid parameters should not throw.
    /// </summary>
    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Act & Assert
        var installer = new TestUpdateInstaller(_mockDownloadService.Object, _mockLogger.Object);
        installer.Should().NotBeNull();
    }

    /// <summary>
    /// Tests that constructor with null download service should throw ArgumentNullException.
    /// </summary>
    [Fact]
    public void Constructor_WithNullDownloadService_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new TestUpdateInstaller(null!, _mockLogger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("downloadService");
    }

    /// <summary>
    /// Tests that constructor with null logger should throw ArgumentNullException.
    /// </summary>
    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new TestUpdateInstaller(_mockDownloadService.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    /// <summary>
    /// Tests that DownloadAndInstallAsync with invalid URL should throw ArgumentException.
    /// </summary>
    /// <param name="url">The invalid URL to test.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task DownloadAndInstallAsync_WithInvalidUrl_ShouldThrowArgumentException(string? url)
    {
        // Arrange
        using var installer = new TestUpdateInstaller(_mockDownloadService.Object, _mockLogger.Object);

        // Act & Assert
        var act = () => installer.DownloadAndInstallAsync(url!);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("downloadUrl");
    }

    /// <summary>
    /// Tests that DownloadAndInstallAsync with valid ZIP URL should download and create updater.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DownloadAndInstallAsync_WithValidZipUrl_ShouldDownloadAndCreateUpdater()
    {
        // Arrange
        var url = "https://github.com/test/repo/releases/download/v1.0.0/test.zip";
        var testFilePath = Path.Combine(_tempDirectory, "test.zip");
        CreateTestZipFile(testFilePath);

        _mockDownloadService.Setup(x => x.DownloadFileAsync(
                It.IsAny<DownloadConfiguration>(),
                It.IsAny<IProgress<DownloadProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DownloadResult.CreateSuccess(testFilePath, 1024, TimeSpan.FromSeconds(1)));

        using var installer = new TestUpdateInstaller(_mockDownloadService.Object, _mockLogger.Object);
        var progressReports = new List<UpdateProgress>();
        var progress = new Progress<UpdateProgress>(p => progressReports.Add(p));

        // Act
        var result = await installer.DownloadAndInstallAsync(url, progress);

        // Assert
        result.Should().BeTrue();
        progressReports.Should().NotBeEmpty();
        progressReports.Should().Contain(p => p.Status.Contains("Preparing download"));
        progressReports.Should().Contain(p => p.Status.Contains("Application will restart"));
        _mockDownloadService.Verify(
            x => x.DownloadFileAsync(
            It.IsAny<DownloadConfiguration>(),
            It.IsAny<IProgress<DownloadProgress>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that DownloadAndInstallAsync with download error should return false.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DownloadAndInstallAsync_WithDownloadError_ShouldReturnFalse()
    {
        // Arrange
        var url = "https://github.com/test/repo/releases/download/v1.0.0/test.zip";
        _mockDownloadService.Setup(x => x.DownloadFileAsync(
                It.IsAny<DownloadConfiguration>(),
                It.IsAny<IProgress<DownloadProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DownloadResult.CreateFailed("Download failed"));

        using var installer = new TestUpdateInstaller(_mockDownloadService.Object, _mockLogger.Object);

        // Act
        var result = await installer.DownloadAndInstallAsync(url);

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// Tests that GetPlatformDownloadUrl with null assets should return null.
    /// </summary>
    [Fact]
    public void GetPlatformDownloadUrl_WithNullAssets_ShouldReturnNull()
    {
        // Arrange
        using var installer = new TestUpdateInstaller(_mockDownloadService.Object, _mockLogger.Object);

        // Act
        var result = installer.GetPlatformDownloadUrl(null!);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Tests that GetPlatformDownloadUrl with empty assets should return null.
    /// </summary>
    [Fact]
    public void GetPlatformDownloadUrl_WithEmptyAssets_ShouldReturnNull()
    {
        // Arrange
        using var installer = new TestUpdateInstaller(_mockDownloadService.Object, _mockLogger.Object);
        var assets = new List<GitHubReleaseAsset>();

        // Act
        var result = installer.GetPlatformDownloadUrl(assets);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Tests that GetPlatformDownloadUrl with matching asset should return correct URL.
    /// </summary>
    [Fact]
    public void GetPlatformDownloadUrl_WithMatchingAsset_ShouldReturnCorrectUrl()
    {
        // Arrange
        using var installer = new TestUpdateInstaller(_mockDownloadService.Object, _mockLogger.Object);
        var assets = new List<GitHubReleaseAsset>
        {
            new () { Name = "app-linux.tar.gz", BrowserDownloadUrl = "https://example.com/linux" },
            new () { Name = "app-test.zip", BrowserDownloadUrl = "https://example.com/test" },
            new () { Name = "app-mac.dmg", BrowserDownloadUrl = "https://example.com/mac" },
        };

        // Act
        var result = installer.GetPlatformDownloadUrl(assets);

        // Assert
        result.Should().Be("https://example.com/test");
    }

    /// <summary>
    /// Tests that GetPlatformDownloadUrl with no matching asset should return first asset.
    /// </summary>
    [Fact]
    public void GetPlatformDownloadUrl_WithNoMatchingAsset_ShouldReturnFirstAsset()
    {
        // Arrange
        using var installer = new TestUpdateInstaller(_mockDownloadService.Object, _mockLogger.Object);
        var assets = new List<GitHubReleaseAsset>
        {
            new () { Name = "readme.txt", BrowserDownloadUrl = "https://example.com/readme" },
            new () { Name = "changelog.md", BrowserDownloadUrl = "https://example.com/changelog" },
        };

        // Act
        var result = installer.GetPlatformDownloadUrl(assets);

        // Assert
        result.Should().Be("https://example.com/readme");
    }

    /// <summary>
    /// Tests that DownloadAndInstallAsync with cancellation should return false.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DownloadAndInstallAsync_WithCancellation_ShouldReturnFalse()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var url = "https://github.com/test/repo/releases/download/v1.0.0/test.zip";
        _mockDownloadService.Setup(x => x.DownloadFileAsync(
                It.IsAny<DownloadConfiguration>(),
                It.IsAny<IProgress<DownloadProgress>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        using var installer = new TestUpdateInstaller(_mockDownloadService.Object, _mockLogger.Object);

        // Act
        cts.Cancel();
        var result = await installer.DownloadAndInstallAsync(url, null, cts.Token);

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// Tests that Dispose should not throw.
    /// </summary>
    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var installer = new TestUpdateInstaller(_mockDownloadService.Object, _mockLogger.Object);

        // Act & Assert
        var act = installer.Dispose;
        act.Should().NotThrow();
    }

    /// <summary>
    /// Tests that Dispose called multiple times should not throw.
    /// </summary>
    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var installer = new TestUpdateInstaller(_mockDownloadService.Object, _mockLogger.Object);

        // Act & Assert
        installer.Dispose();
        var act = installer.Dispose;
        act.Should().NotThrow();
    }

    /// <summary>
    /// Tests that download progress is properly reported.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DownloadAndInstallAsync_ShouldReportDownloadProgress()
    {
        // Arrange
        var url = "https://github.com/test/repo/releases/download/v1.0.0/test.zip";
        var testFilePath = Path.Combine(_tempDirectory, "test.zip");
        CreateTestZipFile(testFilePath);

        _mockDownloadService.Setup(x => x.DownloadFileAsync(
                It.IsAny<DownloadConfiguration>(),
                It.IsAny<IProgress<DownloadProgress>>(),
                It.IsAny<CancellationToken>()))
            .Callback<DownloadConfiguration, IProgress<DownloadProgress>?, CancellationToken>(
                (config, progress, ct) =>
                {
                    progress?.Report(new DownloadProgress(512, 1024, "test.zip", url, 1024, TimeSpan.FromSeconds(0.5)));
                    progress?.Report(new DownloadProgress(1024, 1024, "test.zip", url, 1024, TimeSpan.FromSeconds(1)));
                })
            .ReturnsAsync(DownloadResult.CreateSuccess(testFilePath, 1024, TimeSpan.FromSeconds(1)));

        using var installer = new TestUpdateInstaller(_mockDownloadService.Object, _mockLogger.Object);
        var progressReports = new List<UpdateProgress>();
        var progress = new Progress<UpdateProgress>(p => progressReports.Add(p));

        // Act
        var result = await installer.DownloadAndInstallAsync(url, progress);

        // Assert
        result.Should().BeTrue();
        progressReports.Should().Contain(p => p.Status.Contains("Downloading"));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }

        GC.SuppressFinalize(this);
    }

    private void CreateTestZipFile(string filePath)
    {
        using var fileStream = new FileStream(filePath, System.IO.FileMode.Create);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, true);
        archive.CreateEntry("publish/GenHub.Windows.exe");
        archive.CreateEntry("publish/GenHub.dll");
        archive.CreateEntry("publish/appsettings.json");
    }

    /// <summary>
    /// Test implementation of BaseUpdateInstaller for testing purposes.
    /// </summary>
    public class TestUpdateInstaller(
        IDownloadService downloadService,
        ILogger<TestUpdateInstaller> logger)
        : BaseUpdateInstaller(downloadService, logger)
    {
        /// <inheritdoc/>
        protected override List<string> GetPlatformAssetPatterns()
        {
            return new List<string> { "test", ".zip" };
        }

        /// <inheritdoc/>
        protected override Task<bool> CreateAndLaunchExternalUpdaterAsync(
            string sourceDirectory,
            string targetDirectory,
            IProgress<UpdateProgress>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report(new UpdateProgress
            {
                Status = "Application will restart to complete installation.",
                PercentComplete = 100,
                IsCompleted = true,
            });
            return Task.FromResult(true);
        }

        /// <summary>
        /// Schedules application shutdown after update installation.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        protected new Task<bool> ScheduleApplicationShutdownAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }
    }
}
