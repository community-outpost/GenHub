using System;
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
        if (!string.IsNullOrWhiteSpace(userSettings.WorkspacePath) &&
            Directory.Exists(Path.GetDirectoryName(userSettings.WorkspacePath)))
        {
            return userSettings.WorkspacePath;
        }

        return _appConfig.GetDefaultWorkspacePath();
    }

    /// <inheritdoc />
    public string GetCacheDirectory() => _appConfig.GetDefaultCacheDirectory();

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
    public int GetDownloadBufferSize() => _userSettings.GetSettings().DownloadBufferSize;

    /// <inheritdoc />
    public WorkspaceStrategy GetDefaultWorkspaceStrategy() => _userSettings.GetSettings().DefaultWorkspaceStrategy;

    /// <inheritdoc />
    public bool GetAutoCheckForUpdatesOnStartup() => _userSettings.GetSettings().AutoCheckForUpdatesOnStartup;

    /// <inheritdoc />
    public bool GetEnableDetailedLogging() => _userSettings.GetSettings().EnableDetailedLogging;
}
