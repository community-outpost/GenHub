using System.Collections.Generic;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Storage;

namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Unified configuration service that intelligently combines app config and user settings to provide effective values.
/// This is the single service that other components should depend on for all configuration needs.
/// </summary>
public interface IConfigurationProviderService
{
    /// <summary>
    /// Gets the effective workspace path, falling back to defaults if necessary.
    /// </summary>
    /// <returns>The workspace path as a string.</returns>
    string GetWorkspacePath();

    /// <summary>
    /// Gets the effective cache path.
    /// </summary>
    /// <returns>The cache path as a string.</returns>
    string GetCachePath();

    /// <summary>
    /// Gets the effective maximum number of concurrent downloads.
    /// </summary>
    /// <returns>The maximum number of concurrent downloads.</returns>
    int GetMaxConcurrentDownloads();

    /// <summary>
    /// Gets whether background downloads are allowed.
    /// </summary>
    /// <returns>True if background downloads are allowed; otherwise, false.</returns>
    bool GetAllowBackgroundDownloads();

    /// <summary>
    /// Gets the effective download timeout in seconds.
    /// </summary>
    /// <returns>The download timeout in seconds.</returns>
    int GetDownloadTimeoutSeconds();

    /// <summary>
    /// Gets the effective user agent for downloads.
    /// </summary>
    /// <returns>The user agent string.</returns>
    string GetDownloadUserAgent();

    /// <summary>
    /// Gets the effective download buffer size in bytes.
    /// </summary>
    /// <returns>The download buffer size in bytes.</returns>
    int GetDownloadBufferSize();

    /// <summary>
    /// Gets the effective workspace strategy.
    /// </summary>
    /// <returns>The effective workspace strategy.</returns>
    WorkspaceStrategy GetDefaultWorkspaceStrategy();

    /// <summary>
    /// Gets the effective UI theme.
    /// </summary>
    /// <returns>The effective UI theme.</returns>
    string GetTheme();

    /// <summary>
    /// Gets the effective window width.
    /// </summary>
    /// <returns>The effective window width.</returns>
    double GetWindowWidth();

    /// <summary>
    /// Gets the effective window height.
    /// </summary>
    /// <returns>The effective window height.</returns>
    double GetWindowHeight();

    /// <summary>
    /// Gets whether the window is maximized.
    /// </summary>
    /// <returns>True if the window is maximized, otherwise false.</returns>
    bool GetIsWindowMaximized();

    /// <summary>
    /// Gets the last selected navigation tab.
    /// </summary>
    /// <returns>The last selected navigation tab.</returns>
    NavigationTab GetLastSelectedTab();

    /// <summary>
    /// Gets whether automatic update checks on startup are enabled.
    /// </summary>
    /// <returns>True if automatic update checks on startup are enabled, otherwise false.</returns>
    bool GetAutoCheckForUpdatesOnStartup();

    /// <summary>
    /// Gets whether periodic update checks are enabled.
    /// </summary>
    /// <returns>True if periodic update checks are enabled, otherwise false.</returns>
    bool GetAutoCheckForUpdatesPeriodically();

    /// <summary>
    /// Gets the interval for periodic update checks in minutes.
    /// </summary>
    /// <returns>The interval in minutes.</returns>
    int GetPeriodicUpdateCheckIntervalMinutes();

    /// <summary>
    /// Gets whether detailed logging is enabled.
    /// </summary>
    /// <returns>True if detailed logging is enabled, otherwise false.</returns>
    bool GetEnableDetailedLogging();

    /// <summary>
    /// Gets the list of content directories.
    /// </summary>
    /// <returns>The list of content directories.</returns>
    List<string> GetContentDirectories();

    /// <summary>
    /// Gets the list of GitHub discovery repositories.
    /// </summary>
    /// <returns>The list of repository names.</returns>
    List<string> GetGitHubDiscoveryRepositories();

    /// <summary>
    /// Gets all effective user settings in a single object.
    /// </summary>
    /// <returns>The effective user settings.</returns>
    UserSettings GetEffectiveSettings();

    /// <summary>
    /// Gets the effective application data path.
    /// </summary>
    /// <returns>The effective application data path.</returns>
    string GetApplicationDataPath();

    /// <summary>
    /// Gets the root application data path across all components.
    /// </summary>
    /// <returns>The root application data path.</returns>
    string GetRootAppDataPath();

    /// <summary>
    /// Gets the directory path where profiles are stored.
    /// </summary>
    /// <returns>The profiles directory path.</returns>
    string GetProfilesPath();

    /// <summary>
    /// Gets the directory path where manifests are stored.
    /// </summary>
    /// <returns>The manifests directory path.</returns>
    string GetManifestsPath();

    /// <summary>
    /// Gets the CAS configuration settings.
    /// </summary>
    /// <returns>The CAS configuration.</returns>
    CasConfiguration GetCasConfiguration();

    /// <summary>
    /// Gets the directory path where application logs are stored.
    /// </summary>
    /// <returns>The logs directory path.</returns>
    string GetLogsPath();

    /// <summary>
    /// Gets the CSV catalog configuration.
    /// </summary>
    /// <returns>The CSV catalog configuration.</returns>
    CsvCatalogConfiguration GetCsvCatalogConfiguration();
}
