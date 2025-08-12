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
        var s = _userSettings.GetSettings();
        if (s.IsExplicitlySet(nameof(UserSettings.WorkspacePath)) &&
            !string.IsNullOrWhiteSpace(s.WorkspacePath))
        {
            try
            {
                // Check if the directory exists or can be created.
                var dir = Path.GetDirectoryName(s.WorkspacePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    return s.WorkspacePath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "User-defined workspace path '{Path}' is invalid. Falling back to default.", s.WorkspacePath);
            }
        }

        return _appConfig.GetDefaultWorkspacePath();
    }

    /// <inheritdoc />
    public string GetCacheDirectory()
    {
        var s = _userSettings.GetSettings();
        if (s.IsExplicitlySet(nameof(UserSettings.CachePath)) &&
            !string.IsNullOrWhiteSpace(s.CachePath))
        {
            try
            {
                // Validate the user-defined cache directory
                if (Directory.Exists(s.CachePath))
                {
                    return s.CachePath;
                }

                var parentDir = Path.GetDirectoryName(s.CachePath);
                if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                {
                    return s.CachePath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "User-defined cache path '{Path}' is invalid. Falling back to default.", s.CachePath);
            }
        }

        return _appConfig.GetDefaultCacheDirectory();
    }

    /// <inheritdoc />
    public int GetMaxConcurrentDownloads()
    {
        var s = _userSettings.GetSettings();
        var value = s.IsExplicitlySet(nameof(UserSettings.MaxConcurrentDownloads)) && s.MaxConcurrentDownloads > 0
            ? s.MaxConcurrentDownloads
            : _appConfig.GetDefaultMaxConcurrentDownloads();
        return Math.Clamp(value, _appConfig.GetMinConcurrentDownloads(), _appConfig.GetMaxConcurrentDownloads());
    }

    /// <inheritdoc />
    public bool GetAllowBackgroundDownloads()
    {
        var s = _userSettings.GetSettings();
        return s.IsExplicitlySet(nameof(UserSettings.AllowBackgroundDownloads))
            ? s.AllowBackgroundDownloads
            : true; // App default
    }

    /// <inheritdoc />
    public int GetDownloadTimeoutSeconds()
    {
        var s = _userSettings.GetSettings();
        var value = s.IsExplicitlySet(nameof(UserSettings.DownloadTimeoutSeconds)) && s.DownloadTimeoutSeconds > 0
            ? s.DownloadTimeoutSeconds
            : _appConfig.GetDefaultDownloadTimeoutSeconds();
        return Math.Clamp(value, _appConfig.GetMinDownloadTimeoutSeconds(), _appConfig.GetMaxDownloadTimeoutSeconds());
    }

    /// <inheritdoc />
    public string GetDownloadUserAgent()
    {
        var s = _userSettings.GetSettings();
        return s.IsExplicitlySet(nameof(UserSettings.DownloadUserAgent)) && !string.IsNullOrWhiteSpace(s.DownloadUserAgent)
            ? s.DownloadUserAgent
            : _appConfig.GetDefaultUserAgent();
    }

    /// <inheritdoc />
    public int GetDownloadBufferSize()
    {
        var s = _userSettings.GetSettings();
        var value = s.IsExplicitlySet(nameof(UserSettings.DownloadBufferSize)) && s.DownloadBufferSize > 0
            ? s.DownloadBufferSize
            : _appConfig.GetDefaultDownloadBufferSize();

        return Math.Clamp(value, _appConfig.GetMinDownloadBufferSizeBytes(), _appConfig.GetMaxDownloadBufferSizeBytes());
    }

    /// <inheritdoc />
    public WorkspaceStrategy GetDefaultWorkspaceStrategy()
    {
        var s = _userSettings.GetSettings();
        return s.IsExplicitlySet(nameof(UserSettings.DefaultWorkspaceStrategy))
            ? s.DefaultWorkspaceStrategy
            : _appConfig.GetDefaultWorkspaceStrategy();
    }

    /// <inheritdoc />
    public bool GetAutoCheckForUpdatesOnStartup()
    {
        var s = _userSettings.GetSettings();
        return s.IsExplicitlySet(nameof(UserSettings.AutoCheckForUpdatesOnStartup))
            ? s.AutoCheckForUpdatesOnStartup
            : true; // App default
    }

    /// <inheritdoc />
    public bool GetEnableDetailedLogging()
    {
        var s = _userSettings.GetSettings();
        return s.IsExplicitlySet(nameof(UserSettings.EnableDetailedLogging))
            ? s.EnableDetailedLogging
            : false; // App default
    }

    /// <inheritdoc />
    public string GetTheme()
    {
        var s = _userSettings.GetSettings();
        return s.IsExplicitlySet(nameof(UserSettings.Theme)) && !string.IsNullOrWhiteSpace(s.Theme)
            ? s.Theme
            : _appConfig.GetDefaultTheme();
    }

    /// <inheritdoc />
    public double GetWindowWidth()
    {
        var s = _userSettings.GetSettings();
        return s.IsExplicitlySet(nameof(UserSettings.WindowWidth)) && s.WindowWidth > 0
            ? s.WindowWidth
            : _appConfig.GetDefaultWindowWidth();
    }

    /// <inheritdoc />
    public double GetWindowHeight()
    {
        var s = _userSettings.GetSettings();
        return s.IsExplicitlySet(nameof(UserSettings.WindowHeight)) && s.WindowHeight > 0
            ? s.WindowHeight
            : _appConfig.GetDefaultWindowHeight();
    }

    /// <inheritdoc />
    public bool GetIsWindowMaximized()
    {
        var s = _userSettings.GetSettings();
        return s.IsExplicitlySet(nameof(UserSettings.IsMaximized))
            ? s.IsMaximized
            : false; // App default
    }

    /// <inheritdoc />
    public NavigationTab GetLastSelectedTab()
    {
        var s = _userSettings.GetSettings();
        return s.IsExplicitlySet(nameof(UserSettings.LastSelectedTab))
            ? s.LastSelectedTab
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
        var s = _userSettings.GetSettings();
        if (s.IsExplicitlySet(nameof(UserSettings.ContentDirectories)) &&
            s.ContentDirectories != null && s.ContentDirectories.Count > 0)
            return s.ContentDirectories;

        return new List<string>
        {
            Path.Combine(_appConfig.GetAppDataPath(), "Manifests"),
            Path.Combine(_appConfig.GetAppDataPath(), "CustomManifests"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Command and Conquer Generals Zero Hour Data",
                "Mods"),
        };
    }

    /// <inheritdoc />
    public List<string> GetGitHubDiscoveryRepositories()
    {
        var s = _userSettings.GetSettings();
        if (s.IsExplicitlySet(nameof(UserSettings.GitHubDiscoveryRepositories)) &&
            s.GitHubDiscoveryRepositories != null && s.GitHubDiscoveryRepositories.Count > 0)
            return s.GitHubDiscoveryRepositories;

        return new List<string> { "TheSuperHackers/GeneralsGameCode" };
    }

    /// <inheritdoc />
    public string GetContentStoragePath()
    {
        var s = _userSettings.GetSettings();
        if (s.IsExplicitlySet(nameof(UserSettings.ContentStoragePath)) &&
            !string.IsNullOrWhiteSpace(s.ContentStoragePath))
        {
            return s.ContentStoragePath;
        }

        return Path.Combine(_appConfig.GetAppDataPath(), "Content");
    }
}
