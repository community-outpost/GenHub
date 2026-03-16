using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>
/// Tests for <see cref="ConfigurationProviderService"/>.
/// </summary>
public class ConfigurationProviderServiceTests
{
    private readonly Mock<IAppConfiguration> _mockAppConfig;
    private readonly Mock<IUserSettingsService> _mockUserSettings;
    private readonly Mock<ILogger<ConfigurationProviderService>> _mockLogger;
    private readonly UserSettings _defaultUserSettings;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationProviderServiceTests"/> class.
    /// </summary>
    public ConfigurationProviderServiceTests()
    {
        _mockAppConfig = new Mock<IAppConfiguration>();
        _mockUserSettings = new Mock<IUserSettingsService>();
        _mockLogger = new Mock<ILogger<ConfigurationProviderService>>();
        _defaultUserSettings = new UserSettings();

        // Setup default returns for user settings
        _mockUserSettings.Setup(x => x.Get()).Returns(_defaultUserSettings);
    }

    /// <summary>
    /// Verifies that the constructor initializes correctly with valid dependencies.
    /// </summary>
    [Fact]
    public void Constructor_WithValidDependencies_DoesNotThrow()
    {
        var provider = new ConfigurationProviderService(
            _mockAppConfig.Object,
            _mockUserSettings.Object,
            _mockLogger.Object);
        Assert.NotNull(provider);
    }

