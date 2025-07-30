using System;
using System.IO;
using GenHub.Common.Services;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>
/// Tests for <see cref="ConfigurationProvider"/>.
/// </summary>
public class ConfigurationProviderTests
{
    private readonly Mock<IAppConfigurationService> _mockAppConfig;
    private readonly Mock<IUserSettingsService> _mockUserSettings;
    private readonly Mock<ILogger<ConfigurationProvider>> _mockLogger;
    private readonly AppSettings _defaultUserSettings;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationProviderTests"/> class.
    /// </summary>
    public ConfigurationProviderTests()
    {
        _mockAppConfig = new Mock<IAppConfigurationService>();
        _mockUserSettings = new Mock<IUserSettingsService>();
        _mockLogger = new Mock<ILogger<ConfigurationProvider>>();
        _defaultUserSettings = new AppSettings();

        // Setup default returns for user settings
        _mockUserSettings.Setup(x => x.GetSettings()).Returns(_defaultUserSettings);
    }

    /// <summary>
    /// Verifies that the constructor initializes correctly with valid dependencies.
    /// </summary>
    [Fact]
    public void Constructor_WithValidDependencies_DoesNotThrow()
    {
        var provider = new ConfigurationProvider(_mockAppConfig.Object, _mockUserSettings.Object, _mockLogger.Object);
        Assert.NotNull(provider);
    }

