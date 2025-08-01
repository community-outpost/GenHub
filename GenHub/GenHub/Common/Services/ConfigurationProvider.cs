using System;
using System.Collections.Generic;
using System.IO;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.Services;

/// <summary>
/// Unified configuration provider that intelligently combines app config and user settings.
/// </summary>
public class ConfigurationProvider : IConfigurationProvider
{
    private readonly IAppConfigurationService _appConfig;
    private readonly IUserSettingsService _userSettings;
    private readonly ILogger<ConfigurationProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationProvider"/> class.
    /// </summary>
    /// <param name="appConfig">The application-level configuration service.</param>
    /// <param name="userSettings">The user settings service.</param>
    /// <param name="logger">The logger instance.</param>
    public ConfigurationProvider(
        IAppConfigurationService appConfig,
        IUserSettingsService userSettings,
        ILogger<ConfigurationProvider> logger)
    {
        _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
        _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string GetWorkspacePath()
    {
        var userSettings = _userSettings.GetSettings();
        if (!string.IsNullOrWhiteSpace(userSettings.WorkspacePath))
        {
            try
            {
                // Check if the directory exists or can be created.
                var dir = Path.GetDirectoryName(userSettings.WorkspacePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    return userSettings.WorkspacePath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "User-defined workspace path '{Path}' is invalid. Falling back to default.", userSettings.WorkspacePath);
            }
        }

        return _appConfig.GetDefaultWorkspacePath();
    }

    /// <inheritdoc />
    public string GetCacheDirectory()
    {
        var userSettings = _userSettings.GetSettings();
        if (!string.IsNullOrWhiteSpace(userSettings.CachePath))
        {
            try
            {
                // Validate the user-defined cache directory
                if (Directory.Exists(userSettings.CachePath))
                {
                    return userSettings.CachePath;
                }

                var parentDir = Path.GetDirectoryName(userSettings.CachePath);
                if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                {
                    return userSettings.CachePath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "User-defined cache path '{Path}' is invalid. Falling back to default.", userSettings.CachePath);
            }
        }

        return _appConfig.GetDefaultCacheDirectory();
    }

    /// <inheritdoc />
    public List<string> GetContentDirectories()
    {
        var userSettings = _userSettings.GetSettings();
        if (userSettings.ContentDirectories != null && userSettings.ContentDirectories.Count > 0)
            return userSettings.ContentDirectories;
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
        var userSettings = _userSettings.GetSettings();
        if (userSettings.GitHubDiscoveryRepositories != null && userSettings.GitHubDiscoveryRepositories.Count > 0)
            return userSettings.GitHubDiscoveryRepositories;
        return new List<string> { "TheSuperHackers/GeneralsGameCode", };
    }

    /// <inheritdoc />
    public string GetContentStoragePath()
    {
        var userSettings = _userSettings.GetSettings();
        if (!string.IsNullOrWhiteSpace(userSettings.ContentStoragePath))
        {
            return userSettings.ContentStoragePath;
        }

        return Path.Combine(_appConfig.GetAppDataPath(), "Content");
    }

    /// <inheritdoc />
    public int GetMaxConcurrentDownloads()
    {
        var userSettings = _userSettings.GetSettings();
        return userSettings.MaxConcurrentDownloads > 0
            ? userSettings.MaxConcurrentDownloads
            : _appConfig.GetDefaultMaxConcurrentDownloads();
    }

    /// <inheritdoc />
    public bool GetAllowBackgroundDownloads() => _userSettings.GetSettings().AllowBackgroundDownloads;

    /// <inheritdoc />
    public int GetDownloadTimeoutSeconds()
    {
        var userSettings = _userSettings.GetSettings();
        return userSettings.DownloadTimeoutSeconds > 0
            ? userSettings.DownloadTimeoutSeconds
            : _appConfig.GetDefaultDownloadTimeoutSeconds();
    }

    /// <inheritdoc />
    public string GetDownloadUserAgent()
    {
        var userSettings = _userSettings.GetSettings();
        return !string.IsNullOrWhiteSpace(userSettings.DownloadUserAgent)
            ? userSettings.DownloadUserAgent
            : _appConfig.GetDefaultUserAgent();
    }

    /// <inheritdoc />
    /// <summary>
    /// Gets the download buffer size, falling back to the app default if the user setting is invalid.
    /// </summary>
    public int GetDownloadBufferSize()
    {
        var userSettings = _userSettings.GetSettings();
        return userSettings.DownloadBufferSize > 0
            ? userSettings.DownloadBufferSize
            : _appConfig.GetDefaultDownloadBufferSize();
    }

    /// <inheritdoc />
    /// <summary>
    /// Gets the default workspace strategy, falling back to the app default if the user setting is invalid.
    /// </summary>
    public WorkspaceStrategy GetDefaultWorkspaceStrategy()
    {
        var userSettings = _userSettings.GetSettings();
        return Enum.IsDefined(typeof(WorkspaceStrategy), userSettings.DefaultWorkspaceStrategy)
            ? userSettings.DefaultWorkspaceStrategy
            : _appConfig.GetDefaultWorkspaceStrategy();
    }

    /// <inheritdoc />
    public bool GetAutoCheckForUpdatesOnStartup() => _userSettings.GetSettings().AutoCheckForUpdatesOnStartup;

    /// <inheritdoc />
    public bool GetEnableDetailedLogging() => _userSettings.GetSettings().EnableDetailedLogging;
}
