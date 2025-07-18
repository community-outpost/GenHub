using FluentAssertions;
using GenHub.Core.Interfaces.AppUpdate;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.GitHub;
using GenHub.Features.AppUpdate.Factories;
using GenHub.Features.AppUpdate.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace GenHub.Tests.Core.Features.AppUpdate.Services;

/// <summary>
/// Integration tests for App Update service components.
/// </summary>
public class AppUpdateServiceIntegrationTests : IDisposable
{
    private readonly Mock<ILogger<AppUpdateService>> mockAppUpdateLogger;
    private readonly Mock<ILogger<AppVersionService>> mockVersionLogger;
    private readonly Mock<ILogger<SemVerComparator>> mockComparatorLogger;
    private readonly Mock<ILogger<UpdateInstallerFactory>> mockFactoryLogger;
    private readonly Mock<IGitHubApiClient> mockGitHubApiClient;
    private readonly HttpClient httpClient;
    private readonly ServiceCollection services;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppUpdateServiceIntegrationTests"/> class.
    /// </summary>
    public AppUpdateServiceIntegrationTests()
    {
        this.mockAppUpdateLogger = new Mock<ILogger<AppUpdateService>>();
        this.mockVersionLogger = new Mock<ILogger<AppVersionService>>();
        this.mockComparatorLogger = new Mock<ILogger<SemVerComparator>>();
        this.mockFactoryLogger = new Mock<ILogger<UpdateInstallerFactory>>();
        this.mockGitHubApiClient = new Mock<IGitHubApiClient>();

        var mockHttpHandler = new Mock<HttpMessageHandler>();
        mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent("test content"),
            });

        // Setup Dispose method to avoid strict mock failures
        mockHttpHandler
            .Protected()
            .Setup("Dispose", ItExpr.IsAny<bool>());

        this.httpClient = new HttpClient(mockHttpHandler.Object);

        this.services = new ServiceCollection();
        this.SetupServices();
    }

    /// <summary>
    /// Tests that full update flow with valid release should complete successfully.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task FullUpdateFlow_WithValidRelease_ShouldCompleteSuccessfully()
    {
        // Arrange
        var serviceProvider = this.services.BuildServiceProvider();

        // Mock the version service to return a predictable lower version
        var mockVersionService = new Mock<IAppVersionService>();
        mockVersionService.Setup(x => x.GetCurrentVersion()).Returns("1.0.0");

        var versionComparator = new SemVerComparator(this.mockComparatorLogger.Object);
        var installerFactory = serviceProvider.GetRequiredService<UpdateInstallerFactory>();
        var installer = installerFactory.CreateInstaller();
        var updateService = new AppUpdateService(
            this.mockGitHubApiClient.Object,
            mockVersionService.Object,
            versionComparator,
            this.mockAppUpdateLogger.Object);

        // Create mock GitHub release
        var mockRelease = new GitHubRelease
        {
            TagName = "v2.0.0",
            HtmlUrl = "https://github.com/test/repo/releases/tag/v2.0.0",
            Name = "Version 2.0.0",
            Body = "New features and bug fixes",
            Assets = new List<GitHubReleaseAsset>
                {
                    new ()
                    {
                        Name = "app-windows.zip",
                        BrowserDownloadUrl = "https://github.com/test/repo/releases/download/v2.0.0/app.zip",
                        Size = 1024,
                        ContentType = "application/zip",
                    },
                },
        };

        this.mockGitHubApiClient.Setup(x => x.GetLatestReleaseAsync("test", "repo", It.IsAny<CancellationToken>()))
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

        // Verify installer was created successfully
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
        var serviceProvider = this.services.BuildServiceProvider();

        var mockVersionService = new Mock<IAppVersionService>();
        mockVersionService.Setup(x => x.GetCurrentVersion()).Returns("1.0.0");

        var versionComparator = new SemVerComparator(this.mockComparatorLogger.Object);
        var installerFactory = serviceProvider.GetRequiredService<UpdateInstallerFactory>();
        var installer = installerFactory.CreateInstaller();
        var updateService = new AppUpdateService(
            this.mockGitHubApiClient.Object,
            mockVersionService.Object,
            versionComparator,
            this.mockAppUpdateLogger.Object);

        // Assert
        mockVersionService.Object.Should().NotBeNull();
        versionComparator.Should().NotBeNull();
        installer.Should().NotBeNull();
        updateService.Should().NotBeNull();
        installerFactory.Should().NotBeNull();

        // Verify version service works
        var currentVersion = mockVersionService.Object.GetCurrentVersion();
        currentVersion.Should().NotBeNullOrEmpty();

        // Verify version comparator works
        var isNewer = versionComparator.IsNewer("1.0.0", "2.0.0");
        isNewer.Should().BeTrue();
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
        var versionComparator = new SemVerComparator(this.mockComparatorLogger.Object);
        var updateService = new AppUpdateService(
            this.mockGitHubApiClient.Object,
            mockVersionService.Object,
            versionComparator,
            this.mockAppUpdateLogger.Object);

        this.mockGitHubApiClient.Setup(x => x.GetLatestReleaseAsync("test", "repo", It.IsAny<CancellationToken>()))
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
        var serviceProvider = this.services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<UpdateInstallerFactory>();

        // Act
        var installer = factory.CreateInstaller();

        // Assert
        installer.Should().NotBeNull();
        installer.Should().BeAssignableTo<IUpdateInstaller>();
        installer.Should().BeAssignableTo<IPlatformUpdateInstaller>();
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        this.httpClient?.Dispose();
        this.services?.Clear();
        GC.SuppressFinalize(this);
    }

    private void SetupServices()
    {
        // Register required services for factory pattern
        this.services.AddSingleton(this.mockFactoryLogger.Object);
        this.services.AddSingleton<UpdateInstallerFactory>();

        // Register a mock platform installer for testing - avoid disposal issues
        this.services.AddSingleton<IPlatformUpdateInstaller>(serviceProvider =>
        {
            // Use a simple mock logger instead of LoggerFactory to avoid disposal
            var mockTestLogger = new Mock<ILogger<TestPlatformUpdateInstaller>>();
            return new TestPlatformUpdateInstaller(this.httpClient, mockTestLogger.Object);
        });
    }

    /// <summary>
    /// Test implementation of IPlatformUpdateInstaller for testing purposes.
    /// </summary>
    public class TestPlatformUpdateInstaller : BaseUpdateInstaller, IPlatformUpdateInstaller
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TestPlatformUpdateInstaller"/> class.
        /// </summary>
        /// <param name="httpClient">The HTTP client.</param>
        /// <param name="logger">The logger.</param>
        public TestPlatformUpdateInstaller(HttpClient httpClient, ILogger<TestPlatformUpdateInstaller> logger)
            : base(httpClient, logger)
        {
        }

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
            // Mock implementation for testing - DON'T create actual scripts/processes
            progress?.Report(new UpdateProgress
            {
                Status = "Application will restart to complete installation.",
                PercentComplete = 100,
                IsCompleted = true,
            });
            return Task.FromResult(true);
        }

        /// <summary>
        /// Override to prevent actual application shutdown during tests.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Always returns true for testing.</returns>
        protected new Task<bool> ScheduleApplicationShutdownAsync(CancellationToken cancellationToken)
        {
            // Do nothing in tests - don't actually try to shutdown or create batch files
            return Task.FromResult(true);
        }
    }
}
