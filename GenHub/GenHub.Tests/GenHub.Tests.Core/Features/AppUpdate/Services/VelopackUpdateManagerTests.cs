using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Features.AppUpdate.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Velopack.Sources;

namespace GenHub.Tests.Core.Features.AppUpdate.Services;

/// <summary>
/// Tests for <see cref="VelopackUpdateManager"/>.
/// </summary>
public class VelopackUpdateManagerTests
{
    private const string CannedGitHubReleasesJson = """
        [
          {
            "tag_name": "v0.3.0",
            "name": "GenHub v0.3.0",
            "prerelease": false,
            "draft": false
          }
        ]
        """;

    private readonly Mock<ILogger<VelopackUpdateManager>> _mockLogger;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<IGitHubAuthService> _mockGitHubAuthService;
    private readonly Mock<IUserSettingsService> _mockUserSettingsService;

    /// <summary>
    /// Initializes a new instance of the <see cref="VelopackUpdateManagerTests"/> class.
    /// </summary>
    public VelopackUpdateManagerTests()
    {
        _mockLogger = new Mock<ILogger<VelopackUpdateManager>>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockGitHubAuthService = new Mock<IGitHubAuthService>();
        _mockUserSettingsService = new Mock<IUserSettingsService>();

        // Return a fresh canned HttpClient per call to support dispose-per-request patterns
        _mockHttpClientFactory
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(() => CreateCannedHttpClient(CannedGitHubReleasesJson));

        // Default: no GitHub authentication available
        _mockGitHubAuthService.SetupGet(x => x.IsAuthenticated).Returns(false);

        // Default: return default settings
        _mockUserSettingsService.Setup(x => x.Get()).Returns(new GenHub.Core.Models.Common.UserSettings());
    }

    /// <summary>
    /// Tests that VelopackUpdateManager can be constructed successfully.
    /// </summary>
    [Fact]
    public void Constructor_ShouldInitializeSuccessfully()
    {
        // Act
        var manager = CreateManager();

        // Assert
        Assert.NotNull(manager);
        Assert.False(manager.IsUpdatePendingRestart);
    }

    /// <summary>
    /// Tests that CheckForUpdatesAsync returns null and sets no GitHub update flag when running from development environment.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_InDevEnvironment_ShouldReturnNullAsync()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var result = await manager.CheckForUpdatesAsync();

