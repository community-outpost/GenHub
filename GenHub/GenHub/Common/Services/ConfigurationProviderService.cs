using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Storage;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.Services;

/// <summary>
/// Unified configuration service that intelligently combines app config and user settings to provide effective values.
/// This is the single service that other components should depend on for all configuration needs.
/// </summary>
public class ConfigurationProviderService(
    IAppConfiguration appConfig,
    IUserSettingsService userSettings,
    ILogger<ConfigurationProviderService> logger) : IConfigurationProviderService
{
    private readonly IAppConfiguration _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
    private readonly IUserSettingsService _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
    private readonly ILogger<ConfigurationProviderService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly object _migrationLock = new();
    private bool _migrated;

    /// <inheritdoc />
    public string GetWorkspacePath()
    {
        var settings = _userSettings.Get();
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
    public string GetCachePath()
    {
        var settings = _userSettings.Get();
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
        var settings = _userSettings.Get();
        var value = settings.IsExplicitlySet(nameof(UserSettings.MaxConcurrentDownloads)) && settings.MaxConcurrentDownloads > 0
            ? settings.MaxConcurrentDownloads
            : _appConfig.GetDefaultMaxConcurrentDownloads();
        return Math.Clamp(value, _appConfig.GetMinConcurrentDownloads(), _appConfig.GetMaxConcurrentDownloads());
    }

    /// <inheritdoc />
    public bool GetAllowBackgroundDownloads()
    {
        var settings = _userSettings.Get();
        return !settings.IsExplicitlySet(nameof(UserSettings.AllowBackgroundDownloads)) || settings.AllowBackgroundDownloads; // App default
    }

    /// <inheritdoc />
    public int GetDownloadTimeoutSeconds()
    {
        var settings = _userSettings.Get();
        var value = settings.IsExplicitlySet(nameof(UserSettings.DownloadTimeoutSeconds)) && settings.DownloadTimeoutSeconds > 0
            ? settings.DownloadTimeoutSeconds
            : _appConfig.GetDefaultDownloadTimeoutSeconds();
        return Math.Clamp(value, _appConfig.GetMinDownloadTimeoutSeconds(), _appConfig.GetMaxDownloadTimeoutSeconds());
    }

    /// <inheritdoc />
    public string GetDownloadUserAgent()
    {
        var settings = _userSettings.Get();
        return settings.IsExplicitlySet(nameof(UserSettings.DownloadUserAgent)) && !string.IsNullOrWhiteSpace(settings.DownloadUserAgent)
            ? settings.DownloadUserAgent
            : _appConfig.GetDefaultUserAgent();
    }

    /// <inheritdoc />
    public int GetDownloadBufferSize()
    {
        var settings = _userSettings.Get();
        var value = settings.IsExplicitlySet(nameof(UserSettings.DownloadBufferSize)) && settings.DownloadBufferSize > 0
            ? settings.DownloadBufferSize
            : _appConfig.GetDefaultDownloadBufferSize();

        return Math.Clamp(value, _appConfig.GetMinDownloadBufferSizeBytes(), _appConfig.GetMaxDownloadBufferSizeBytes());
    }

    /// <inheritdoc />
    public WorkspaceStrategy GetDefaultWorkspaceStrategy()
    {
        var settings = _userSettings.Get();
        return settings.IsExplicitlySet(nameof(UserSettings.DefaultWorkspaceStrategy))
            ? settings.DefaultWorkspaceStrategy
            : WorkspaceConstants.DefaultWorkspaceStrategy;
    }

    /// <inheritdoc />
    public bool GetAutoCheckForUpdatesOnStartup()
    {
        var settings = _userSettings.Get();
        return !settings.IsExplicitlySet(nameof(UserSettings.AutoCheckForUpdatesOnStartup)) || settings.AutoCheckForUpdatesOnStartup; // App default
    }

    /// <inheritdoc />
    public bool GetEnableDetailedLogging()
    {
        var settings = _userSettings.Get();
        return settings.IsExplicitlySet(nameof(UserSettings.EnableDetailedLogging)) && settings.EnableDetailedLogging; // App default
    }

    /// <inheritdoc />
    public string GetTheme()
    {
        var settings = _userSettings.Get();
        return settings.IsExplicitlySet(nameof(UserSettings.Theme)) && !string.IsNullOrWhiteSpace(settings.Theme)
            ? settings.Theme
            : _appConfig.GetDefaultTheme();
    }

    /// <inheritdoc />
    public double GetWindowWidth()
    {
        var settings = _userSettings.Get();
        if (settings.IsExplicitlySet(nameof(UserSettings.WindowWidth)) && settings.WindowWidth > 0)
        {
            return settings.WindowWidth;
        }

        return _appConfig.GetDefaultWindowWidth();
    }

    /// <inheritdoc />
    public double GetWindowHeight()
    {
        var settings = _userSettings.Get();
        if (settings.IsExplicitlySet(nameof(UserSettings.WindowHeight)) && settings.WindowHeight > 0)
        {
            return settings.WindowHeight;
        }

        return _appConfig.GetDefaultWindowHeight();
    }

    /// <inheritdoc />
    public bool GetIsWindowMaximized()
    {
        var settings = _userSettings.Get();
        return settings.IsExplicitlySet(nameof(UserSettings.IsMaximized)) && settings.IsMaximized; // App default
    }

    /// <inheritdoc />
    public NavigationTab GetLastSelectedTab()
    {
        var settings = _userSettings.Get();
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
            LastUsedProfileId = _userSettings.Get().LastUsedProfileId,
            LastSelectedTab = GetLastSelectedTab(),
            MaxConcurrentDownloads = GetMaxConcurrentDownloads(),
            AllowBackgroundDownloads = GetAllowBackgroundDownloads(),
            AutoCheckForUpdatesOnStartup = GetAutoCheckForUpdatesOnStartup(),
            LastUpdateCheckTimestamp = _userSettings.Get().LastUpdateCheckTimestamp,
            EnableDetailedLogging = GetEnableDetailedLogging(),
            DefaultWorkspaceStrategy = GetDefaultWorkspaceStrategy(),
            DownloadBufferSize = GetDownloadBufferSize(),
            DownloadTimeoutSeconds = GetDownloadTimeoutSeconds(),
            DownloadUserAgent = GetDownloadUserAgent(),
            SettingsFilePath = _userSettings.Get().SettingsFilePath,
            ContentDirectories = GetContentDirectories(),
            GitHubDiscoveryRepositories = GetGitHubDiscoveryRepositories(),
            ApplicationDataPath = GetApplicationDataPath(),
            CachePath = GetCachePath(),
            CasConfiguration = GetCasConfiguration(),
        };
    }

    /// <inheritdoc />
    public List<string> GetContentDirectories()
    {
        var settings = _userSettings.Get();
        if (settings.IsExplicitlySet(nameof(UserSettings.ContentDirectories)) &&
            settings.ContentDirectories != null && settings.ContentDirectories.Count > 0)
        {
            return settings.ContentDirectories;
        }

        return
        [
            Path.Combine(_appConfig.GetConfiguredDataPath(), FileTypes.ManifestsDirectory),
            Path.Combine(_appConfig.GetConfiguredDataPath(), "CustomManifests"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Command and Conquer Generals Zero Hour Data",
                "Mods"),
        ];
    }

    /// <inheritdoc />
    public List<string> GetGitHubDiscoveryRepositories()
    {
        var settings = _userSettings.Get();
        if (settings.IsExplicitlySet(nameof(UserSettings.GitHubDiscoveryRepositories)) &&
            settings.GitHubDiscoveryRepositories != null && settings.GitHubDiscoveryRepositories.Count > 0)
            return settings.GitHubDiscoveryRepositories;

        return ["TheSuperHackers/GeneralsGameCode"];
    }

    /// <inheritdoc />
    public string GetApplicationDataPath()
    {
        if (!_migrated)
        {
            lock (_migrationLock)
            {
                if (!_migrated)
                {
                    // Double-check
                    MigrateContentDirectory();
                    _migrated = true;
                }
            }
        }

        var settings = _userSettings.Get();
        if (settings.IsExplicitlySet(nameof(UserSettings.ApplicationDataPath)) &&
            !string.IsNullOrWhiteSpace(settings.ApplicationDataPath))
        {
            return settings.ApplicationDataPath;
        }

        return _appConfig.GetConfiguredDataPath();
    }

    /// <inheritdoc />
    public string GetRootAppDataPath() => _appConfig.GetConfiguredDataPath();

    /// <inheritdoc />
    public string GetProfilesPath() => Path.Combine(_appConfig.GetConfiguredDataPath(), DirectoryNames.Profiles);

    /// <inheritdoc />
    public string GetManifestsPath() => Path.Combine(_appConfig.GetConfiguredDataPath(), FileTypes.ManifestsDirectory);

    /// <inheritdoc />
    /// <remarks>
    /// Returns the current CAS configuration. If the path is not configured, a default path is applied
    /// to a new configuration instance.
    /// Note: Modifying the returned object will not update the persistent user settings.
    /// To update settings, use <see cref="IUserSettingsService.TryUpdateAndSaveAsync"/>.
    /// </remarks>
    public CasConfiguration GetCasConfiguration()
    {
        var settings = _userSettings.Get();
        var casConfig = settings.CasConfiguration;

        // If CasRootPath is empty, apply the default path
        if (string.IsNullOrWhiteSpace(casConfig.CasRootPath))
        {
            var defaultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppConstants.AppName,
                DirectoryNames.CasPool);

            return new CasConfiguration
            {
                CasRootPath = defaultPath,
                EnableAutomaticGc = casConfig.EnableAutomaticGc,
                HashAlgorithm = casConfig.HashAlgorithm,
                GcGracePeriod = casConfig.GcGracePeriod,
                MaxCacheSizeBytes = casConfig.MaxCacheSizeBytes,
                AutoGcInterval = casConfig.AutoGcInterval,
                MaxConcurrentOperations = casConfig.MaxConcurrentOperations,
                VerifyIntegrity = casConfig.VerifyIntegrity,
            };
        }

        return casConfig;
    }

    /// <inheritdoc />
    public string GetLogsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppConstants.AppName,
            DirectoryNames.Logs.ToLowerInvariant());
    }

    private void MigrateContentDirectory()
    {
        try
        {
            var rootPath = _appConfig.GetConfiguredDataPath();
            var contentPath = Path.Combine(rootPath, "Content");

            if (!Directory.Exists(contentPath))
            {
                return;
            }

            _logger.LogInformation("Migrating content from {ContentPath} to root {RootPath}", contentPath, rootPath);

            // 1. Move Manifests
            MigrateDirectory(Path.Combine(contentPath, "Manifests"), Path.Combine(rootPath, "Manifests"));

            // 2. Move UserData
            MigrateDirectory(Path.Combine(contentPath, "UserData"), Path.Combine(rootPath, "UserData"));

            // 3. Move workspaces.json
            var sourceWorkspaces = Path.Combine(contentPath, "workspaces.json");
            var destWorkspaces = Path.Combine(rootPath, "workspaces.json");
            if (File.Exists(sourceWorkspaces))
            {
                if (!File.Exists(destWorkspaces))
                {
                    File.Move(sourceWorkspaces, destWorkspaces);
                    _logger.LogInformation("Moved workspaces.json to root");
                }
                else
                {
                    _logger.LogWarning("workspaces.json already exists in root, keeping original in Content (backup)");
                }
            }

            // 4. Try to delete Content if empty
            try
            {
                if (Directory.GetFiles(contentPath).Length == 0 && Directory.GetDirectories(contentPath).Length == 0)
                {
                    Directory.Delete(contentPath);
                    _logger.LogInformation("Deleted empty Content directory");
                }
            }
            catch
            {
                // Ignore if not empty
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate Content directory");
        }
    }

    private void MigrateDirectory(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir)) return;

        if (!Directory.Exists(destDir))
        {
            Directory.Move(sourceDir, destDir);
            _logger.LogInformation("Moved {Source} to {Dest}", sourceDir, destDir);
            return;
        }

        // Destination exists, move content
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            if (!File.Exists(destFile))
            {
                File.Move(file, destFile);
            }
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            MigrateDirectory(subDir, destSubDir);
        }

        // Try delete source if empty
        try
        {
            if (!Directory.EnumerateFileSystemEntries(sourceDir).Any())
            {
                Directory.Delete(sourceDir);
            }
        }
        catch
        {
        }
    }
}
