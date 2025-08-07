using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Provides access to application-level configuration (read-only, deployment-time settings).
/// </summary>
public interface IAppConfiguration
{
    /// <summary>Gets the default workspace path for GenHub.</summary>
    /// <returns>The default workspace path.</returns>
    string GetDefaultWorkspacePath();

    /// <summary>Gets the default cache directory for GenHub.</summary>
    /// <returns>The default cache directory path.</returns>
    string GetDefaultCacheDirectory();

    /// <summary>Gets the default download timeout in seconds.</summary>
    /// <returns>The default download timeout in seconds.</returns>
    int GetDefaultDownloadTimeoutSeconds();

    /// <summary>Gets the default user agent string for downloads.</summary>
    /// <returns>The default User-Agent string.</returns>
    string GetDefaultUserAgent();

    /// <summary>Gets the default log level for the application.</summary>
    /// <returns>The default log level.</returns>
    LogLevel GetDefaultLogLevel();

    /// <summary>Gets the default maximum number of concurrent downloads.</summary>
    /// <returns>The default max concurrent downloads.</returns>
    int GetDefaultMaxConcurrentDownloads();

    /// <summary>Gets the default download buffer size in bytes.</summary>
    /// <returns>The default buffer size in bytes.</returns>
    int GetDefaultDownloadBufferSize();

    /// <summary>Gets the default workspace strategy for GenHub.</summary>
    /// <returns>The default workspace strategy.</returns>
    WorkspaceStrategy GetDefaultWorkspaceStrategy();

    /// <summary>Gets the default UI theme.</summary>
    /// <returns>The default theme name.</returns>
    string GetDefaultTheme();

    /// <summary>Gets the default window width.</summary>
    /// <returns>The default window width in pixels.</returns>
    double GetDefaultWindowWidth();

    /// <summary>Gets the default window height.</summary>
    /// <returns>The default window height in pixels.</returns>
    double GetDefaultWindowHeight();

    // Policy bounds

    /// <summary>Gets the minimum allowed concurrent downloads value.</summary>
    /// <returns>The minimum concurrent downloads.</returns>
    int GetMinConcurrentDownloads();

    /// <summary>Gets the maximum allowed concurrent downloads value.</summary>
    /// <returns>The maximum concurrent downloads.</returns>
    int GetMaxConcurrentDownloads();

    /// <summary>Gets the minimum allowed download timeout in seconds.</summary>
    /// <returns>The minimum timeout in seconds.</returns>
    int GetMinDownloadTimeoutSeconds();

    /// <summary>Gets the maximum allowed download timeout in seconds.</summary>
    /// <returns>The maximum timeout in seconds.</returns>
    int GetMaxDownloadTimeoutSeconds();

    /// <summary>Gets the minimum allowed download buffer size in bytes.</summary>
    /// <returns>The minimum buffer size in bytes.</returns>
    int GetMinDownloadBufferSizeBytes();

    /// <summary>Gets the maximum allowed download buffer size in bytes.</summary>
    /// <returns>The maximum buffer size in bytes.</returns>
    int GetMaxDownloadBufferSizeBytes();
}
