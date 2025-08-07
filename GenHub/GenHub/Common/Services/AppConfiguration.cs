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
public class AppConfiguration : IAppConfiguration
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AppConfiguration> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppConfiguration"/> class.
    /// </summary>
    /// <param name="configuration">The configuration provider.</param>
    /// <param name="logger">The logger instance.</param>
    public AppConfiguration(IConfiguration configuration, ILogger<AppConfiguration> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the default workspace path for GenHub.
    /// </summary>
    /// <returns>The default workspace path as a string.</returns>
    public string GetDefaultWorkspacePath()
    {
        var configured = _configuration.GetValue<string>("GenHub:Workspace:DefaultPath");
        return !string.IsNullOrEmpty(configured)
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub", "Workspace");
    }

    /// <summary>
    /// Gets the default cache directory for GenHub.
    /// </summary>
    /// <returns>The default cache directory as a string.</returns>
    public string GetDefaultCacheDirectory()
    {
        var configured = _configuration.GetValue<string>("GenHub:Cache:DefaultPath");
        return !string.IsNullOrEmpty(configured)
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub", "Cache");
    }

    /// <summary>
    /// Gets the default download timeout in seconds.
    /// </summary>
    /// <returns>The default download timeout in seconds.</returns>
    public int GetDefaultDownloadTimeoutSeconds() => _configuration.GetValue("GenHub:Downloads:DefaultTimeoutSeconds", 600);

    /// <summary>
    /// Gets the default user agent string for downloads.
    /// </summary>
    /// <returns>The default user agent string.</returns>
    public string GetDefaultUserAgent() => _configuration.GetValue("GenHub:Downloads:DefaultUserAgent", "GenHub/1.0");

    /// <summary>
    /// Gets the default log level for the application.
    /// </summary>
    /// <returns>The default <see cref="LogLevel"/>.</returns>
    public LogLevel GetDefaultLogLevel() => _configuration.GetValue("Logging:LogLevel:Default", LogLevel.Information);

    /// <summary>
    /// Gets the default maximum number of concurrent downloads.
    /// </summary>
    /// <returns>The default maximum number of concurrent downloads.</returns>
    public int GetDefaultMaxConcurrentDownloads() => _configuration.GetValue("GenHub:Downloads:DefaultMaxConcurrent", 3);

    /// <summary>
    /// Gets the default download buffer size in bytes.
    /// </summary>
    /// <returns>The default download buffer size in bytes.</returns>
    public int GetDefaultDownloadBufferSize() => _configuration.GetValue("GenHub:Downloads:DefaultBufferSize", 81920);

    /// <summary>
    /// Gets the default workspace strategy for GenHub.
    /// </summary>
    /// <returns>The default <see cref="WorkspaceStrategy"/>.</returns>
    public WorkspaceStrategy GetDefaultWorkspaceStrategy() => _configuration.GetValue("GenHub:Workspace:DefaultStrategy", WorkspaceStrategy.HybridCopySymlink);

    /// <summary>
    /// Gets the default UI theme for GenHub.
    /// </summary>
    /// <returns>The default UI theme as a string.</returns>
    public string GetDefaultTheme() => _configuration.GetValue("GenHub:UI:DefaultTheme", "Dark").Trim();

    /// <summary>
    /// Gets the default window width for GenHub.
    /// </summary>
    /// <returns>The default window width in pixels.</returns>
    public double GetDefaultWindowWidth() => _configuration.GetValue("GenHub:UI:DefaultWindowWidth", 1024.0);

    /// <summary>
    /// Gets the default window height for GenHub.
    /// </summary>
    /// <returns>The default window height in pixels.</returns>
    public double GetDefaultWindowHeight() => _configuration.GetValue("GenHub:UI:DefaultWindowHeight", 768.0);

    /// <summary>
    /// Gets the minimum allowed concurrent downloads value.
    /// </summary>
    /// <returns>The minimum allowed number of concurrent downloads.</returns>
    public int GetMinConcurrentDownloads() => _configuration.GetValue("GenHub:Downloads:Policy:MinConcurrent", 1);

    /// <summary>
    /// Gets the maximum allowed concurrent downloads value.
    /// </summary>
    /// <returns>The maximum allowed number of concurrent downloads.</returns>
    public int GetMaxConcurrentDownloads() => _configuration.GetValue("GenHub:Downloads:Policy:MaxConcurrent", 10);

    /// <summary>
    /// Gets the minimum allowed download timeout in seconds.
    /// </summary>
    /// <returns>The minimum allowed download timeout in seconds.</returns>
    public int GetMinDownloadTimeoutSeconds() => _configuration.GetValue("GenHub:Downloads:Policy:MinTimeoutSeconds", 10);

    /// <summary>
    /// Gets the maximum allowed download timeout in seconds.
    /// </summary>
    /// <returns>The maximum allowed download timeout in seconds.</returns>
    public int GetMaxDownloadTimeoutSeconds() => _configuration.GetValue("GenHub:Downloads:Policy:MaxTimeoutSeconds", 3600);

    /// <summary>
    /// Gets the minimum allowed download buffer size in bytes.
    /// </summary>
    /// <returns>The minimum allowed download buffer size in bytes.</returns>
    public int GetMinDownloadBufferSizeBytes() => _configuration.GetValue("GenHub:Downloads:Policy:MinBufferSizeBytes", 4 * 1024);

    /// <summary>
    /// Gets the maximum allowed download buffer size in bytes.
    /// </summary>
    /// <returns>The maximum allowed download buffer size in bytes.</returns>
    public int GetMaxDownloadBufferSizeBytes() => _configuration.GetValue("GenHub:Downloads:Policy:MaxBufferSizeBytes", 1024 * 1024);
}
