using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Storage;
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
    /// Preserves every CAS option when applying the default primary pool path.
    /// </summary>
    [Fact]
    public void GetCasConfiguration_WhenPrimaryPathIsEmpty_PreservesGcLockTimeout()
    {
        var expectedTimeout = TimeSpan.FromSeconds(91);
        var settings = new UserSettings
        {
            CasConfiguration = new CasConfiguration
            {
                CasRootPath = string.Empty,
                GcLockTimeout = expectedTimeout,
            },
        };
        _mockUserSettings.Setup(service => service.Get()).Returns(settings);
        var provider = CreateProvider();

        var result = provider.GetCasConfiguration();

        Assert.Equal(expectedTimeout, result.GcLockTimeout);
        Assert.False(string.IsNullOrWhiteSpace(result.CasRootPath));
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
    /// Verifies that GetAutoCheckForUpdatesPeriodically returns user setting when explicitly set.
    /// </summary>
    /// <param name="userValue">The value to set for AutoCheckForUpdatesPeriodically in user settings.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetAutoCheckForUpdatesPeriodically_ReturnsUserSetting(bool userValue)
    {
        // Arrange
        var userSettings = new UserSettings { AutoCheckForUpdatesPeriodically = userValue };
        userSettings.MarkAsExplicitlySet(nameof(UserSettings.AutoCheckForUpdatesPeriodically));
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);

        var provider = CreateProvider();

        // Act
        var result = provider.GetAutoCheckForUpdatesPeriodically();

        // Assert
        Assert.Equal(userValue, result);
    }

    /// <summary>
    /// Verifies that GetPeriodicUpdateCheckIntervalMinutes returns user setting when explicitly set.
    /// </summary>
    /// <param name="intervalMinutes">The interval to set in user settings.</param>
    /// <param name="expectedMinutes">The expected clamped interval.</param>
    [Theory]
    [InlineData(60, 60)]
    [InlineData(0, AppUpdateConstants.DefaultPeriodicUpdateCheckIntervalMinutes)]
    [InlineData(20000, AppUpdateConstants.MaxPeriodicUpdateCheckIntervalMinutes)]
    public void GetPeriodicUpdateCheckIntervalMinutes_ReturnsUserSetting(int intervalMinutes, int expectedMinutes)
    {
        // Arrange
        var userSettings = new UserSettings { PeriodicUpdateCheckIntervalMinutes = intervalMinutes };
        if (intervalMinutes > 0)
        {
            userSettings.MarkAsExplicitlySet(nameof(UserSettings.PeriodicUpdateCheckIntervalMinutes));
        }

        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);

        var provider = CreateProvider();

        // Act
        var result = provider.GetPeriodicUpdateCheckIntervalMinutes();

        // Assert
        Assert.Equal(expectedMinutes, result);
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
        Assert.Contains(Path.Combine(appDataPath, DirectoryNames.CustomManifests), result);
        Assert.True(result.Count >= 3);
    }

    /// <summary>
    /// Verifies that the default content directories follow an explicitly set application data path,
    /// so local discovery scans the same root the manifests are read from and written to.
    /// </summary>
    [Fact]
    public void GetContentDirectories_WithExplicitApplicationDataPath_ReturnsOverride()
    {
        // Arrange
        var userPath = Path.Combine(Path.GetTempPath(), "genhub-user-data-root");
        var userSettings = new UserSettings { ApplicationDataPath = userPath, ContentDirectories = [] };
        userSettings.MarkAsExplicitlySet(nameof(UserSettings.ApplicationDataPath));
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetConfiguredDataPath()).Returns("/app/data/path");

        var provider = CreateProvider();

        // Act
        var result = provider.GetContentDirectories();

        // Assert
        Assert.Contains(Path.Combine(userPath, FileTypes.ManifestsDirectory), result);
        Assert.Contains(Path.Combine(userPath, DirectoryNames.CustomManifests), result);
        Assert.Equal(provider.GetManifestsPath(), result[0]);
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
    /// Verifies that GetGitHubDiscoveryRepositories returns defaults when user setting is empty.
    /// </summary>
    [Fact]
    public void GetGitHubDiscoveryRepositories_WithEmptyUserSetting_ReturnsDefaults()
    {
        // Arrange
        var userSettings = new UserSettings { GitHubDiscoveryRepositories = new() };
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);

        var provider = CreateProvider();

        // Act
        var result = provider.GetGitHubDiscoveryRepositories();

        // Assert
        Assert.Contains("TheSuperHackers/GeneralsGameCode", result);
        Assert.Contains("TheSuperHackers/GeneralsGamePatch2", result);
        Assert.Equal(2, result.Count);
    }

    /// <summary>
    /// Verifies that GetProfilesPath honors an explicitly set application data path.
    /// </summary>
    [Fact]
    public void GetProfilesPath_WithExplicitApplicationDataPath_ReturnsOverride()
    {
        // Arrange
        var userPath = Path.Combine(Path.GetTempPath(), "genhub-user-data-root");
        var userSettings = new UserSettings { ApplicationDataPath = userPath };
        userSettings.MarkAsExplicitlySet(nameof(UserSettings.ApplicationDataPath));
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetConfiguredDataPath()).Returns("/app/data/path");

        var provider = CreateProvider();

        // Act
        var result = provider.GetProfilesPath();

        // Assert
        Assert.Equal(Path.Combine(userPath, DirectoryNames.Profiles), result);
    }

    /// <summary>
    /// Verifies that GetManifestsPath honors an explicitly set application data path.
    /// </summary>
    [Fact]
    public void GetManifestsPath_WithExplicitApplicationDataPath_ReturnsOverride()
    {
        // Arrange
        var userPath = Path.Combine(Path.GetTempPath(), "genhub-user-data-root");
        var userSettings = new UserSettings { ApplicationDataPath = userPath };
        userSettings.MarkAsExplicitlySet(nameof(UserSettings.ApplicationDataPath));
        _mockUserSettings.Setup(x => x.Get()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetConfiguredDataPath()).Returns("/app/data/path");

        var provider = CreateProvider();

        // Act
        var result = provider.GetManifestsPath();

        // Assert
        Assert.Equal(Path.Combine(userPath, FileTypes.ManifestsDirectory), result);
    }

    /// <summary>
    /// Verifies that the profiles and manifests paths fall back to the configured data path when no
    /// application data path override is set.
    /// </summary>
    [Fact]
    public void GetProfilesAndManifestsPath_WithoutOverride_ReturnConfiguredDataPath()
    {
        // Arrange
        var appDataPath = "/app/data/path";
        _mockAppConfig.Setup(x => x.GetConfiguredDataPath()).Returns(appDataPath);

        var provider = CreateProvider();

        // Act & Assert
        Assert.Equal(Path.Combine(appDataPath, DirectoryNames.Profiles), provider.GetProfilesPath());
        Assert.Equal(Path.Combine(appDataPath, FileTypes.ManifestsDirectory), provider.GetManifestsPath());
    }

    /// <summary>
    /// Verifies that the legacy roaming data root is migrated into the current root while the CAS
    /// pool, which still defaults to the legacy location, is left in place.
    /// </summary>
    [Fact]
    public void MigrateLegacyDataRoot_WithLegacyData_MovesTrackedEntriesAndLeavesCasPool()
    {
        var (legacyRoot, newRoot) = CreateMigrationRoots();
        try
        {
            SeedLegacyRoot(legacyRoot);

            CreateProvider().MigrateLegacyDataRoot(legacyRoot, newRoot, newRoot);

            Assert.Equal("profile", File.ReadAllText(Path.Combine(newRoot, DirectoryNames.Profiles, "profile.json")));
            Assert.Equal("manifest", File.ReadAllText(Path.Combine(newRoot, FileTypes.ManifestsDirectory, "content.manifest.json")));
            Assert.Equal("index", File.ReadAllText(Path.Combine(newRoot, DirectoryNames.UserData, FileTypes.UserDataIndexFileName)));
            Assert.Equal("backup", File.ReadAllText(Path.Combine(newRoot, DirectoryNames.UserData, DirectoryNames.UserDataBackups, "save.bak")));
            Assert.Equal("settings", File.ReadAllText(Path.Combine(newRoot, FileTypes.SettingsFileName)));
            Assert.Equal("workspaces", File.ReadAllText(Path.Combine(newRoot, FileTypes.WorkspaceMetadataFileName)));

            Assert.True(File.Exists(Path.Combine(legacyRoot, DirectoryNames.CasPool, "objects", "blob.bin")));
            Assert.False(Directory.Exists(Path.Combine(newRoot, DirectoryNames.CasPool)));
        }
        finally
        {
            DeleteDirectories(legacyRoot, newRoot);
        }
    }

    /// <summary>
    /// Verifies that running the legacy root migration a second time leaves the migrated data alone.
    /// </summary>
    [Fact]
    public void MigrateLegacyDataRoot_RunTwice_IsIdempotent()
    {
        var (legacyRoot, newRoot) = CreateMigrationRoots();
        try
        {
            SeedLegacyRoot(legacyRoot);
            var provider = CreateProvider();

            provider.MigrateLegacyDataRoot(legacyRoot, newRoot, newRoot);
            provider.MigrateLegacyDataRoot(legacyRoot, newRoot, newRoot);

            Assert.Equal("profile", File.ReadAllText(Path.Combine(newRoot, DirectoryNames.Profiles, "profile.json")));
            Assert.Equal("settings", File.ReadAllText(Path.Combine(newRoot, FileTypes.SettingsFileName)));
            Assert.True(File.Exists(Path.Combine(legacyRoot, DirectoryNames.CasPool, "objects", "blob.bin")));
        }
        finally
        {
            DeleteDirectories(legacyRoot, newRoot);
        }
    }

    /// <summary>
    /// Verifies that data already present in the current root wins over the legacy copy.
    /// </summary>
    [Fact]
    public void MigrateLegacyDataRoot_WithExistingData_DoesNotOverwriteNewRoot()
    {
        var (legacyRoot, newRoot) = CreateMigrationRoots();
        try
        {
            SeedLegacyRoot(legacyRoot);
            Directory.CreateDirectory(Path.Combine(newRoot, DirectoryNames.Profiles));
            File.WriteAllText(Path.Combine(newRoot, DirectoryNames.Profiles, "profile.json"), "current-profile");
            File.WriteAllText(Path.Combine(newRoot, FileTypes.SettingsFileName), "current-settings");

            CreateProvider().MigrateLegacyDataRoot(legacyRoot, newRoot, newRoot);

            Assert.Equal("current-profile", File.ReadAllText(Path.Combine(newRoot, DirectoryNames.Profiles, "profile.json")));
            Assert.Equal("current-settings", File.ReadAllText(Path.Combine(newRoot, FileTypes.SettingsFileName)));
            Assert.Equal("workspaces", File.ReadAllText(Path.Combine(newRoot, FileTypes.WorkspaceMetadataFileName)));
        }
        finally
        {
            DeleteDirectories(legacyRoot, newRoot);
        }
    }

    /// <summary>
    /// Verifies that a missing legacy root does not create the current root.
    /// </summary>
    [Fact]
    public void MigrateLegacyDataRoot_WithoutLegacyRoot_DoesNothing()
    {
        var (legacyRoot, newRoot) = CreateMigrationRoots();
        Directory.Delete(legacyRoot);
        Directory.Delete(newRoot);
        try
        {
            CreateProvider().MigrateLegacyDataRoot(legacyRoot, newRoot, newRoot);

            Assert.False(Directory.Exists(newRoot));
        }
        finally
        {
            DeleteDirectories(legacyRoot, newRoot);
        }
    }

    /// <summary>
    /// Verifies that the migration is skipped when both roots resolve to the same directory.
    /// </summary>
    [Fact]
    public void MigrateLegacyDataRoot_WithIdenticalRoots_DoesNothing()
    {
        var (legacyRoot, newRoot) = CreateMigrationRoots();
        try
        {
            SeedLegacyRoot(legacyRoot);

            CreateProvider().MigrateLegacyDataRoot(legacyRoot, Path.Combine(legacyRoot, "."), Path.Combine(legacyRoot, "."));

            Assert.Equal("profile", File.ReadAllText(Path.Combine(legacyRoot, DirectoryNames.Profiles, "profile.json")));
            Assert.Equal("settings", File.ReadAllText(Path.Combine(legacyRoot, FileTypes.SettingsFileName)));
        }
        finally
        {
            DeleteDirectories(legacyRoot, newRoot);
        }
    }

    /// <summary>
    /// Verifies that the migration leaves nothing behind in the legacy root, so a regression from a
    /// move to a copy is caught rather than passing every positive assertion.
    /// </summary>
    [Fact]
    public void MigrateLegacyDataRoot_WithLegacyData_RemovesTheLegacySources()
    {
        var (legacyRoot, newRoot) = CreateMigrationRoots();
        try
        {
            SeedLegacyRoot(legacyRoot);

            CreateProvider().MigrateLegacyDataRoot(legacyRoot, newRoot, newRoot);

            Assert.False(File.Exists(Path.Combine(legacyRoot, FileTypes.SettingsFileName)));
            Assert.False(File.Exists(Path.Combine(legacyRoot, FileTypes.WorkspaceMetadataFileName)));
            Assert.False(Directory.Exists(Path.Combine(legacyRoot, DirectoryNames.Profiles)));
            Assert.False(Directory.Exists(Path.Combine(legacyRoot, FileTypes.ManifestsDirectory)));
            Assert.False(Directory.Exists(Path.Combine(legacyRoot, DirectoryNames.UserData)));
        }
        finally
        {
            DeleteDirectories(legacyRoot, newRoot);
        }
    }

    /// <summary>
    /// Verifies the steady state after a successful migration: a legacy root that still holds the CAS
    /// pool, but none of the migrated entries, is left completely alone.
    /// </summary>
    [Fact]
    public void MigrateLegacyDataRoot_WithoutLegacyEntries_LeavesBothRootsAlone()
    {
        var (legacyRoot, newRoot) = CreateMigrationRoots();
        Directory.Delete(newRoot);
        try
        {
            WriteFile(Path.Combine(legacyRoot, DirectoryNames.CasPool, "objects", "blob.bin"), "cas");

            CreateProvider().MigrateLegacyDataRoot(legacyRoot, newRoot, newRoot);

            Assert.False(Directory.Exists(newRoot));
            Assert.True(File.Exists(Path.Combine(legacyRoot, DirectoryNames.CasPool, "objects", "blob.bin")));
        }
        finally
        {
            DeleteDirectories(legacyRoot, newRoot);
        }
    }

    /// <summary>
    /// Verifies that the sub-layout releases up to v0.0.3 wrote, which nested the manifests, tracked
    /// user data and workspace metadata under a Content directory, is flattened into the data root.
    /// </summary>
    [Fact]
    public void MigrateLegacyDataRoot_WithContentSubLayout_FlattensIntoDataRoot()
    {
        var (legacyRoot, newRoot) = CreateMigrationRoots();
        try
        {
            var legacyContent = Path.Combine(legacyRoot, DirectoryNames.LegacyContent);
            WriteFile(Path.Combine(legacyRoot, DirectoryNames.Profiles, "profile.json"), "profile");
            WriteFile(Path.Combine(legacyContent, FileTypes.ManifestsDirectory, "content.manifest.json"), "manifest");
            WriteFile(Path.Combine(legacyContent, DirectoryNames.UserData, FileTypes.UserDataIndexFileName), "index");
            WriteFile(Path.Combine(legacyContent, FileTypes.WorkspaceMetadataFileName), "workspaces");

            CreateProvider().MigrateLegacyDataRoot(legacyRoot, newRoot, newRoot);

            Assert.Equal("profile", File.ReadAllText(Path.Combine(newRoot, DirectoryNames.Profiles, "profile.json")));
            Assert.Equal("manifest", File.ReadAllText(Path.Combine(newRoot, FileTypes.ManifestsDirectory, "content.manifest.json")));
            Assert.Equal("index", File.ReadAllText(Path.Combine(newRoot, DirectoryNames.UserData, FileTypes.UserDataIndexFileName)));
            Assert.Equal("workspaces", File.ReadAllText(Path.Combine(newRoot, FileTypes.WorkspaceMetadataFileName)));
        }
        finally
        {
            DeleteDirectories(legacyRoot, newRoot);
        }
    }

    /// <summary>
    /// Verifies that the settings file releases up to v0.0.3 wrote, which was named after the JSON
    /// extension rather than the settings file name, is migrated under the current name.
    /// </summary>
    [Fact]
    public void MigrateLegacyDataRoot_WithLegacySettingsFileName_MigratesUnderCurrentName()
    {
        var (legacyRoot, newRoot) = CreateMigrationRoots();
        try
        {
            WriteFile(Path.Combine(legacyRoot, FileTypes.LegacySettingsFileName), "settings");

            CreateProvider().MigrateLegacyDataRoot(legacyRoot, newRoot, newRoot);

            Assert.Equal("settings", File.ReadAllText(Path.Combine(newRoot, FileTypes.SettingsFileName)));
            Assert.False(File.Exists(Path.Combine(legacyRoot, FileTypes.LegacySettingsFileName)));
        }
        finally
        {
            DeleteDirectories(legacyRoot, newRoot);
        }
    }

    /// <summary>
    /// Verifies that a settings file already under the current name wins over the v0.0.3 one.
    /// </summary>
    [Fact]
    public void MigrateLegacyDataRoot_WithBothSettingsFileNames_PrefersTheCurrentName()
    {
        var (legacyRoot, newRoot) = CreateMigrationRoots();
        try
        {
            WriteFile(Path.Combine(legacyRoot, FileTypes.SettingsFileName), "current");
            WriteFile(Path.Combine(legacyRoot, FileTypes.LegacySettingsFileName), "older");

            CreateProvider().MigrateLegacyDataRoot(legacyRoot, newRoot, newRoot);

            Assert.Equal("current", File.ReadAllText(Path.Combine(newRoot, FileTypes.SettingsFileName)));
        }
        finally
        {
            DeleteDirectories(legacyRoot, newRoot);
        }
    }

    /// <summary>
    /// Verifies that the data consumers read through the application data path lands in the override
    /// root while the settings file, which is resolved from the configured root, lands there instead.
    /// </summary>
    [Fact]
    public void MigrateLegacyDataRoot_WithSeparateDataAndSettingsRoots_SplitsTheDestinations()
    {
        var (legacyRoot, newRoot) = CreateMigrationRoots();
        var overrideRoot = Path.Combine(Path.GetDirectoryName(newRoot)!, "relocated");
        try
        {
            SeedLegacyRoot(legacyRoot);

            CreateProvider().MigrateLegacyDataRoot(legacyRoot, overrideRoot, newRoot);

            Assert.Equal("profile", File.ReadAllText(Path.Combine(overrideRoot, DirectoryNames.Profiles, "profile.json")));
            Assert.Equal("manifest", File.ReadAllText(Path.Combine(overrideRoot, FileTypes.ManifestsDirectory, "content.manifest.json")));
            Assert.Equal("index", File.ReadAllText(Path.Combine(overrideRoot, DirectoryNames.UserData, FileTypes.UserDataIndexFileName)));
            Assert.Equal("workspaces", File.ReadAllText(Path.Combine(overrideRoot, FileTypes.WorkspaceMetadataFileName)));

            Assert.Equal("settings", File.ReadAllText(Path.Combine(newRoot, FileTypes.SettingsFileName)));
            Assert.False(File.Exists(Path.Combine(overrideRoot, FileTypes.SettingsFileName)));
            Assert.False(Directory.Exists(Path.Combine(newRoot, DirectoryNames.Profiles)));
        }
        finally
        {
            DeleteDirectories(legacyRoot, newRoot);
        }
    }

    /// <summary>
    /// Creates a fresh legacy and current data root pair under the temp directory.
    /// </summary>
    /// <returns>The legacy and current root paths.</returns>
    private static (string LegacyRoot, string NewRoot) CreateMigrationRoots()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"genhub-migration-{Guid.NewGuid():N}");
        var legacyRoot = Path.Combine(testRoot, "roaming");
        var newRoot = Path.Combine(testRoot, "local");
        Directory.CreateDirectory(legacyRoot);
        Directory.CreateDirectory(newRoot);
        return (legacyRoot, newRoot);
    }

    /// <summary>
    /// Populates a legacy data root with the entries an alpha-3 install would contain.
    /// </summary>
    /// <param name="legacyRoot">The legacy data root to populate.</param>
    private static void SeedLegacyRoot(string legacyRoot)
    {
        WriteFile(Path.Combine(legacyRoot, DirectoryNames.Profiles, "profile.json"), "profile");
        WriteFile(Path.Combine(legacyRoot, FileTypes.ManifestsDirectory, "content.manifest.json"), "manifest");
        WriteFile(Path.Combine(legacyRoot, DirectoryNames.UserData, FileTypes.UserDataIndexFileName), "index");
        WriteFile(Path.Combine(legacyRoot, DirectoryNames.UserData, DirectoryNames.UserDataBackups, "save.bak"), "backup");
        WriteFile(Path.Combine(legacyRoot, FileTypes.SettingsFileName), "settings");
        WriteFile(Path.Combine(legacyRoot, FileTypes.WorkspaceMetadataFileName), "workspaces");
        WriteFile(Path.Combine(legacyRoot, DirectoryNames.CasPool, "objects", "blob.bin"), "cas");
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void DeleteDirectories(params string[] paths)
    {
        foreach (var path in paths.Select(Path.GetDirectoryName).Where(path => !string.IsNullOrEmpty(path)).Distinct())
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path!, true);
                }
            }
            catch (IOException)
            {
            }
        }
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
