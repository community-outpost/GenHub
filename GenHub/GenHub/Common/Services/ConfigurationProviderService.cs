using System;
using System.Collections.Generic;
using System.IO;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.Services;

/// <summary>
/// Unified configuration service that intelligently combines app config and user settings to provide effective values.
/// This is the single service that other components should depend on for all configuration needs.
/// </summary>
public class ConfigurationProviderService : IConfigurationProviderService
{
    private readonly IAppConfiguration _appConfig;
    private readonly IUserSettingsService _userSettings;
    private readonly ILogger<ConfigurationProviderService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationProviderService"/> class.
    /// </summary>
    /// <param name="appConfig">The application-level configuration service.</param>
    /// <param name="userSettings">The user settings service.</param>
    /// <param name="logger">The logger instance.</param>
    public ConfigurationProviderService(
        IAppConfiguration appConfig,
        IUserSettingsService userSettings,
        ILogger<ConfigurationProviderService> logger)
    {
        _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
        _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string GetWorkspacePath()
    {
        var settings = _userSettings.GetSettings();
        if (settings.IsExplicitlySet(nameof(UserSettings.WorkspacePath)) &&
            !string.IsNullOrWhiteSpace(settings.WorkspacePath))
        {
            try
            {
                // Check if the directory exists or can be created.
                var dir = Path.GetDirectoryName(settings.WorkspacePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    return settings.WorkspacePath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "User-defined workspace path '{Path}' is invalid. Falling back to default.", settings.WorkspacePath);
            }
        }

        return _appConfig.GetDefaultWorkspacePath();
    }

    /// <inheritdoc />
    public string GetCacheDirectory()
    {
        var settings = _userSettings.GetSettings();
        if (settings.IsExplicitlySet(nameof(UserSettings.CachePath)) &&
            !string.IsNullOrWhiteSpace(settings.CachePath))
        {
            try
            {
                // Validate the user-defined cache directory
                if (Directory.Exists(settings.CachePath))
                {
                    return settings.CachePath;
                }

                var parentDir = Path.GetDirectoryName(settings.CachePath);
                if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                {
                    return settings.CachePath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "User-defined cache path '{Path}' is invalid. Falling back to default.", settings.CachePath);
            }
        }

        return _appConfig.GetDefaultCacheDirectory();
    }

    /// <inheritdoc />
    public int GetMaxConcurrentDownloads()
    {
        var settings = _userSettings.GetSettings();
        var value = settings.IsExplicitlySet(nameof(UserSettings.MaxConcurrentDownloads)) && settings.MaxConcurrentDownloads > 0
            ? settings.MaxConcurrentDownloads
            : _appConfig.GetDefaultMaxConcurrentDownloads();
        return Math.Clamp(value, _appConfig.GetMinConcurrentDownloads(), _appConfig.GetMaxConcurrentDownloads());
    }

    /// <inheritdoc />
    public bool GetAllowBackgroundDownloads()
    {
        var settings = _userSettings.GetSettings();
        return settings.IsExplicitlySet(nameof(UserSettings.AllowBackgroundDownloads))
            ? settings.AllowBackgroundDownloads
            : true; // App default
    }

    /// <inheritdoc />
    public int GetDownloadTimeoutSeconds()
    {
        var settings = _userSettings.GetSettings();
        var value = settings.IsExplicitlySet(nameof(UserSettings.DownloadTimeoutSeconds)) && settings.DownloadTimeoutSeconds > 0
            ? settings.DownloadTimeoutSeconds
            : _appConfig.GetDefaultDownloadTimeoutSeconds();
        return Math.Clamp(value, _appConfig.GetMinDownloadTimeoutSeconds(), _appConfig.GetMaxDownloadTimeoutSeconds());
    }

    /// <inheritdoc />
    public string GetDownloadUserAgent()
    {
        var settings = _userSettings.GetSettings();
        return settings.IsExplicitlySet(nameof(UserSettings.DownloadUserAgent)) && !string.IsNullOrWhiteSpace(settings.DownloadUserAgent)
            ? settings.DownloadUserAgent
            : _appConfig.GetDefaultUserAgent();
    }

    /// <inheritdoc />
    public int GetDownloadBufferSize()
    {
        var settings = _userSettings.GetSettings();
        var value = settings.IsExplicitlySet(nameof(UserSettings.DownloadBufferSize)) && settings.DownloadBufferSize > 0
            ? settings.DownloadBufferSize
            : _appConfig.GetDefaultDownloadBufferSize();

        return Math.Clamp(value, _appConfig.GetMinDownloadBufferSizeBytes(), _appConfig.GetMaxDownloadBufferSizeBytes());
    }

    /// <inheritdoc />
    public WorkspaceStrategy GetDefaultWorkspaceStrategy()
    {
        var settings = _userSettings.GetSettings();
        return settings.IsExplicitlySet(nameof(UserSettings.DefaultWorkspaceStrategy))
            ? settings.DefaultWorkspaceStrategy
            : _appConfig.GetDefaultWorkspaceStrategy();
    }

    /// <inheritdoc />
    public bool GetAutoCheckForUpdatesOnStartup()
    {
        var settings = _userSettings.GetSettings();
        return settings.IsExplicitlySet(nameof(UserSettings.AutoCheckForUpdatesOnStartup))
            ? settings.AutoCheckForUpdatesOnStartup
            : true; // App default
    }

    /// <inheritdoc />
    public bool GetEnableDetailedLogging()
    {
        var settings = _userSettings.GetSettings();
        return settings.IsExplicitlySet(nameof(UserSettings.EnableDetailedLogging))
            ? settings.EnableDetailedLogging
            : false; // App default
    }

    /// <inheritdoc />
    public string GetTheme()
    {
        var settings = _userSettings.GetSettings();
        return settings.IsExplicitlySet(nameof(UserSettings.Theme)) && !string.IsNullOrWhiteSpace(settings.Theme)
            ? settings.Theme
            : _appConfig.GetDefaultTheme();
    }

    /// <inheritdoc />
    public double GetWindowWidth()
    {
        var settings = _userSettings.GetSettings();
        if (settings.IsExplicitlySet(nameof(UserSettings.WindowWidth)) && settings.WindowWidth > 0)
        {
            return settings.WindowWidth;
        }

        return _appConfig.GetDefaultWindowWidth();
    }

    /// <inheritdoc />
    public double GetWindowHeight()
    {
        var settings = _userSettings.GetSettings();
        if (settings.IsExplicitlySet(nameof(UserSettings.WindowHeight)) && settings.WindowHeight > 0)
        {
            return settings.WindowHeight;
        }

        return _appConfig.GetDefaultWindowHeight();
    }

    /// <inheritdoc />
    public bool GetIsWindowMaximized()
    {
        var settings = _userSettings.GetSettings();
        return settings.IsExplicitlySet(nameof(UserSettings.IsMaximized))
            ? settings.IsMaximized
            : false; // App default
    }

    /// <inheritdoc />
    public NavigationTab GetLastSelectedTab()
    {
        var settings = _userSettings.GetSettings();
        return settings.IsExplicitlySet(nameof(UserSettings.LastSelectedTab))
            ? settings.LastSelectedTab
            : NavigationTab.Home; // App default
    }

    /// <inheritdoc />
    public UserSettings GetEffectiveSettings()
    {
        return new UserSettings
        {
            Theme = GetTheme(),
            WindowWidth = GetWindowWidth(),
            WindowHeight = GetWindowHeight(),
            IsMaximized = GetIsWindowMaximized(),
            WorkspacePath = GetWorkspacePath(),
            LastUsedProfileId = _userSettings.GetSettings().LastUsedProfileId,
            LastSelectedTab = GetLastSelectedTab(),
            MaxConcurrentDownloads = GetMaxConcurrentDownloads(),
            AllowBackgroundDownloads = GetAllowBackgroundDownloads(),
            AutoCheckForUpdatesOnStartup = GetAutoCheckForUpdatesOnStartup(),
            LastUpdateCheckTimestamp = _userSettings.GetSettings().LastUpdateCheckTimestamp,
            EnableDetailedLogging = GetEnableDetailedLogging(),
            DefaultWorkspaceStrategy = GetDefaultWorkspaceStrategy(),
            DownloadBufferSize = GetDownloadBufferSize(),
            DownloadTimeoutSeconds = GetDownloadTimeoutSeconds(),
            DownloadUserAgent = GetDownloadUserAgent(),
            SettingsFilePath = _userSettings.GetSettings().SettingsFilePath,
        };
    }

    /// <inheritdoc />
    public List<string> GetContentDirectories()
    {
        var settings = _userSettings.GetSettings();
        if (settings.IsExplicitlySet(nameof(UserSettings.ContentDirectories)) &&
            settings.ContentDirectories != null && settings.ContentDirectories.Count > 0)
            return settings.ContentDirectories;

        return new List<string>
        {
            Path.Combine(_appConfig.GetConfiguredDataPath(), "Manifests"),
            Path.Combine(_appConfig.GetConfiguredDataPath(), "CustomManifests"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Command and Conquer Generals Zero Hour Data",
                "Mods"),
        };
    }

    /// <inheritdoc />
    public List<string> GetGitHubDiscoveryRepositories()
    {
        var settings = _userSettings.GetSettings();
        if (settings.IsExplicitlySet(nameof(UserSettings.GitHubDiscoveryRepositories)) &&
            settings.GitHubDiscoveryRepositories != null && settings.GitHubDiscoveryRepositories.Count > 0)
            return settings.GitHubDiscoveryRepositories;

        return new List<string> { "TheSuperHackers/GeneralsGameCode" };
    }

    /// <inheritdoc />
    public string GetContentStoragePath()
    {
        var settings = _userSettings.GetSettings();
        if (settings.IsExplicitlySet(nameof(UserSettings.ContentStoragePath)) &&
            !string.IsNullOrWhiteSpace(settings.ContentStoragePath))
        {
            return settings.ContentStoragePath;
        }

        return Path.Combine(_appConfig.GetConfiguredDataPath(), "Content");
    }
}