    /// <summary>
    /// Verifies that the constructor throws ArgumentNullException when appConfig is null.
    /// </summary>
    [Fact]
    public void Constructor_WithNullAppConfig_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ConfigurationProviderService(
                null!,
                _mockUserSettings.Object,
                _mockLogger.Object));
    }

    /// <summary>
    /// Verifies that the constructor throws ArgumentNullException when userSettings is null.
    /// </summary>
    [Fact]
    public void Constructor_WithNullUserSettings_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ConfigurationProviderService(
                _mockAppConfig.Object,
                null!,
                _mockLogger.Object));
    }

    /// <summary>
    /// Verifies that the constructor throws ArgumentNullException when logger is null.
    /// </summary>
    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ConfigurationProviderService(
                _mockAppConfig.Object,
                _mockUserSettings.Object,
                null!));
    }

    /// <summary>
    /// Verifies that GetWorkspacePath returns user setting when it's valid and directory exists.
    /// </summary>
    [Fact]
    public void GetWorkspacePath_WithValidUserSetting_ReturnsUserSetting()
    {
        // Arrange
        var tempDir = Path.GetTempPath();
        var userPath = Path.Combine(tempDir, "user-workspace");
        Directory.CreateDirectory(userPath);

        try
        {
            var userSettings = new UserSettings { WorkspacePath = userPath };
            userSettings.MarkAsExplicitlySet(nameof(UserSettings.WorkspacePath));
            _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);

            var provider = CreateProvider();

            // Act
            var result = provider.GetWorkspacePath();

            // Assert
            Assert.Equal(userPath, result);
            _mockAppConfig.Verify(x => x.GetDefaultWorkspacePath(), Times.Never);
        }
        finally
        {
            if (Directory.Exists(userPath))
                Directory.Delete(userPath, true);
        }
    }

    /// <summary>
    /// Verifies that GetWorkspacePath returns app default when user setting is null.
    /// </summary>
    [Fact]
    public void GetWorkspacePath_WithNullUserSetting_ReturnsAppDefault()
    {
        // Arrange
        var appDefault = "/app/default/workspace";
        var userSettings = new UserSettings { WorkspacePath = null };

        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetDefaultWorkspacePath()).Returns(appDefault);

        var provider = CreateProvider();

        // Act
        var result = provider.GetWorkspacePath();

        // Assert
        Assert.Equal(appDefault, result);
        _mockAppConfig.Verify(x => x.GetDefaultWorkspacePath(), Times.Once);
    }

    /// <summary>
    /// Verifies that GetWorkspacePath returns app default when user setting directory doesn't exist.
    /// </summary>
    [Fact]
    public void GetWorkspacePath_WithNonExistentUserDirectory_ReturnsAppDefault()
    {
        // Arrange
        var appDefault = "/app/default/workspace";
        var nonExistentPath = "/non/existent/path/that/should/never/exist";
        var userSettings = new UserSettings { WorkspacePath = nonExistentPath };

        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetDefaultWorkspacePath()).Returns(appDefault);

        var provider = CreateProvider();

        // Act
        var result = provider.GetWorkspacePath();

        // Assert
        Assert.Equal(appDefault, result);
        _mockAppConfig.Verify(x => x.GetDefaultWorkspacePath(), Times.Once);
    }

    /// <summary>
    /// Verifies that GetWorkspacePath returns app default when user setting is empty string.
    /// </summary>
    [Fact]
    public void GetWorkspacePath_WithEmptyUserSetting_ReturnsAppDefault()
    {
        // Arrange
        var appDefault = "/app/default/workspace";
        var userSettings = new UserSettings { WorkspacePath = string.Empty };

        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetDefaultWorkspacePath()).Returns(appDefault);

        var provider = CreateProvider();

        // Act
        var result = provider.GetWorkspacePath();

        // Assert
        Assert.Equal(appDefault, result);
        _mockAppConfig.Verify(x => x.GetDefaultWorkspacePath(), Times.Once);
    }

    /// <summary>
    /// Verifies that GetCachePath returns app configuration default.
    /// </summary>
    [Fact]
    public void GetCachePath_ReturnsAppDefault()
    {
        // Arrange
        var appDefault = "/app/cache/directory";
        _mockAppConfig.Setup(x => x.GetDefaultCacheDirectory()).Returns(appDefault);

        var provider = CreateProvider();

        // Act
        var result = provider.GetCachePath();

        // Assert
        Assert.Equal(appDefault, result);
        _mockAppConfig.Verify(x => x.GetDefaultCacheDirectory(), Times.Once);
    }

    /// <summary>
    /// Verifies that GetMaxConcurrentDownloads returns user setting when explicitly set.
    /// </summary>
    [Fact]
    public void GetMaxConcurrentDownloads_WithValidUserSetting_ReturnsUserSetting()
    {
        // Arrange
        var userSettings = new UserSettings { MaxConcurrentDownloads = 5 };
        userSettings.MarkAsExplicitlySet(nameof(UserSettings.MaxConcurrentDownloads));
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetMinConcurrentDownloads()).Returns(1);
        _mockAppConfig.Setup(x => x.GetMaxConcurrentDownloads()).Returns(10);

        var provider = CreateProvider();

        // Act
        var result = provider.GetMaxConcurrentDownloads();

        // Assert
        Assert.Equal(5, result);
        _mockAppConfig.Verify(x => x.GetDefaultMaxConcurrentDownloads(), Times.Never);
    }

    /// <summary>
    /// Verifies that GetMaxConcurrentDownloads returns app default when not explicitly set.
    /// </summary>
    [Fact]
    public void GetMaxConcurrentDownloads_WithZeroUserSetting_ReturnsAppDefault()
    {
        // Arrange - Don't mark as explicitly set
        var userSettings = new UserSettings { MaxConcurrentDownloads = 0 };
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetDefaultMaxConcurrentDownloads()).Returns(8);
        _mockAppConfig.Setup(x => x.GetMinConcurrentDownloads()).Returns(1);
        _mockAppConfig.Setup(x => x.GetMaxConcurrentDownloads()).Returns(10);

        var provider = CreateProvider();

        // Act
        var result = provider.GetMaxConcurrentDownloads();

        // Assert
        Assert.Equal(8, result);
        _mockAppConfig.Verify(x => x.GetDefaultMaxConcurrentDownloads(), Times.Once);
    }

    /// <summary>
    /// Verifies that GetMaxConcurrentDownloads returns app default when user setting is negative.
    /// </summary>
    [Fact]
    public void GetMaxConcurrentDownloads_WithNegativeUserSetting_ReturnsAppDefault()
    {
        // Arrange
        var userSettings = new UserSettings { MaxConcurrentDownloads = -1 };

        // Don't mark as explicitly set - should use app default
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetDefaultMaxConcurrentDownloads()).Returns(3);
        _mockAppConfig.Setup(x => x.GetMinConcurrentDownloads()).Returns(1);
        _mockAppConfig.Setup(x => x.GetMaxConcurrentDownloads()).Returns(10);

        var provider = CreateProvider();

        // Act
        var result = provider.GetMaxConcurrentDownloads();

        // Assert
        Assert.Equal(3, result);
        _mockAppConfig.Verify(x => x.GetDefaultMaxConcurrentDownloads(), Times.Once);
    }

    /// <summary>
    /// Verifies that GetAllowBackgroundDownloads returns user setting when explicitly set to false.
    /// </summary>
    [Fact]
    public void GetAllowBackgroundDownloads_ExplicitlySetToFalse_ReturnsFalse()
    {
        // Arrange
        var userSettings = new UserSettings { AllowBackgroundDownloads = false };
        userSettings.MarkAsExplicitlySet(nameof(UserSettings.AllowBackgroundDownloads));
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);

        var provider = CreateProvider();

        // Act
        var result = provider.GetAllowBackgroundDownloads();

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Verifies that GetAllowBackgroundDownloads returns app default when not explicitly set.
    /// </summary>
    [Fact]
    public void GetAllowBackgroundDownloads_NotExplicitlySet_ReturnsAppDefault()
    {
        // Arrange - Don't mark as explicitly set
        var userSettings = new UserSettings { AllowBackgroundDownloads = false };
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);

        var provider = CreateProvider();

        // Act
        var result = provider.GetAllowBackgroundDownloads();

        // Assert
        Assert.True(result); // App default
    }

    /// <summary>
    /// Verifies that GetDownloadTimeoutSeconds returns user setting when explicitly set.
    /// </summary>
    [Fact]
    public void GetDownloadTimeoutSeconds_WithValidUserSetting_ReturnsUserSetting()
    {
        // Arrange
        var userSettings = new UserSettings { DownloadTimeoutSeconds = 300 };
        userSettings.MarkAsExplicitlySet(nameof(UserSettings.DownloadTimeoutSeconds));
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetMinDownloadTimeoutSeconds()).Returns(10);
        _mockAppConfig.Setup(x => x.GetMaxDownloadTimeoutSeconds()).Returns(3600);

        var provider = CreateProvider();

        // Act
        var result = provider.GetDownloadTimeoutSeconds();

        // Assert
        Assert.Equal(300, result);
        _mockAppConfig.Verify(x => x.GetDefaultDownloadTimeoutSeconds(), Times.Never);
    }

    /// <summary>
    /// Verifies that GetDownloadTimeoutSeconds returns app default when not explicitly set.
    /// </summary>
    [Fact]
    public void GetDownloadTimeoutSeconds_WithZeroUserSetting_ReturnsAppDefault()
    {
        // Arrange
        var userSettings = new UserSettings { DownloadTimeoutSeconds = 0 };

        // Don't mark as explicitly set
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetDefaultDownloadTimeoutSeconds()).Returns(600);
        _mockAppConfig.Setup(x => x.GetMinDownloadTimeoutSeconds()).Returns(10);
        _mockAppConfig.Setup(x => x.GetMaxDownloadTimeoutSeconds()).Returns(3600);

        var provider = CreateProvider();

        // Act
        var result = provider.GetDownloadTimeoutSeconds();

        // Assert
        Assert.Equal(600, result);
        _mockAppConfig.Verify(x => x.GetDefaultDownloadTimeoutSeconds(), Times.Once);
    }

    /// <summary>
    /// Verifies that GetDownloadUserAgent returns user setting when explicitly set.
    /// </summary>
    [Fact]
    public void GetDownloadUserAgent_WithValidUserSetting_ReturnsUserSetting()
    {
        // Arrange
        var userAgent = "CustomAgent/2.0";
        var userSettings = new UserSettings { DownloadUserAgent = userAgent };
        userSettings.MarkAsExplicitlySet(nameof(UserSettings.DownloadUserAgent));
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);

        var provider = CreateProvider();

        // Act
        var result = provider.GetDownloadUserAgent();

        // Assert
        Assert.Equal(userAgent, result);
        _mockAppConfig.Verify(x => x.GetDefaultUserAgent(), Times.Never);
    }

    /// <summary>
    /// Verifies that GetDownloadUserAgent returns app default when user setting is null.
    /// </summary>
    [Fact]
    public void GetDownloadUserAgent_WithNullUserSetting_ReturnsAppDefault()
    {
        // Arrange
        var appDefault = "AppDefault/1.0";
        var userSettings = new UserSettings { DownloadUserAgent = string.Empty };
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetDefaultUserAgent()).Returns(appDefault);

        var provider = CreateProvider();

        // Act
        var result = provider.GetDownloadUserAgent();

        // Assert
        Assert.Equal(appDefault, result);
        _mockAppConfig.Verify(x => x.GetDefaultUserAgent(), Times.Once);
    }

    /// <summary>
    /// Verifies that GetDownloadUserAgent returns app default when user setting is empty.
    /// </summary>
    [Fact]
    public void GetDownloadUserAgent_WithEmptyUserSetting_ReturnsAppDefault()
    {
        // Arrange
        var appDefault = "AppDefault/1.0";
        var userSettings = new UserSettings { DownloadUserAgent = string.Empty };
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetDefaultUserAgent()).Returns(appDefault);

        var provider = CreateProvider();

        // Act
        var result = provider.GetDownloadUserAgent();

        // Assert
        Assert.Equal(appDefault, result);
        _mockAppConfig.Verify(x => x.GetDefaultUserAgent(), Times.Once);
    }

    /// <summary>
    /// Verifies that GetDownloadUserAgent returns app default when user setting is whitespace.
    /// </summary>
    [Fact]
    public void GetDownloadUserAgent_WithWhitespaceUserSetting_ReturnsAppDefault()
    {
        // Arrange
        var appDefault = "AppDefault/1.0";
        var userSettings = new UserSettings { DownloadUserAgent = "   " };
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetDefaultUserAgent()).Returns(appDefault);

        var provider = CreateProvider();

        // Act
        var result = provider.GetDownloadUserAgent();

        // Assert
        Assert.Equal(appDefault, result);
        _mockAppConfig.Verify(x => x.GetDefaultUserAgent(), Times.Once);
    }

    /// <summary>
    /// Verifies that GetDownloadBufferSize returns user setting when explicitly set.
    /// </summary>
    [Fact]
    public void GetDownloadBufferSize_ReturnsUserSetting()
    {
        // Arrange
        var bufferSize = 16384;
        var userSettings = new UserSettings { DownloadBufferSize = bufferSize };
        userSettings.MarkAsExplicitlySet(nameof(UserSettings.DownloadBufferSize));
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetMinDownloadBufferSizeBytes()).Returns(4096);
        _mockAppConfig.Setup(x => x.GetMaxDownloadBufferSizeBytes()).Returns(1048576);

        var provider = CreateProvider();

        // Act
        var result = provider.GetDownloadBufferSize();

        // Assert
        Assert.Equal(bufferSize, result);
    }

    /// <summary>
    /// Verifies that GetDefaultWorkspaceStrategy returns user setting when explicitly set.
    /// </summary>
    /// <param name="strategy">The workspace strategy to set in user settings.</param>
    [Theory]
    [InlineData(WorkspaceStrategy.HybridCopySymlink)]
    [InlineData(WorkspaceStrategy.FullCopy)]
    [InlineData(WorkspaceStrategy.HardLink)]
    [InlineData(WorkspaceStrategy.SymlinkOnly)]
    public void GetDefaultWorkspaceStrategy_ReturnsUserSetting(WorkspaceStrategy strategy)
    {
        // Arrange
        var userSettings = new UserSettings { DefaultWorkspaceStrategy = strategy };
        userSettings.MarkAsExplicitlySet(nameof(UserSettings.DefaultWorkspaceStrategy));
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);

        var provider = CreateProvider();

        // Act
        var result = provider.GetDefaultWorkspaceStrategy();

        // Assert
        Assert.Equal(strategy, result);
    }

    /// <summary>
    /// Verifies that GetAutoCheckForUpdatesOnStartup returns user setting when explicitly set.
    /// </summary>
    /// <param name="userValue">The value to set for AutoCheckForUpdatesOnStartup in user settings.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetAutoCheckForUpdatesOnStartup_ReturnsUserSetting(bool userValue)
    {
        // Arrange
        var userSettings = new UserSettings { AutoCheckForUpdatesOnStartup = userValue };
        userSettings.MarkAsExplicitlySet(nameof(UserSettings.AutoCheckForUpdatesOnStartup));
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);

        var provider = CreateProvider();

        // Act
        var result = provider.GetAutoCheckForUpdatesOnStartup();

        // Assert
        Assert.Equal(userValue, result);
    }

    /// <summary>
    /// Verifies that GetEnableDetailedLogging returns user setting when explicitly set.
    /// </summary>
    /// <param name="userValue">The value to set for EnableDetailedLogging in user settings.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetEnableDetailedLogging_ReturnsUserSetting(bool userValue)
    {
        // Arrange
        var userSettings = new UserSettings { EnableDetailedLogging = userValue };
        userSettings.MarkAsExplicitlySet(nameof(UserSettings.EnableDetailedLogging));
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);

        var provider = CreateProvider();

        // Act
        var result = provider.GetEnableDetailedLogging();

        // Assert
        Assert.Equal(userValue, result);
    }

    /// <summary>
    /// Verifies that multiple calls work correctly with explicit property tracking.
    /// </summary>
    [Fact]
    public void MultipleMethodCalls_WorkCorrectly()
    {
        // Arrange
        var userSettings = new UserSettings
        {
            MaxConcurrentDownloads = 7,
            AllowBackgroundDownloads = false,
            EnableDetailedLogging = true,
        };

        // Mark only some as explicitly set
        userSettings.MarkAsExplicitlySet(nameof(UserSettings.MaxConcurrentDownloads));
        userSettings.MarkAsExplicitlySet(nameof(UserSettings.AllowBackgroundDownloads));
        userSettings.MarkAsExplicitlySet(nameof(UserSettings.EnableDetailedLogging));

        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetDefaultCacheDirectory()).Returns("/cache");
        _mockAppConfig.Setup(x => x.GetMinConcurrentDownloads()).Returns(1);
        _mockAppConfig.Setup(x => x.GetMaxConcurrentDownloads()).Returns(10);

        var provider = CreateProvider();

        // Act & Assert
        Assert.Equal(7, provider.GetMaxConcurrentDownloads());
        Assert.False(provider.GetAllowBackgroundDownloads());
        Assert.True(provider.GetEnableDetailedLogging());
        Assert.Equal("/cache", provider.GetCachePath());
    }

    /// <summary>
    /// Verifies that GetCachePath returns user setting when it's valid.
    /// </summary>
    [Fact]
    public void GetCachePath_WithValidUserSetting_ReturnsUserSetting()
    {
        // Arrange
        var tempDir = Path.GetTempPath();
        var userCache = Path.Combine(tempDir, "user-cache");
        Directory.CreateDirectory(userCache);

        try
        {
            var userSettings = new UserSettings { CachePath = userCache };
            userSettings.MarkAsExplicitlySet(nameof(UserSettings.CachePath));
            _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);

            var provider = CreateProvider();

            // Act
            var result = provider.GetCachePath();

            // Assert
            Assert.Equal(userCache, result);
            _mockAppConfig.Verify(x => x.GetDefaultCacheDirectory(), Times.Never);
        }
        finally
        {
            if (Directory.Exists(userCache))
                Directory.Delete(userCache, true);
        }
    }

    /// <summary>
    /// Verifies that GetCachePath returns app default when user setting is null.
    /// </summary>
    [Fact]
    public void GetCachePath_WithNullUserSetting_ReturnsAppDefault()
    {
        // Arrange
        var appDefault = "/app/cache/directory";
        var userSettings = new UserSettings { CachePath = null };
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetDefaultCacheDirectory()).Returns(appDefault);

        var provider = CreateProvider();

        // Act
        var result = provider.GetCachePath();

        // Assert
        Assert.Equal(appDefault, result);
        _mockAppConfig.Verify(x => x.GetDefaultCacheDirectory(), Times.Once);
    }

    /// <summary>
    /// Verifies that GetApplicationDataPath returns user setting when available.
    /// </summary>
    [Fact]
    public void GetApplicationDataPath_WithValidUserSetting_ReturnsUserSetting()
    {
        // Arrange
        var userPath = "/user/content/path";
        var userSettings = new UserSettings { ApplicationDataPath = userPath };
        userSettings.MarkAsExplicitlySet(nameof(UserSettings.ApplicationDataPath));
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);

        var provider = CreateProvider();

        // Act
        var result = provider.GetApplicationDataPath();

        // Assert
        Assert.Equal(userPath, result);
    }

    /// <summary>
    /// Verifies that GetApplicationDataPath returns default when user setting is null.
    /// </summary>
    [Fact]
    public void GetApplicationDataPath_WithNullUserSetting_ReturnsDefault()
    {
        // Arrange
        var appDataPath = "/app/data/path";
        var userSettings = new UserSettings { ApplicationDataPath = null };
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetConfiguredDataPath()).Returns(appDataPath);

        var provider = CreateProvider();

        // Act
        var result = provider.GetApplicationDataPath();

        // Assert
        Assert.Equal(appDataPath, result);
    }

    /// <summary>
    /// Verifies that GetContentDirectories returns user setting when available.
    /// </summary>
    [Fact]
    public void GetContentDirectories_WithUserSetting_ReturnsUserSetting()
    {
        // Arrange
        var userDirs = new List<string> { "/user/dir1", "/user/dir2" };
        var userSettings = new UserSettings { ContentDirectories = userDirs };
        userSettings.MarkAsExplicitlySet(nameof(UserSettings.ContentDirectories));
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);

        var provider = CreateProvider();

        // Act
        var result = provider.GetContentDirectories();

        // Assert
        Assert.Equal(userDirs, result);
    }

    /// <summary>
    /// Verifies that GetContentDirectories returns defaults when user setting is null.
    /// </summary>
    [Fact]
    public void GetContentDirectories_WithNullUserSetting_ReturnsDefaults()
    {
        // Arrange
        var appDataPath = "/app/data/path";
        var userSettings = new UserSettings { ContentDirectories = new() };
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetConfiguredDataPath()).Returns(appDataPath);

        var provider = CreateProvider();

        // Act
        var result = provider.GetContentDirectories();

        // Assert
        Assert.Contains(Path.Combine(appDataPath, FileTypes.ManifestsDirectory), result);
        Assert.Contains(Path.Combine(appDataPath, "CustomManifests"), result);
        Assert.True(result.Count >= 3);
    }

    /// <summary>
    /// Verifies that GetGitHubDiscoveryRepositories returns user setting when available.
    /// </summary>
    [Fact]
    public void GetGitHubDiscoveryRepositories_WithUserSetting_ReturnsUserSetting()
    {
        // Arrange
        var userRepos = new List<string> { "user/repo1", "user/repo2" };
        var userSettings = new UserSettings { GitHubDiscoveryRepositories = userRepos };
        userSettings.MarkAsExplicitlySet(nameof(UserSettings.GitHubDiscoveryRepositories));
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);

        var provider = CreateProvider();

        // Act
        var result = provider.GetGitHubDiscoveryRepositories();

        // Assert
        Assert.Equal(userRepos, result);
    }

    /// <summary>
    /// Verifies that GetGitHubDiscoveryRepositories returns defaults when user setting is null.
    /// </summary>
    [Fact]
    public void GetGitHubDiscoveryRepositories_WithNullUserSetting_ReturnsDefaults()
    {
        // Arrange
        var userSettings = new UserSettings { GitHubDiscoveryRepositories = new() };
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);

        var provider = CreateProvider();

        // Act
        var result = provider.GetGitHubDiscoveryRepositories();

        // Assert
        Assert.Contains("TheSuperHackers/GeneralsGameCode", result);
        Assert.Single(result);
    }

    /// <summary>
    /// Creates a ConfigurationProviderService instance for testing.
    /// </summary>
    /// <returns>A new ConfigurationProviderService instance.</returns>
    private ConfigurationProviderService CreateProvider()
    {
        return new ConfigurationProviderService(
            _mockAppConfig.Object,
            _mockUserSettings.Object,
            _mockLogger.Object);
    }
}
