using System;
using System.Reflection;
using GenHub.Core.Interfaces.AppUpdate;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.AppUpdate.Services;

/// <summary>
/// Service for managing application version information.
/// </summary>
public class AppVersionService(ILogger<AppVersionService> logger) : IAppVersionService
{
    private readonly ILogger<AppVersionService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Gets the current application version from the entry assembly.
    /// </summary>
    /// <returns>The current version string.</returns>
    public string GetCurrentVersion()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly();
            var version = assembly?.GetName().Version;

            return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current application version");
            return "0.0.0";
        }
    }

    /// <summary>
    /// Gets the current application version as a Version object from the entry assembly.
    /// </summary>
    /// <returns>The current version.</returns>
    public Version GetCurrentVersionObject()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly();
            return assembly?.GetName().Version ?? new Version(0, 0, 0, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current application version object");
            return new Version(0, 0, 0, 0);
        }
    }
}