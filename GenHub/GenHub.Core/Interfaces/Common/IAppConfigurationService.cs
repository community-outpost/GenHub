using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Provides access to application-level configuration (read-only, deployment-time settings).
/// </summary>
public interface IAppConfigurationService
{
    /// <summary>
    /// Gets the default workspace path for GenHub.
    /// </summary>
    /// <returns>The default workspace path as a string.</returns>
    string GetDefaultWorkspacePath();

    /// <summary>
    /// Gets the default cache directory for GenHub.
    /// </summary>
    /// <returns>The default cache directory as a string.</returns>
    string GetDefaultCacheDirectory();

    /// <summary>
    /// Gets the default download timeout in seconds.
    /// </summary>
    /// <returns>The default download timeout in seconds.</returns>
    int GetDefaultDownloadTimeoutSeconds();

    /// <summary>
    /// Gets the default user agent string for downloads.
    /// </summary>
    /// <returns>The default user agent string.</returns>
    string GetDefaultUserAgent();

    /// <summary>
    /// Gets the default log level for the application.
    /// </summary>
    /// <returns>The default <see cref="LogLevel"/>.</returns>
    LogLevel GetDefaultLogLevel();

    /// <summary>
    /// Gets the default maximum number of concurrent downloads.
    /// </summary>
    /// <returns>The default maximum number of concurrent downloads.</returns>
    int GetDefaultMaxConcurrentDownloads();

    /// <summary>
    /// Gets the default download buffer size in bytes.
    /// </summary>
    /// <returns>The default download buffer size in bytes.</returns>
    int GetDefaultDownloadBufferSize();

    /// <summary>
    /// Gets the default workspace strategy for GenHub.
    /// </summary>
    /// <returns>The default <see cref="WorkspaceStrategy"/>.</returns>
    WorkspaceStrategy GetDefaultWorkspaceStrategy();
}