    /// <summary>
    /// Verifies that the constructor throws ArgumentNullException when appConfig is null.
    /// </summary>
    [Fact]
    public void Constructor_WithNullAppConfig_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ConfigurationProvider(null!, _mockUserSettings.Object, _mockLogger.Object));
    }

    /// <summary>
    /// Verifies that the constructor throws ArgumentNullException when userSettings is null.
    /// </summary>
    [Fact]
    public void Constructor_WithNullUserSettings_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ConfigurationProvider(_mockAppConfig.Object, null!, _mockLogger.Object));
    }

    /// <summary>
    /// Verifies that the constructor throws ArgumentNullException when logger is null.
    /// </summary>
    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ConfigurationProvider(_mockAppConfig.Object, _mockUserSettings.Object, null!));
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
            var userSettings = new AppSettings { WorkspacePath = userPath };
            _mockUserSettings.Setup(x => x.GetSettings()).Returns(userSettings);

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
        var userSettings = new AppSettings { WorkspacePath = null };

        _mockUserSettings.Setup(x => x.GetSettings()).Returns(userSettings);
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
        var userSettings = new AppSettings { WorkspacePath = nonExistentPath };

        _mockUserSettings.Setup(x => x.GetSettings()).Returns(userSettings);
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
        var userSettings = new AppSettings { WorkspacePath = string.Empty };

        _mockUserSettings.Setup(x => x.GetSettings()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetDefaultWorkspacePath()).Returns(appDefault);

        var provider = CreateProvider();

        // Act
        var result = provider.GetWorkspacePath();

        // Assert
        Assert.Equal(appDefault, result);
        _mockAppConfig.Verify(x => x.GetDefaultWorkspacePath(), Times.Once);
    }

    /// <summary>
    /// Verifies that GetCacheDirectory returns app configuration default.
    /// </summary>
    [Fact]
    public void GetCacheDirectory_ReturnsAppDefault()
    {
        // Arrange
        var appDefault = "/app/cache/directory";
        _mockAppConfig.Setup(x => x.GetDefaultCacheDirectory()).Returns(appDefault);

        var provider = CreateProvider();

        // Act
        var result = provider.GetCacheDirectory();

        // Assert
        Assert.Equal(appDefault, result);
        _mockAppConfig.Verify(x => x.GetDefaultCacheDirectory(), Times.Once);
    }

    /// <summary>
    /// Verifies that GetMaxConcurrentDownloads returns user setting when greater than 0.
    /// </summary>
    [Fact]
    public void GetMaxConcurrentDownloads_WithValidUserSetting_ReturnsUserSetting()
    {
        // Arrange
        var userSettings = new AppSettings { MaxConcurrentDownloads = 5 };
        _mockUserSettings.Setup(x => x.GetSettings()).Returns(userSettings);

        var provider = CreateProvider();

        // Act
        var result = provider.GetMaxConcurrentDownloads();

        // Assert
        Assert.Equal(5, result);
        _mockAppConfig.Verify(x => x.GetDefaultMaxConcurrentDownloads(), Times.Never);
    }

    /// <summary>
    /// Verifies that GetMaxConcurrentDownloads returns app default when user setting is 0.
    /// </summary>
    [Fact]
    public void GetMaxConcurrentDownloads_WithZeroUserSetting_ReturnsAppDefault()
    {
        // Arrange
        var userSettings = new AppSettings { MaxConcurrentDownloads = 0 };
        _mockUserSettings.Setup(x => x.GetSettings()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetDefaultMaxConcurrentDownloads()).Returns(8);

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
        var userSettings = new AppSettings { MaxConcurrentDownloads = -1 };
        _mockUserSettings.Setup(x => x.GetSettings()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetDefaultMaxConcurrentDownloads()).Returns(3);

        var provider = CreateProvider();

        // Act
        var result = provider.GetMaxConcurrentDownloads();

        // Assert
        Assert.Equal(3, result);
        _mockAppConfig.Verify(x => x.GetDefaultMaxConcurrentDownloads(), Times.Once);
    }

    /// <summary>
    /// Verifies that GetAllowBackgroundDownloads returns user setting.
    /// </summary>
    /// <param name="userValue">The value to set for AllowBackgroundDownloads in user settings.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetAllowBackgroundDownloads_ReturnsUserSetting(bool userValue)
    {
        // Arrange
        var userSettings = new AppSettings { AllowBackgroundDownloads = userValue };
        _mockUserSettings.Setup(x => x.GetSettings()).Returns(userSettings);

        var provider = CreateProvider();

        // Act
        var result = provider.GetAllowBackgroundDownloads();

        // Assert
        Assert.Equal(userValue, result);
    }

    /// <summary>
    /// Verifies that GetDownloadTimeoutSeconds returns user setting when greater than 0.
    /// </summary>
    [Fact]
    public void GetDownloadTimeoutSeconds_WithValidUserSetting_ReturnsUserSetting()
    {
        // Arrange
        var userSettings = new AppSettings { DownloadTimeoutSeconds = 300 };
        _mockUserSettings.Setup(x => x.GetSettings()).Returns(userSettings);

        var provider = CreateProvider();

        // Act
        var result = provider.GetDownloadTimeoutSeconds();

        // Assert
        Assert.Equal(300, result);
        _mockAppConfig.Verify(x => x.GetDefaultDownloadTimeoutSeconds(), Times.Never);
    }

    /// <summary>
    /// Verifies that GetDownloadTimeoutSeconds returns app default when user setting is 0.
    /// </summary>
    [Fact]
    public void GetDownloadTimeoutSeconds_WithZeroUserSetting_ReturnsAppDefault()
    {
        // Arrange
        var userSettings = new AppSettings { DownloadTimeoutSeconds = 0 };
        _mockUserSettings.Setup(x => x.GetSettings()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetDefaultDownloadTimeoutSeconds()).Returns(600);

        var provider = CreateProvider();

        // Act
        var result = provider.GetDownloadTimeoutSeconds();

        // Assert
        Assert.Equal(600, result);
        _mockAppConfig.Verify(x => x.GetDefaultDownloadTimeoutSeconds(), Times.Once);
    }

    /// <summary>
    /// Verifies that GetDownloadUserAgent returns user setting when not null or empty.
    /// </summary>
    [Fact]
    public void GetDownloadUserAgent_WithValidUserSetting_ReturnsUserSetting()
    {
        // Arrange
        var userAgent = "CustomAgent/2.0";
        var userSettings = new AppSettings { DownloadUserAgent = userAgent };
        _mockUserSettings.Setup(x => x.GetSettings()).Returns(userSettings);

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
        var userSettings = new AppSettings { DownloadUserAgent = string.Empty };
        _mockUserSettings.Setup(x => x.GetSettings()).Returns(userSettings);
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
        var userSettings = new AppSettings { DownloadUserAgent = string.Empty };
        _mockUserSettings.Setup(x => x.GetSettings()).Returns(userSettings);
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
        var userSettings = new AppSettings { DownloadUserAgent = "   " };
        _mockUserSettings.Setup(x => x.GetSettings()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetDefaultUserAgent()).Returns(appDefault);

        var provider = CreateProvider();

        // Act
        var result = provider.GetDownloadUserAgent();

        // Assert
        Assert.Equal(appDefault, result);
        _mockAppConfig.Verify(x => x.GetDefaultUserAgent(), Times.Once);
    }

    /// <summary>
    /// Verifies that GetDownloadBufferSize returns user setting.
    /// </summary>
    [Fact]
    public void GetDownloadBufferSize_ReturnsUserSetting()
    {
        // Arrange
        var bufferSize = 16384;
        var userSettings = new AppSettings { DownloadBufferSize = bufferSize };
        _mockUserSettings.Setup(x => x.GetSettings()).Returns(userSettings);

        var provider = CreateProvider();

        // Act
        var result = provider.GetDownloadBufferSize();

        // Assert
        Assert.Equal(bufferSize, result);
    }

    /// <summary>
    /// Verifies that GetDefaultWorkspaceStrategy returns user setting.
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
        var userSettings = new AppSettings { DefaultWorkspaceStrategy = strategy };
        _mockUserSettings.Setup(x => x.GetSettings()).Returns(userSettings);

        var provider = CreateProvider();

        // Act
        var result = provider.GetDefaultWorkspaceStrategy();

        // Assert
        Assert.Equal(strategy, result);
    }

    /// <summary>
    /// Verifies that GetAutoCheckForUpdatesOnStartup returns user setting.
    /// </summary>
    /// <param name="userValue">The value to set for AutoCheckForUpdatesOnStartup in user settings.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetAutoCheckForUpdatesOnStartup_ReturnsUserSetting(bool userValue)
    {
        // Arrange
        var userSettings = new AppSettings { AutoCheckForUpdatesOnStartup = userValue };
        _mockUserSettings.Setup(x => x.GetSettings()).Returns(userSettings);

        var provider = CreateProvider();

        // Act
        var result = provider.GetAutoCheckForUpdatesOnStartup();

        // Assert
        Assert.Equal(userValue, result);
    }

    /// <summary>
    /// Verifies that GetEnableDetailedLogging returns user setting.
    /// </summary>
    /// <param name="userValue">The value to set for EnableDetailedLogging in user settings.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetEnableDetailedLogging_ReturnsUserSetting(bool userValue)
    {
        // Arrange
        var userSettings = new AppSettings { EnableDetailedLogging = userValue };
        _mockUserSettings.Setup(x => x.GetSettings()).Returns(userSettings);

        var provider = CreateProvider();

        // Act
        var result = provider.GetEnableDetailedLogging();

        // Assert
        Assert.Equal(userValue, result);
    }

    /// <summary>
    /// Verifies that multiple calls to GetSettings don't cause issues with caching.
    /// </summary>
    [Fact]
    public void MultipleMethodCalls_WorkCorrectly()
    {
        // Arrange
        var userSettings = new AppSettings
        {
            MaxConcurrentDownloads = 7,
            AllowBackgroundDownloads = false,
            EnableDetailedLogging = true,
        };
        _mockUserSettings.Setup(x => x.GetSettings()).Returns(userSettings);
        _mockAppConfig.Setup(x => x.GetDefaultCacheDirectory()).Returns("/cache");

        var provider = CreateProvider();

        // Act & Assert
        Assert.Equal(7, provider.GetMaxConcurrentDownloads());
        Assert.False(provider.GetAllowBackgroundDownloads());
        Assert.True(provider.GetEnableDetailedLogging());
        Assert.Equal("/cache", provider.GetCacheDirectory());

        // Verify GetSettings was called for each method that needs user settings
        _mockUserSettings.Verify(x => x.GetSettings(), Times.AtLeast(3));
    }

    /// <summary>
    /// Creates a ConfigurationProvider instance for testing.
    /// </summary>
    /// <returns>A new ConfigurationProvider instance.</returns>
    private ConfigurationProvider CreateProvider()
    {
        return new ConfigurationProvider(_mockAppConfig.Object, _mockUserSettings.Object, _mockLogger.Object);
    }
}
