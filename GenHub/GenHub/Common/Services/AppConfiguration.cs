using System;
using System.IO;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.Services;

/// <summary>
/// Provides access to application-level configuration (read-only, deployment-time settings).
/// </summary>
public class AppConfiguration(IConfiguration? configuration, ILogger<AppConfiguration>? logger) : IAppConfiguration
{
    private const string DefaultWorkspaceDirectoryName = "Workspace";
    private const string DefaultCacheDirectoryName = "Cache";

    // Default buffer size: 80 KB (81920 bytes) - reasonable size for network downloads
    // Balances memory usage with performance; not too small (avoids many small reads)
    // and not too large (prevents excessive memory consumption)
    private const int DefaultDownloadBufferSizeBytes = 81920;

    // Default timeout: 10 minutes (600 seconds) - reasonable timeout for large downloads
    private const int DefaultDownloadTimeoutSeconds = 600;

    // Default concurrent downloads: 3 - balances performance with server load
    private const int DefaultMaxConcurrentDownloads = 3;

    // Default UI theme
    private const string DefaultTheme = "Dark";

    // Default window dimensions (1024x768 provides good balance of space and compatibility)
    private const double DefaultWindowWidth = 1024.0;
    private const double DefaultWindowHeight = 768.0;

    private readonly IConfiguration? _configuration = configuration;
    private readonly ILogger<AppConfiguration>? _logger = logger;

    /// <summary>
    /// Gets the default workspace path for GenHub.
    /// </summary>
    /// <returns>The default workspace path as a string.</returns>
    public string GetDefaultWorkspacePath()
    {
        try
        {
            var configured = _configuration?["GenHub:Workspace:DefaultPath"];
            return !string.IsNullOrEmpty(configured)
                ? configured
                : Path.Combine(GetConfiguredDataPath(), DefaultWorkspaceDirectoryName);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to get configured workspace path, using default");
            return Path.Combine(GetConfiguredDataPath(), DefaultWorkspaceDirectoryName);
        }
    }

    /// <summary>
    /// Gets the default cache directory for GenHub.
    /// </summary>
    /// <returns>The default cache directory as a string.</returns>
    public string GetDefaultCacheDirectory()
    {
        try
        {
            var configured = _configuration?["GenHub:Cache:DefaultPath"];
            return !string.IsNullOrEmpty(configured)
                ? configured
                : Path.Combine(GetConfiguredDataPath(), DefaultCacheDirectoryName);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to get configured cache directory, using default");
            return Path.Combine(GetConfiguredDataPath(), DefaultCacheDirectoryName);
        }
    }

    /// <summary>
    /// Gets the configured default download timeout in seconds, or defaults to 10 minutes (600 seconds).
    /// </summary>
    /// <returns>The default download timeout in seconds.</returns>
    public int GetDefaultDownloadTimeoutSeconds() =>
        int.TryParse(_configuration?["GenHub:Downloads:DefaultTimeoutSeconds"], out var result) ? result : DefaultDownloadTimeoutSeconds;

    /// <summary>
    /// Gets the configured default user agent string for downloads, or defaults to "GenHub/1.0".
    /// </summary>
    /// <returns>The default user agent string.</returns>
    public string GetDefaultUserAgent() =>
        _configuration?["GenHub:Downloads:DefaultUserAgent"] ?? AppConstants.DefaultUserAgent;

    /// <summary>
    /// Gets the configured default log level for the application, or defaults to Information.
    /// </summary>
    /// <returns>The default <see cref="LogLevel"/>.</returns>
    public LogLevel GetDefaultLogLevel()
    {
        var configured = _configuration?["Logging:LogLevel:Default"];
        return !string.IsNullOrEmpty(configured) && Enum.TryParse(configured, out LogLevel level)
            ? level
            : LogLevel.Information;
    }

    /// <summary>
    /// Gets the configured default maximum number of concurrent downloads, or defaults to 3.
    /// </summary>
    /// <returns>The default maximum number of concurrent downloads.</returns>
    public int GetDefaultMaxConcurrentDownloads() =>
        int.TryParse(_configuration?["GenHub:Downloads:DefaultMaxConcurrent"], out var result) ? result : DefaultMaxConcurrentDownloads;

    /// <summary>
    /// Gets the configured default download buffer size in bytes, or defaults to 80 KB (81920 bytes).
    /// This size balances memory usage with network performance for download operations.
    /// </summary>
    /// <returns>The default download buffer size in bytes.</returns>
    public int GetDefaultDownloadBufferSize() =>
        int.TryParse(_configuration?["GenHub:Downloads:DefaultBufferSize"], out var result) ? result : DefaultDownloadBufferSizeBytes;

