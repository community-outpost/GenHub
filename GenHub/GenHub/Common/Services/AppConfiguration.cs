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
    private readonly IConfiguration? _configuration = configuration;
    private readonly ILogger<AppConfiguration>? _logger = logger;

    /// <summary>
    /// Gets the root application data path for GenHub.
    /// </summary>
    /// <returns>The root application data path as a string.</returns>
    public string GetAppDataPath()
    {
        try
        {
            var configured = _configuration?.GetValue<string>(ConfigurationKeys.AppDataPath);
            return !string.IsNullOrEmpty(configured)
                ? configured
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to get configured AppDataPath, using default");
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
            var configured = _configuration?[ConfigurationKeys.WorkspaceDefaultPath];
            return !string.IsNullOrEmpty(configured)
                ? configured
                : Path.Combine(GetConfiguredDataPath(), DirectoryNames.Data);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to get configured workspace path, using default");
            return Path.Combine(GetConfiguredDataPath(), DirectoryNames.Data);
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
            var configured = _configuration?[ConfigurationKeys.CacheDefaultPath];
            return !string.IsNullOrEmpty(configured)
                ? configured
                : Path.Combine(GetConfiguredDataPath(), DirectoryNames.Cache);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to get configured cache directory, using default");
            return Path.Combine(GetConfiguredDataPath(), DirectoryNames.Cache);
        }
    }

    /// <summary>
    /// Gets the configured default download timeout in seconds, or defaults to 10 minutes (600 seconds).
    /// </summary>
    /// <returns>The default download timeout in seconds.</returns>
    public int GetDefaultDownloadTimeoutSeconds() =>
        int.TryParse(_configuration?[ConfigurationKeys.DownloadsDefaultTimeoutSeconds], out var result) ? result : DownloadDefaults.TimeoutSeconds;

    /// <summary>
    /// Gets the configured default user agent string for downloads, or defaults to "GenHub/1.0".
    /// </summary>
    /// <returns>The default user agent string.</returns>
    public string GetDefaultUserAgent() =>
        _configuration?[ConfigurationKeys.DownloadsDefaultUserAgent] ?? ApiConstants.DefaultUserAgent;

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
        int.TryParse(_configuration?[ConfigurationKeys.DownloadsDefaultMaxConcurrent], out var result) ? result : DownloadDefaults.MaxConcurrentDownloads;

    /// <summary>
    /// Gets the configured default download buffer size in bytes, or defaults to 80 KB (81920 bytes).
    /// This size balances memory usage with network performance for download operations.
    /// </summary>
    /// <returns>The default download buffer size in bytes.</returns>
    public int GetDefaultDownloadBufferSize() =>
        int.TryParse(_configuration?[ConfigurationKeys.DownloadsDefaultBufferSize], out var result) ? result : DownloadDefaults.BufferSizeBytes;

    /// <summary>
    /// Gets the default workspace strategy for GenHub.
    /// </summary>
    /// <returns>The default <see cref="WorkspaceStrategy"/>.</returns>
    public WorkspaceStrategy GetDefaultWorkspaceStrategy()
    {
        var configured = _configuration?[ConfigurationKeys.WorkspaceDefaultStrategy];
        return !string.IsNullOrEmpty(configured) && Enum.TryParse(configured, out WorkspaceStrategy strategy)
            ? strategy
            : WorkspaceConstants.DefaultWorkspaceStrategy;
    }

    /// <summary>
    /// Gets the configured default UI theme for GenHub, or defaults to "Dark".
    /// </summary>
    /// <returns>The default UI theme as a string.</returns>
    public string GetDefaultTheme()
    {
        var configured = _configuration?[ConfigurationKeys.UiDefaultTheme];
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

        return AppConstants.DefaultThemeName; // Default theme
    }

    /// <summary>
    /// Gets the configured default window width for GenHub, or defaults to 1200 pixels.
    /// </summary>
    /// <returns>The default window width in pixels.</returns>
    public double GetDefaultWindowWidth() =>
        double.TryParse(_configuration?[ConfigurationKeys.UiDefaultWindowWidth], out var result) ? result : UiConstants.DefaultWindowWidth;

    /// <summary>
    /// Gets the configured default window height for GenHub, or defaults to 800 pixels.
    /// </summary>
    /// <returns>The default window height in pixels.</returns>
    public double GetDefaultWindowHeight() =>
        double.TryParse(_configuration?[ConfigurationKeys.UiDefaultWindowHeight], out var result) ? result : UiConstants.DefaultWindowHeight;

    /// <summary>
    /// Gets the minimum allowed concurrent downloads value.
    /// </summary>
    /// <returns>The minimum allowed number of concurrent downloads.</returns>
    public int GetMinConcurrentDownloads() =>
        int.TryParse(_configuration?[ConfigurationKeys.DownloadsPolicyMinConcurrent], out var result) ? result : ValidationLimits.MinConcurrentDownloads;

    /// <summary>
    /// Gets the maximum allowed concurrent downloads value.
    /// </summary>
    /// <returns>The maximum allowed number of concurrent downloads.</returns>
    public int GetMaxConcurrentDownloads() =>
        int.TryParse(_configuration?[ConfigurationKeys.DownloadsPolicyMaxConcurrent], out var result) ? result : ValidationLimits.MaxConcurrentDownloads;

    /// <summary>
    /// Gets the minimum allowed download timeout in seconds.
    /// </summary>
    /// <returns>The minimum allowed download timeout in seconds.</returns>
    public int GetMinDownloadTimeoutSeconds() =>
        int.TryParse(_configuration?[ConfigurationKeys.DownloadsPolicyMinTimeoutSeconds], out var result) ? result : ValidationLimits.MinDownloadTimeoutSeconds;

    /// <summary>
    /// Gets the maximum allowed download timeout in seconds.
    /// </summary>
    /// <returns>The maximum allowed download timeout in seconds.</returns>
    public int GetMaxDownloadTimeoutSeconds() =>
        int.TryParse(_configuration?[ConfigurationKeys.DownloadsPolicyMaxTimeoutSeconds], out var result) ? result : ValidationLimits.MaxDownloadTimeoutSeconds;

    /// <summary>
    /// Gets the minimum allowed download buffer size in bytes.
    /// </summary>
    /// <returns>The minimum allowed download buffer size in bytes.</returns>
    public int GetMinDownloadBufferSizeBytes() =>
        int.TryParse(_configuration?[ConfigurationKeys.DownloadsPolicyMinBufferSizeBytes], out var result) ? result : ValidationLimits.MinDownloadBufferSizeBytes;

    /// <summary>
    /// Gets the maximum allowed download buffer size in bytes.
    /// </summary>
    /// <returns>The maximum allowed download buffer size in bytes.</returns>
    public int GetMaxDownloadBufferSizeBytes() =>
        int.TryParse(_configuration?[ConfigurationKeys.DownloadsPolicyMaxBufferSizeBytes], out var result) ? result : ValidationLimits.MaxDownloadBufferSizeBytes;

    /// <summary>
    /// Gets the application data path for GenHub.
    /// </summary>
    /// <returns>The application data path as a string.</returns>
    public string GetConfiguredDataPath()
    {
        if (_configuration == null)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppConstants.AppName);
        }

        var configured = _configuration[ConfigurationKeys.AppDataPath];
        return !string.IsNullOrEmpty(configured)
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppConstants.AppName);
    }
}