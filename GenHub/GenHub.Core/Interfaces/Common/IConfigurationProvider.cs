using GenHub.Core.Models.Enums;

namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Unified configuration provider that combines app config and user settings.
/// </summary>
public interface IConfigurationProvider
{
    /// <summary>
    /// Gets the effective workspace path.
    /// </summary>
    /// <returns>The workspace path as a string.</returns>
    string GetWorkspacePath();

    /// <summary>
    /// Gets the effective cache directory.
    /// </summary>
    /// <returns>The cache directory as a string.</returns>
    string GetCacheDirectory();

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
    /// Gets the effective user agent string for downloads.
    /// </summary>
    /// <returns>The user agent string.</returns>
    string GetDownloadUserAgent();

    /// <summary>
    /// Gets the effective download buffer size in bytes.
    /// </summary>
    /// <returns>The download buffer size in bytes.</returns>
    int GetDownloadBufferSize();

    /// <summary>
    /// Gets the effective default workspace strategy.
    /// </summary>
    /// <returns>The <see cref="WorkspaceStrategy"/> value.</returns>
    WorkspaceStrategy GetDefaultWorkspaceStrategy();

    /// <summary>
    /// Gets whether to auto-check for updates on startup.
    /// </summary>
    /// <returns>True if auto-check for updates is enabled; otherwise, false.</returns>
    bool GetAutoCheckForUpdatesOnStartup();

    /// <summary>
    /// Gets whether detailed logging is enabled.
    /// </summary>
    /// <returns>True if detailed logging is enabled; otherwise, false.</returns>
    bool GetEnableDetailedLogging();
}
