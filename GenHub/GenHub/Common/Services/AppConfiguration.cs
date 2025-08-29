using System;
using System.IO;
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
                : Path.Combine(GetConfiguredDataPath(), "Workspace");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to get configured workspace path, using default");
            return Path.Combine(GetConfiguredDataPath(), "Workspace");
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
                : Path.Combine(GetConfiguredDataPath(), "Cache");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to get configured cache directory, using default");
            return Path.Combine(GetConfiguredDataPath(), "Cache");
        }
    }

    /// <summary>
    /// Gets the default download timeout in seconds.
    /// </summary>
    /// <returns>The default download timeout in seconds.</returns>
    public int GetDefaultDownloadTimeoutSeconds() =>
        int.TryParse(_configuration?["GenHub:Downloads:DefaultTimeoutSeconds"], out var result) ? result : 600;

    /// <summary>
    /// Gets the default user agent string for downloads.
    /// </summary>
    /// <returns>The default user agent string.</returns>
    public string GetDefaultUserAgent() =>
        _configuration?["GenHub:Downloads:DefaultUserAgent"] ?? "GenHub/1.0";

    /// <summary>
    /// Gets the default log level for the application.
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
    /// Gets the default maximum number of concurrent downloads.
    /// </summary>
    /// <returns>The default maximum number of concurrent downloads.</returns>
    public int GetDefaultMaxConcurrentDownloads() =>
        int.TryParse(_configuration?["GenHub:Downloads:DefaultMaxConcurrent"], out var result) ? result : 3;

    /// <summary>
    /// Gets the default download buffer size in bytes.
    /// </summary>
    /// <returns>The default download buffer size in bytes.</returns>
    public int GetDefaultDownloadBufferSize() =>
        int.TryParse(_configuration?["GenHub:Downloads:DefaultBufferSize"], out var result) ? result : 81920;

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
    /// Gets the default UI theme for GenHub.
    /// </summary>
    /// <returns>The default UI theme as a string.</returns>
    public string GetDefaultTheme() =>
        _configuration?["GenHub:UI:DefaultTheme"] ?? "Dark";

    /// <summary>
    /// Gets the default window width for GenHub.
    /// </summary>
    /// <returns>The default window width in pixels.</returns>
    public double GetDefaultWindowWidth() =>
        double.TryParse(_configuration?["GenHub:UI:DefaultWindowWidth"], out var result) ? result : 1024.0;

    /// <summary>
    /// Gets the default window height for GenHub.
    /// </summary>
    /// <returns>The default window height in pixels.</returns>
    public double GetDefaultWindowHeight() =>
        double.TryParse(_configuration?["GenHub:UI:DefaultWindowHeight"], out var result) ? result : 768.0;

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