    /// <summary>
    /// Gets the default workspace strategy for GenHub.
    /// </summary>
    /// <returns>The default <see cref="WorkspaceStrategy"/>.</returns>
    public WorkspaceStrategy GetDefaultWorkspaceStrategy()
    {
        var configured = _configuration?["GenHub:Workspace:DefaultStrategy"];
        return !string.IsNullOrEmpty(configured) && Enum.TryParse(configured, out WorkspaceStrategy strategy)
            ? strategy
            : WorkspaceStrategy.HybridCopySymlink;
    }

    /// <summary>
    /// Gets the configured default UI theme for GenHub, or defaults to "Dark".
    /// </summary>
    /// <returns>The default UI theme as a string.</returns>
    public string GetDefaultTheme()
    {
        var configured = _configuration?["GenHub:UI:DefaultTheme"];
        if (!string.IsNullOrEmpty(configured))
        {
            // Validate that the configured theme is valid (only "Dark" and "Light" are supported)
            var normalizedTheme = configured.Trim();
            if (string.Equals(normalizedTheme, "Dark", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedTheme, "Light", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedTheme;
            }
            else
            {
                _logger?.LogWarning("Invalid theme '{Theme}' configured, falling back to default", configured);
            }
        }

        return DefaultTheme;
    }

    /// <summary>
    /// Gets the configured default window width for GenHub, or defaults to 1024 pixels.
    /// </summary>
    /// <returns>The default window width in pixels.</returns>
    public double GetDefaultWindowWidth() =>
        double.TryParse(_configuration?["GenHub:UI:DefaultWindowWidth"], out var result) ? result : DefaultWindowWidth;

    /// <summary>
    /// Gets the configured default window height for GenHub, or defaults to 768 pixels.
    /// </summary>
    /// <returns>The default window height in pixels.</returns>
    public double GetDefaultWindowHeight() =>
        double.TryParse(_configuration?["GenHub:UI:DefaultWindowHeight"], out var result) ? result : DefaultWindowHeight;

    /// <summary>
    /// Gets the minimum allowed concurrent downloads value.
    /// </summary>
    /// <returns>The minimum allowed number of concurrent downloads.</returns>
    public int GetMinConcurrentDownloads() =>
        int.TryParse(_configuration?["GenHub:Downloads:Policy:MinConcurrent"], out var result) ? result : 1;

    /// <summary>
    /// Gets the maximum allowed concurrent downloads value.
    /// </summary>
    /// <returns>The maximum allowed number of concurrent downloads.</returns>
    public int GetMaxConcurrentDownloads() =>
        int.TryParse(_configuration?["GenHub:Downloads:Policy:MaxConcurrent"], out var result) ? result : 10;

    /// <summary>
    /// Gets the minimum allowed download timeout in seconds.
    /// </summary>
    /// <returns>The minimum allowed download timeout in seconds.</returns>
    public int GetMinDownloadTimeoutSeconds() =>
        int.TryParse(_configuration?["GenHub:Downloads:Policy:MinTimeoutSeconds"], out var result) ? result : 10;

    /// <summary>
    /// Gets the maximum allowed download timeout in seconds.
    /// </summary>
    /// <returns>The maximum allowed download timeout in seconds.</returns>
    public int GetMaxDownloadTimeoutSeconds() =>
        int.TryParse(_configuration?["GenHub:Downloads:Policy:MaxTimeoutSeconds"], out var result) ? result : 3600;

    /// <summary>
    /// Gets the minimum allowed download buffer size in bytes.
    /// </summary>
    /// <returns>The minimum allowed download buffer size in bytes.</returns>
    public int GetMinDownloadBufferSizeBytes() =>
        int.TryParse(_configuration?["GenHub:Downloads:Policy:MinBufferSizeBytes"], out var result) ? result : 4 * 1024;

    /// <summary>
    /// Gets the maximum allowed download buffer size in bytes.
    /// </summary>
    /// <returns>The maximum allowed download buffer size in bytes.</returns>
    public int GetMaxDownloadBufferSizeBytes() =>
        int.TryParse(_configuration?["GenHub:Downloads:Policy:MaxBufferSizeBytes"], out var result) ? result : 1024 * 1024;

    /// <summary>
    /// Gets the application data path for GenHub.
    /// </summary>
    /// <returns>The application data path as a string.</returns>
    public string GetConfiguredDataPath()
    {
        if (_configuration == null)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub");
        }

        var configured = _configuration["GenHub:AppDataPath"];
        return !string.IsNullOrEmpty(configured)
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub");
    }
}
