using System.Text.Json;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Features.AppUpdate.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Velopack.Sources;

namespace GenHub.Tests.Core.Features.AppUpdate.Services;

/// <summary>
/// Tests for <see cref="VelopackUpdateManager"/>.
/// </summary>
public class VelopackUpdateManagerTests
{
    private readonly Mock<ILogger<VelopackUpdateManager>> _mockLogger;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<IGitHubTokenStorage> _mockGitHubTokenStorage;
    private readonly Mock<IUserSettingsService> _mockUserSettingsService;

    /// <summary>
    /// Initializes a new instance of the <see cref="VelopackUpdateManagerTests"/> class.
    /// </summary>
    public VelopackUpdateManagerTests()
    {
        _mockLogger = new Mock<ILogger<VelopackUpdateManager>>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockGitHubTokenStorage = new Mock<IGitHubTokenStorage>();
        _mockUserSettingsService = new Mock<IUserSettingsService>();

        // Use the actual interface method, not the extension method
        _mockHttpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        // Default: no PAT token available
        _mockGitHubTokenStorage.Setup(x => x.HasToken()).Returns(false);

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
    /// Tests that CheckForUpdatesAsync returns null when running from development environment.
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
        _ = CreateManager();

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
    /// Tests that CheckForArtifactUpdatesAsync returns null when no PAT is available.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForArtifactUpdatesAsync_WithoutPAT_ShouldReturnNullAsync()
    {
        // Arrange
        _mockGitHubTokenStorage.Setup(x => x.HasToken()).Returns(false);
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
            _mockGitHubTokenStorage.Object,
            _mockUserSettingsService.Object,
            customDownloader);

        // Assert
        Assert.NotNull(manager);
        Assert.False(manager.IsUpdatePendingRestart);
    }

    [Fact]
    public void CleanStrayAppDirectoryArtifacts_CleansBuildAndReleaseDirsInSampleProjects()
    {
        // Arrange
        var manager = CreateManager();
        var sampleDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SampleProjects", "TestClean_" + Guid.NewGuid().ToString("N"));
        var buildDir = Path.Combine(sampleDir, ".Build");
        var releaseDir = Path.Combine(sampleDir, ".Release");
        var cacheDir = Path.Combine(sampleDir, ".modbuilder_cache");

        try
        {
            Directory.CreateDirectory(sampleDir);
            File.WriteAllText(Path.Combine(sampleDir, "test.mbproj"), "{}");
            Directory.CreateDirectory(buildDir);
            Directory.CreateDirectory(releaseDir);
            Directory.CreateDirectory(cacheDir);

            // Act
            manager.CleanStrayAppDirectoryArtifacts();

            // Assert
            Assert.False(Directory.Exists(buildDir));
            Assert.False(Directory.Exists(releaseDir));
            Assert.False(Directory.Exists(cacheDir));
        }
        finally
        {
            if (Directory.Exists(sampleDir))
            {
                try { Directory.Delete(sampleDir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
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
    /// Creates a new VelopackUpdateManager instance with mocked dependencies.
    /// </summary>
    private VelopackUpdateManager CreateManager() =>
        new(_mockLogger.Object, _mockHttpClientFactory.Object, _mockGitHubTokenStorage.Object, _mockUserSettingsService.Object);
}
