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
public class AppConfigurationService : IAppConfigurationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AppConfigurationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppConfigurationService"/> class.
    /// </summary>
    /// <param name="configuration">The configuration provider.</param>
    /// <param name="logger">The logger instance.</param>
    public AppConfigurationService(IConfiguration configuration, ILogger<AppConfigurationService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the root application data path for GenHub.
    /// </summary>
    /// <returns>The root application data path as a string.</returns>
    public string GetAppDataPath()
    {
        try
        {
            var configured = _configuration?.GetValue<string>("GenHub:AppDataPath");
            return !string.IsNullOrEmpty(configured)
                ? configured
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get configured AppDataPath, using default");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub");
        }
    }

    /// <summary>
    /// Gets the default workspace path for GenHub.
    /// </summary>
    /// <returns>The default workspace path as a string.</returns>
    public string GetDefaultWorkspacePath()
    {
        try
        {
            var configured = _configuration?.GetValue<string>("GenHub:Workspace:DefaultPath");
            return !string.IsNullOrEmpty(configured)
                ? configured
                : Path.Combine(GetAppDataPath(), "Workspace");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get configured workspace path, using default");
            return Path.Combine(GetAppDataPath(), "Workspace");
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
            var configured = _configuration?.GetValue<string>("GenHub:Cache:DefaultPath");
            return !string.IsNullOrEmpty(configured)
                ? configured
                : Path.Combine(GetAppDataPath(), "Cache");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get configured cache directory, using default");
            return Path.Combine(GetAppDataPath(), "Cache");
        }
    }

    /// <summary>
    /// Gets the default download timeout in seconds.
    /// </summary>
    /// <returns>The default download timeout in seconds.</returns>
    public int GetDefaultDownloadTimeoutSeconds() => _configuration?.GetValue("GenHub:Downloads:DefaultTimeoutSeconds", 600) ?? 600;

    /// <summary>
    /// Gets the default user agent string for downloads.
    /// </summary>
    /// <returns>The default user agent string.</returns>
    public string GetDefaultUserAgent() => _configuration?.GetValue("GenHub:Downloads:DefaultUserAgent", "GenHub/1.0") ?? "GenHub/1.0";

    /// <summary>
    /// Gets the default log level for the application.
    /// </summary>
    /// <returns>The default <see cref="LogLevel"/>.</returns>
    public LogLevel GetDefaultLogLevel() => _configuration?.GetValue("Logging:LogLevel:Default", LogLevel.Information) ?? LogLevel.Information;

    /// <summary>
    /// Gets the default maximum number of concurrent downloads.
    /// </summary>
    /// <returns>The default maximum number of concurrent downloads.</returns>
    public int GetDefaultMaxConcurrentDownloads() => _configuration?.GetValue("GenHub:Downloads:DefaultMaxConcurrent", 3) ?? 3;

    /// <summary>
    /// Gets the default download buffer size in bytes.
    /// </summary>
    /// <returns>The default download buffer size in bytes.</returns>
    public int GetDefaultDownloadBufferSize() => _configuration?.GetValue("GenHub:Downloads:DefaultBufferSize", 81920) ?? 81920;

    /// <summary>
    /// Gets the default workspace strategy for GenHub.
    /// </summary>
    /// <returns>The default <see cref="WorkspaceStrategy"/>.</returns>
    public WorkspaceStrategy GetDefaultWorkspaceStrategy() => _configuration?.GetValue("GenHub:Workspace:DefaultStrategy", WorkspaceStrategy.HybridCopySymlink) ?? WorkspaceStrategy.HybridCopySymlink;
}
