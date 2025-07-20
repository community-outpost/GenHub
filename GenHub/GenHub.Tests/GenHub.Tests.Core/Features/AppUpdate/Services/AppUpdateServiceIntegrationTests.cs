using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Interfaces.AppUpdate;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Results;
using GenHub.Features.AppUpdate.Factories;
using GenHub.Features.AppUpdate.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.AppUpdate.Services;

/// <summary>
/// Integration tests for App Update service components.
/// </summary>
public class AppUpdateServiceIntegrationTests : IDisposable
{
    private readonly Mock<ILogger<AppUpdateService>> _mockAppUpdateLogger;
    private readonly Mock<ILogger<AppVersionService>> _mockVersionLogger;
    private readonly Mock<ILogger<SemVerComparator>> _mockComparatorLogger;
    private readonly Mock<ILogger<UpdateInstallerFactory>> _mockFactoryLogger;
    private readonly Mock<IGitHubApiClient> _mockGitHubApiClient;
    private readonly Mock<IDownloadService> _mockDownloadService;
    private readonly ServiceCollection _services;
    private readonly string _testDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppUpdateServiceIntegrationTests"/> class.
    /// </summary>
    public AppUpdateServiceIntegrationTests()
    {
        _mockAppUpdateLogger = new Mock<ILogger<AppUpdateService>>();
        _mockVersionLogger = new Mock<ILogger<AppVersionService>>();
        _mockComparatorLogger = new Mock<ILogger<SemVerComparator>>();
        _mockFactoryLogger = new Mock<ILogger<UpdateInstallerFactory>>();
        _mockGitHubApiClient = new Mock<IGitHubApiClient>();
        _mockDownloadService = new Mock<IDownloadService>();
        _services = new ServiceCollection();
        _testDirectory = Path.Combine(Path.GetTempPath(), "GenHubIntegrationTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);

        SetupServices();
    }

    /// <summary>
    /// Tests that full update flow with valid release should complete successfully.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task FullUpdateFlow_WithValidRelease_ShouldCompleteSuccessfully()
    {
        // Arrange
        var serviceProvider = _services.BuildServiceProvider();
        var mockVersionService = new Mock<IAppVersionService>();
        mockVersionService.Setup(x => x.GetCurrentVersion()).Returns("1.0.0");
        var versionComparator = new SemVerComparator(_mockComparatorLogger.Object);
        var installerFactory = serviceProvider.GetRequiredService<UpdateInstallerFactory>();
        var installer = installerFactory.CreateInstaller();
        var updateService = new AppUpdateService(
            _mockGitHubApiClient.Object,
            mockVersionService.Object,
            versionComparator,
            _mockAppUpdateLogger.Object);

        var mockRelease = new GitHubRelease
        {
            TagName = "v2.0.0",
            HtmlUrl = "https://github.com/test/repo/releases/tag/v2.0.0",
            Name = "Version 2.0.0",
            Body = "New features and bug fixes",
            Assets =
            [
                new ()
                {
                    Name = "app-windows.zip",
                    BrowserDownloadUrl = "https://github.com/test/repo/releases/download/v2.0.0/app.zip",
                    Size = 1024,
                    ContentType = "application/zip",
                },
            ],
        };

        _mockGitHubApiClient.Setup(x => x.GetLatestReleaseAsync("test", "repo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockRelease);

        // Act
        var result = await updateService.CheckForUpdatesAsync("test", "repo");

        // Assert
        result.Should().NotBeNull();
        result.IsUpdateAvailable.Should().BeTrue();
        result.LatestVersion.Should().Be("v2.0.0");
        result.CurrentVersion.Should().Be("1.0.0");
        result.HasErrors.Should().BeFalse();
        result.Assets.Should().HaveCount(1);
        installer.Should().NotBeNull();
        installer.Should().BeOfType<TestPlatformUpdateInstaller>();
    }

    /// <summary>
    /// Tests that service composition should work correctly.
    /// </summary>
    [Fact]
    public void ServiceComposition_ShouldWorkCorrectly()
    {
        // Arrange & Act
        var serviceProvider = _services.BuildServiceProvider();
        var mockVersionService = new Mock<IAppVersionService>();
        mockVersionService.Setup(x => x.GetCurrentVersion()).Returns("1.0.0");
        var versionComparator = new SemVerComparator(_mockComparatorLogger.Object);
        var installerFactory = serviceProvider.GetRequiredService<UpdateInstallerFactory>();
        var installer = installerFactory.CreateInstaller();
        var updateService = new AppUpdateService(
            _mockGitHubApiClient.Object,
            mockVersionService.Object,
            versionComparator,
            _mockAppUpdateLogger.Object);

        // Assert
        mockVersionService.Object.Should().NotBeNull();
        versionComparator.Should().NotBeNull();
        installer.Should().NotBeNull();
        updateService.Should().NotBeNull();
        installerFactory.Should().NotBeNull();
        mockVersionService.Object.GetCurrentVersion().Should().NotBeNullOrEmpty();
        versionComparator.IsNewer("1.0.0", "2.0.0").Should().BeTrue();
    }

    /// <summary>
    /// Tests that update service with network error should handle gracefully.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task UpdateService_WithNetworkError_ShouldHandleGracefully()
    {
        // Arrange
        var mockVersionService = new Mock<IAppVersionService>();
        mockVersionService.Setup(x => x.GetCurrentVersion()).Returns("1.0.0");
        var versionComparator = new SemVerComparator(_mockComparatorLogger.Object);
        var updateService = new AppUpdateService(
            _mockGitHubApiClient.Object,
            mockVersionService.Object,
            versionComparator,
            _mockAppUpdateLogger.Object);

        _mockGitHubApiClient.Setup(x => x.GetLatestReleaseAsync("test", "repo", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var result = await updateService.CheckForUpdatesAsync("test", "repo");

        // Assert
        result.Should().NotBeNull();
        result.IsUpdateAvailable.Should().BeFalse();
        result.HasErrors.Should().BeTrue();
        result.ErrorMessages.Should().Contain(msg => msg.Contains("Network error"));
    }

    /// <summary>
    /// Tests that UpdateInstallerFactory should create correct installer.
    /// </summary>
    [Fact]
    public void UpdateInstallerFactory_ShouldCreateCorrectInstaller()
    {
        // Arrange
        var serviceProvider = _services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<UpdateInstallerFactory>();

        // Act
        var installer = factory.CreateInstaller();

        // Assert
        installer.Should().NotBeNull();
        installer.Should().BeAssignableTo<IUpdateInstaller>();
        installer.Should().BeAssignableTo<IPlatformUpdateInstaller>();
    }

    /// <summary>
    /// Tests that installer can handle download progress reporting.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task Installer_ShouldHandleDownloadProgressReporting()
    {
        // Arrange
        var serviceProvider = _services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<UpdateInstallerFactory>();
        var installer = factory.CreateInstaller();
        var progressReports = new List<UpdateProgress>();
        var progress = new Progress<UpdateProgress>(p => progressReports.Add(p));

        // Create a real temporary file for the File.Exists check to pass.
        var fakeDownloadPath = Path.Combine(_testDirectory, "update.zip");
        using (var fs = new FileStream(fakeDownloadPath, System.IO.FileMode.Create))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            archive.CreateEntry("test.txt"); // Add a file to make it a valid archive
        }

        // Update the mock to return the path to the real temporary file.
        _mockDownloadService.Setup(x => x.DownloadFileAsync(
                It.IsAny<DownloadConfiguration>(),
                It.IsAny<IProgress<DownloadProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DownloadResult.CreateSuccess(fakeDownloadPath, 1024, TimeSpan.FromSeconds(1)));

        // Act
        var result = await installer.DownloadAndInstallAsync("https://test.com/update.zip", progress);

        // Assert
        result.Should().BeTrue();
        progressReports.Should().NotBeEmpty();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _services?.Clear();
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }

        GC.SuppressFinalize(this);
    }

    private void SetupServices()
    {
        _services.AddSingleton(_mockFactoryLogger.Object);
        _services.AddSingleton<UpdateInstallerFactory>();
        _services.AddSingleton<IPlatformUpdateInstaller>(serviceProvider =>
        {
            var mockTestLogger = new Mock<ILogger<TestPlatformUpdateInstaller>>();
            return new TestPlatformUpdateInstaller(_mockDownloadService.Object, mockTestLogger.Object);
        });
    }

    /// <summary>
    /// Test implementation of IPlatformUpdateInstaller for testing purposes.
    /// </summary>
    public class TestPlatformUpdateInstaller(
        IDownloadService downloadService,
        ILogger<TestPlatformUpdateInstaller> logger)
        : BaseUpdateInstaller(downloadService, logger), IPlatformUpdateInstaller
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
