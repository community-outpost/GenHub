using GenHub.Core.Interfaces.AppUpdate;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.GitHub;
using GenHub.Features.AppUpdate.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.AppUpdate.Services;

/// <summary>
/// Unit tests for <see cref="AppUpdateService"/>.
/// </summary>
public class AppUpdateServiceTests
{
    private readonly Mock<IGitHubApiClient> _mockGitHubApiClient;
    private readonly Mock<IAppVersionService> _mockAppVersionService;
    private readonly Mock<IVersionComparator> _mockVersionComparator;
    private readonly Mock<ILogger<AppUpdateService>> _mockLogger;
    private readonly AppUpdateService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppUpdateServiceTests"/> class.
    /// </summary>
    public AppUpdateServiceTests()
    {
        _mockGitHubApiClient = new Mock<IGitHubApiClient>();
        _mockAppVersionService = new Mock<IAppVersionService>();
        _mockVersionComparator = new Mock<IVersionComparator>();
        _mockLogger = new Mock<ILogger<AppUpdateService>>();

        _service = new AppUpdateService(
            _mockGitHubApiClient.Object,
            _mockAppVersionService.Object,
            _mockVersionComparator.Object,
            _mockLogger.Object);
    }

    /// <summary>
    /// Tests that constructor throws ArgumentNullException when gitHubApiClient is null.
    /// </summary>
    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenGitHubApiClientIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AppUpdateService(
            null!,
            _mockAppVersionService.Object,
            _mockVersionComparator.Object,
            _mockLogger.Object));
    }

    /// <summary>
    /// Tests that constructor throws ArgumentNullException when appVersionService is null.
    /// </summary>
    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenAppVersionServiceIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AppUpdateService(
            _mockGitHubApiClient.Object,
            null!,
            _mockVersionComparator.Object,
            _mockLogger.Object));
    }

    /// <summary>
    /// Tests that constructor throws ArgumentNullException when versionComparator is null.
    /// </summary>
    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenVersionComparatorIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AppUpdateService(
            _mockGitHubApiClient.Object,
            _mockAppVersionService.Object,
            null!,
            _mockLogger.Object));
    }

    /// <summary>
    /// Tests that constructor throws ArgumentNullException when logger is null.
    /// </summary>
    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AppUpdateService(
            _mockGitHubApiClient.Object,
            _mockAppVersionService.Object,
            _mockVersionComparator.Object,
            null!));
    }

    /// <summary>
    /// Tests that GetCurrentVersion returns the version from AppVersionService.
    /// </summary>
    [Fact]
    public void GetCurrentVersion_ReturnsVersionFromAppVersionService()
    {
        // Arrange
        var expectedVersion = "1.2.3";
        _mockAppVersionService.Setup(x => x.GetCurrentVersion()).Returns(expectedVersion);

        // Act
        var result = _service.GetCurrentVersion();

        // Assert
        Assert.Equal(expectedVersion, result);
        _mockAppVersionService.Verify(x => x.GetCurrentVersion(), Times.Once);
    }

    /// <summary>
    /// Verifies update is available when a newer version exists.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsUpdateAvailable_WhenNewerVersionExists()
    {
        // Arrange
        var currentVersion = "1.0.0";
        var latestTag = "1.1.0";
        var release = new GitHubRelease
        {
            TagName = latestTag,
            HtmlUrl = "https://github.com/test/repo/releases/tag/v1.1.0",
            Name = "Release 1.1.0",
            Body = "Release notes",
            Assets = new List<GitHubReleaseAsset>(),
        };

        _mockAppVersionService.Setup(x => x.GetCurrentVersion()).Returns(currentVersion);
        _mockGitHubApiClient.Setup(x => x.GetLatestReleaseAsync("owner", "repo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);
        _mockVersionComparator.Setup(x => x.IsNewer(currentVersion, latestTag)).Returns(true);

        // Act
        var result = await _service.CheckForUpdatesAsync("owner", "repo", CancellationToken.None);

        // Assert
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(latestTag, result.LatestVersion);
        Assert.Equal(currentVersion, result.CurrentVersion);
        Assert.Equal("https://github.com/test/repo/releases/tag/v1.1.0", result.UpdateUrl);
        Assert.Equal("Release 1.1.0", result.ReleaseTitle);
        Assert.Equal("Release notes", result.ReleaseNotes);
    }

    /// <summary>
    /// Verifies no update is available when the current version is up to date.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsNoUpdate_WhenNoNewerVersionExists()
    {
        // Arrange
        var currentVersion = "1.1.0";
        var latestTag = "1.1.0";
        var release = new GitHubRelease
        {
            TagName = latestTag,
            HtmlUrl = "https://github.com/test/repo/releases/tag/v1.1.0",
            Assets = new List<GitHubReleaseAsset>(),
        };

        _mockAppVersionService.Setup(x => x.GetCurrentVersion()).Returns(currentVersion);
        _mockGitHubApiClient.Setup(x => x.GetLatestReleaseAsync("owner", "repo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);
        _mockVersionComparator.Setup(x => x.IsNewer(currentVersion, latestTag)).Returns(false);

        // Act
        var result = await _service.CheckForUpdatesAsync("owner", "repo", CancellationToken.None);

        // Assert
        Assert.False(result.IsUpdateAvailable);
        Assert.Equal(latestTag, result.LatestVersion);
        Assert.Equal(currentVersion, result.CurrentVersion);
        Assert.Equal("https://github.com/test/repo/releases/tag/v1.1.0", result.UpdateUrl);
    }

    /// <summary>
    /// Verifies no update is available when current version is newer than latest release.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsNoUpdate_WhenCurrentVersionIsNewer()
    {
        // Arrange
        var currentVersion = "1.2.0";
        var latestTag = "1.1.0";
        var release = new GitHubRelease
        {
            TagName = latestTag,
            HtmlUrl = "https://github.com/test/repo/releases/tag/v1.1.0",
            Assets = new List<GitHubReleaseAsset>(),
        };

        _mockAppVersionService.Setup(x => x.GetCurrentVersion()).Returns(currentVersion);
        _mockGitHubApiClient.Setup(x => x.GetLatestReleaseAsync("owner", "repo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);
        _mockVersionComparator.Setup(x => x.IsNewer(currentVersion, latestTag)).Returns(false);

        // Act
        var result = await _service.CheckForUpdatesAsync("owner", "repo", CancellationToken.None);

        // Assert
        Assert.False(result.IsUpdateAvailable);
        Assert.Equal(latestTag, result.LatestVersion);
        Assert.Equal(currentVersion, result.CurrentVersion);
    }

    /// <summary>
    /// Verifies no update is available when no releases are found.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsNoUpdate_WhenNoReleasesFound()
    {
        // Arrange
        var currentVersion = "1.0.0";
        _mockAppVersionService.Setup(x => x.GetCurrentVersion()).Returns(currentVersion);
        _mockGitHubApiClient.Setup(x => x.GetLatestReleaseAsync("owner", "repo", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GitHubRelease?)null!);

        // Act
        var result = await _service.CheckForUpdatesAsync("owner", "repo", CancellationToken.None);

        // Assert
        Assert.False(result.IsUpdateAvailable);
        Assert.Equal(currentVersion, result.CurrentVersion);
        Assert.Equal(string.Empty, result.LatestVersion, ignoreCase: false, ignoreLineEndingDifferences: false, ignoreWhiteSpaceDifferences: false);
        Assert.False(result.HasErrors);
    }

    /// <summary>
    /// Verifies error result is returned when GitHub API throws an exception.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsError_WhenGitHubApiThrowsException()
    {
        // Arrange
        var currentVersion = "1.0.0";
        var exceptionMessage = "API rate limit exceeded";
        _mockAppVersionService.Setup(x => x.GetCurrentVersion()).Returns(currentVersion);
        _mockGitHubApiClient.Setup(x => x.GetLatestReleaseAsync("owner", "repo", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception(exceptionMessage));

        // Act
        var result = await _service.CheckForUpdatesAsync("owner", "repo", CancellationToken.None);

        // Assert
        Assert.False(result.IsUpdateAvailable);
        Assert.True(result.HasErrors);
        Assert.Contains(exceptionMessage, result.ErrorMessages);
    }

    /// <summary>
    /// Verifies error result is returned when version service throws an exception.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsError_WhenVersionServiceThrowsException()
    {
        // Arrange
        var exceptionMessage = "Version file not found";
        _mockAppVersionService.Setup(x => x.GetCurrentVersion()).Throws(new Exception(exceptionMessage));

        // Act
        var result = await _service.CheckForUpdatesAsync("owner", "repo", CancellationToken.None);

        // Assert
        Assert.False(result.IsUpdateAvailable);
        Assert.True(result.HasErrors);
        Assert.Contains(exceptionMessage, result.ErrorMessages);
    }

    /// <summary>
    /// Verifies error result is returned when version comparator throws an exception.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsError_WhenVersionComparatorThrowsException()
    {
        // Arrange
        var currentVersion = "1.0.0";
        var latestTag = "invalid-version";
        var release = new GitHubRelease { TagName = latestTag, Assets = new List<GitHubReleaseAsset>(), };
        var exceptionMessage = "Invalid version format";

        _mockAppVersionService.Setup(x => x.GetCurrentVersion()).Returns(currentVersion);
        _mockGitHubApiClient.Setup(x => x.GetLatestReleaseAsync("owner", "repo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);
        _mockVersionComparator.Setup(x => x.IsNewer(currentVersion, latestTag))
            .Throws(new Exception(exceptionMessage));

        // Act
        var result = await _service.CheckForUpdatesAsync("owner", "repo", CancellationToken.None);

        // Assert
        Assert.False(result.IsUpdateAvailable);
        Assert.True(result.HasErrors);
        Assert.Contains(exceptionMessage, result.ErrorMessages);
    }

    /// <summary>
    /// Verifies that cancellation token is properly passed to GitHub API client.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_PassesCancellationTokenToGitHubClient()
    {
        // Arrange
        var cancellationToken = new CancellationToken(true);
        var currentVersion = "1.0.0";
        _mockAppVersionService.Setup(x => x.GetCurrentVersion()).Returns(currentVersion);
        _mockGitHubApiClient.Setup(x => x.GetLatestReleaseAsync("owner", "repo", cancellationToken))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _service.CheckForUpdatesAsync("owner", "repo", cancellationToken));
    }

    /// <summary>
    /// Verifies that assets are properly included in the update result.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_IncludesAssetsInResult_WhenUpdateAvailable()
    {
        // Arrange
        var currentVersion = "1.0.0";
        var latestTag = "1.1.0";
        var assets = new List<GitHubReleaseAsset>
        {
            new() { Name = "app-windows.exe", Size = 1024, },
            new() { Name = "app-linux.tar.gz", Size = 2048, },
        };
        var release = new GitHubRelease
        {
            TagName = latestTag,
            HtmlUrl = "url",
            Assets = assets,
        };

        _mockAppVersionService.Setup(x => x.GetCurrentVersion()).Returns(currentVersion);
        _mockGitHubApiClient.Setup(x => x.GetLatestReleaseAsync("owner", "repo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);
        _mockVersionComparator.Setup(x => x.IsNewer(currentVersion, latestTag)).Returns(true);

        // Act
        var result = await _service.CheckForUpdatesAsync("owner", "repo", CancellationToken.None);

        // Assert
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(2, result.Assets.Count());
        Assert.Contains(result.Assets, a => a.Name == "app-windows.exe");
        Assert.Contains(result.Assets, a => a.Name == "app-linux.tar.gz");
    }

    /// <summary>
    /// Verifies that empty string parameters are handled gracefully.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_HandlesEmptyParameters_Gracefully()
    {
        // Arrange
        var currentVersion = "1.0.0";
        _mockAppVersionService.Setup(x => x.GetCurrentVersion()).Returns(currentVersion);
        _mockGitHubApiClient.Setup(x => x.GetLatestReleaseAsync(string.Empty, string.Empty, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Invalid repository"));

        // Act
        var result = await _service.CheckForUpdatesAsync(string.Empty, string.Empty, CancellationToken.None);

        // Assert
        Assert.False(result.IsUpdateAvailable);
        Assert.True(result.HasErrors);
    }

    /// <summary>
    /// Verifies that logging is performed for successful update checks.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_LogsInformation_WhenUpdateAvailable()
    {
        // Arrange
        var currentVersion = "1.0.0";
        var latestTag = "1.1.0";
        var release = new GitHubRelease
        {
            TagName = latestTag,
            HtmlUrl = "url",
            Assets = new List<GitHubReleaseAsset>(),
        };

        _mockAppVersionService.Setup(x => x.GetCurrentVersion()).Returns(currentVersion);
        _mockGitHubApiClient.Setup(x => x.GetLatestReleaseAsync("owner", "repo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);
        _mockVersionComparator.Setup(x => x.IsNewer(currentVersion, latestTag)).Returns(true);

        // Act
        await _service.CheckForUpdatesAsync("owner", "repo", CancellationToken.None);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()
                !.Contains("Checking for updates")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    /// <summary>
    /// Verifies that logging is performed for error scenarios.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_LogsError_WhenExceptionOccurs()
    {
        // Arrange
        var currentVersion = "1.0.0";
        var exception = new Exception("Test exception");
        _mockAppVersionService.Setup(x => x.GetCurrentVersion()).Returns(currentVersion);
        _mockGitHubApiClient.Setup(x => x.GetLatestReleaseAsync("owner", "repo", It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        await _service.CheckForUpdatesAsync("owner", "repo", CancellationToken.None);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()
                !.Contains("Failed to check for updates")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