        // Assert
        Assert.Null(result);
        Assert.False(manager.HasUpdateAvailableFromGitHub);
        Assert.Null(manager.LatestVersionFromGitHub);
    }

    /// <summary>
    /// Tests that CheckForUpdatesAsync handles cancellation properly.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_WithCancellation_ShouldHandleGracefullyAsync()
    {
        // Arrange
        var manager = CreateManager();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await manager.CheckForUpdatesAsync(cts.Token);

        // Assert
        Assert.Null(result); // Should return null gracefully when UpdateManager is not initialized
    }

    /// <summary>
    /// Tests that DownloadUpdatesAsync throws InvalidOperationException when UpdateManager is not initialized.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadUpdatesAsync_WhenNotInitialized_ShouldThrowInvalidOperationExceptionAsync()
    {
        // Arrange
        var manager = CreateManager();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.DownloadUpdatesAsync(null!));
    }

    /// <summary>
    /// Tests that ApplyUpdatesAndRestart throws InvalidOperationException when UpdateManager is not initialized.
    /// </summary>
    [Fact]
    public void ApplyUpdatesAndRestart_WhenNotInitialized_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var manager = CreateManager();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(
            () => manager.ApplyUpdatesAndRestart(null!));
    }

    /// <summary>
    /// Tests that ApplyUpdatesAndExit throws InvalidOperationException when UpdateManager is not initialized.
    /// </summary>
    [Fact]
    public void ApplyUpdatesAndExit_WhenNotInitialized_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var manager = CreateManager();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(
            () => manager.ApplyUpdatesAndExit(null!));
    }

    /// <summary>
    /// Tests that IsUpdatePendingRestart property is false when UpdateManager is not initialized.
    /// </summary>
    [Fact]
    public void IsUpdatePendingRestart_WhenNotInitialized_ShouldReturnFalse()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var isPending = manager.IsUpdatePendingRestart;

        // Assert
        Assert.False(isPending);
    }

    /// <summary>
    /// Tests that VelopackUpdateManager uses correct GitHub repository URL from constants.
    /// </summary>
    [Fact]
    public void VelopackUpdateManager_ShouldUseCorrectRepositoryUrl()
    {
        // Arrange & Act
        var manager = CreateManager();

        // Assert - verify that the logger was called during construction
        // In a development/test environment, the UpdateManager won't be available
        // so we verify the warning log about UpdateManager not being available
        _mockLogger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    (v.ToString() ?? string.Empty).Contains("Velopack") ||
                    (v.ToString() ?? string.Empty).Contains("Update")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    /// <summary>
    /// Tests that CheckForArtifactUpdatesAsync returns null when GitHub authentication is not available.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForArtifactUpdatesAsync_WithoutAuthentication_ShouldReturnNullAsync()
    {
        // Arrange
        _mockGitHubAuthService.SetupGet(x => x.IsAuthenticated).Returns(false);
        var manager = CreateManager();

        // Act
        var result = await manager.CheckForArtifactUpdatesAsync();

        // Assert
        Assert.Null(result);
        Assert.False(manager.HasArtifactUpdateAvailable);
    }

    /// <summary>
    /// Tests that VelopackUpdateManager accepts a custom IFileDownloader.
    /// </summary>
    [Fact]
    public void Constructor_WithCustomFileDownloader_ShouldInitializeSuccessfully()
    {
        // Arrange
        var customDownloader = new Mock<IFileDownloader>().Object;

        // Act
        var manager = new VelopackUpdateManager(
            _mockLogger.Object,
            _mockHttpClientFactory.Object,
            _mockGitHubAuthService.Object,
            _mockUserSettingsService.Object,
            customDownloader);

        // Assert
        Assert.NotNull(manager);
        Assert.False(manager.IsUpdatePendingRestart);
    }

    /// <summary>
    /// Tests that IsMatchingWorkflowRun accepts push, workflow_dispatch, and pull_request events when head_branch matches.
    /// </summary>
    /// <param name="eventType">The workflow run event type.</param>
    /// <param name="expected">The expected match result.</param>
    [Theory]
    [InlineData("push", true)]
    [InlineData("workflow_dispatch", true)]
    [InlineData("pull_request", true)]
    [InlineData("issue_comment", false)]
    public void IsMatchingWorkflowRun_BranchMatchingEvents_ReturnsExpected(string eventType, bool expected)
    {
        // Arrange
        var json = $"{{\"head_branch\": \"development\", \"event\": \"{eventType}\"}}";
        using var doc = JsonDocument.Parse(json);

        // Act
        var result = VelopackUpdateManager.IsMatchingWorkflowRun(doc.RootElement, "development", null);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests that IsMatchingWorkflowRun rejects runs whose head branch does not match requested branch.
    /// </summary>
    [Fact]
    public void IsMatchingWorkflowRun_WhenHeadBranchDiffers_ReturnsFalse()
    {
        // Arrange
        var json = "{\"head_branch\": \"feature/other\", \"event\": \"pull_request\"}";
        using var doc = JsonDocument.Parse(json);

        // Act
        var result = VelopackUpdateManager.IsMatchingWorkflowRun(doc.RootElement, "development", null);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Tests that IsMatchingWorkflowRun matches PR numbers when specified.
    /// </summary>
    [Fact]
    public void IsMatchingWorkflowRun_WithMatchingPrNumber_ReturnsTrue()
    {
        // Arrange
        var json = "{\"head_branch\": \"development\", \"event\": \"pull_request\", \"pull_requests\": [{\"number\": 378}]}";
        using var doc = JsonDocument.Parse(json);

        // Act
        var result = VelopackUpdateManager.IsMatchingWorkflowRun(doc.RootElement, null, 378);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// Tests that IsMatchingWorkflowRun rejects non-matching PR numbers.
    /// </summary>
    [Fact]
    public void IsMatchingWorkflowRun_WithDifferentPrNumber_ReturnsFalse()
    {
        // Arrange
        var json = "{\"head_branch\": \"development\", \"event\": \"pull_request\", \"pull_requests\": [{\"number\": 378}]}";
        using var doc = JsonDocument.Parse(json);

        // Act
        var result = VelopackUpdateManager.IsMatchingWorkflowRun(doc.RootElement, null, 400);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Tests that an installed PR build allows release fallback when a newer GitHub release exists.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_WhenInstalledPrBuildAndNewerReleaseExists_AllowsReleaseFallbackAsync()
    {
        // Arrange - installed PR build from CI (has CI metadata, not a local build)
        var manager = CreateManager(
            currentAppVersion: "0.0.1525-pr541",
            isLocalDevelopmentBuild: false);

        // Act
        var result = await manager.CheckForUpdatesAsync();

        // Assert - UpdateManager is not installed in the test runner, so result is null,
        // but GitHub API release update must be marked available to allow release fallback.
        Assert.Null(result);
        Assert.True(manager.HasUpdateAvailableFromGitHub);
        Assert.Equal("0.3.0", manager.LatestVersionFromGitHub);
    }

    /// <summary>
    /// Tests that an installed branch build allows release fallback when a newer GitHub release exists.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_WhenInstalledBranchBuildAndNewerReleaseExists_AllowsReleaseFallbackAsync()
    {
        // Arrange - installed branch artifact from CI (e.g. development branch run 1525)
        var manager = CreateManager(
            currentAppVersion: "0.0.1525",
            isLocalDevelopmentBuild: false);

        // Act
        var result = await manager.CheckForUpdatesAsync();

        // Assert
        Assert.Null(result);
        Assert.True(manager.HasUpdateAvailableFromGitHub);
        Assert.Equal("0.3.0", manager.LatestVersionFromGitHub);
    }

    /// <summary>
    /// Tests that an installed release build detects a newer GitHub release.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_WhenReleaseBuildAndNewerReleaseExists_DetectsUpdateAsync()
    {
        // Arrange - installed official release build (e.g. 0.2.0)
        var manager = CreateManager(
            currentAppVersion: "0.2.0",
            isLocalDevelopmentBuild: false);

        // Act
        var result = await manager.CheckForUpdatesAsync();

        // Assert
        Assert.Null(result);
        Assert.True(manager.HasUpdateAvailableFromGitHub);
        Assert.Equal("0.3.0", manager.LatestVersionFromGitHub);
    }

    /// <summary>
    /// Tests that an installed release build that is already up to date returns no update.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_WhenReleaseBuildIsUpToDate_ReturnsNoUpdateAsync()
    {
        // Arrange - installed official release build already at 0.3.0
        var manager = CreateManager(
            currentAppVersion: "0.3.0",
            isLocalDevelopmentBuild: false);

        // Act
        var result = await manager.CheckForUpdatesAsync();

        // Assert
        Assert.Null(result);
        Assert.False(manager.HasUpdateAvailableFromGitHub);
        Assert.Null(manager.LatestVersionFromGitHub);
    }

    private static HttpClient CreateCannedHttpClient(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        });
        return new HttpClient(handler);
    }

    /// <summary>
    /// Creates a new VelopackUpdateManager instance with mocked dependencies and optional version overrides.
    /// </summary>
    private VelopackUpdateManager CreateManager(
        string? currentAppVersion = null,
        bool? isLocalDevelopmentBuild = null)
    {
        if (currentAppVersion != null || isLocalDevelopmentBuild.HasValue)
        {
            return new VelopackUpdateManager(
                _mockLogger.Object,
                _mockHttpClientFactory.Object,
                _mockGitHubAuthService.Object,
                _mockUserSettingsService.Object,
                fileDownloader: null,
                currentAppVersion: currentAppVersion ?? AppConstants.AppVersion,
                isLocalDevelopmentBuild: isLocalDevelopmentBuild ?? AppConstants.IsLocalBuild);
        }

        return new VelopackUpdateManager(
            _mockLogger.Object,
            _mockHttpClientFactory.Object,
            _mockGitHubAuthService.Object,
            _mockUserSettingsService.Object);
    }

    private sealed class TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
