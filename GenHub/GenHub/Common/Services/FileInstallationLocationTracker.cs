using System;
using System.IO;
using System.Security;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Storage;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.Services;

/// <summary>
/// Tracks installation locations across platforms using a user profile marker file.
/// </summary>
/// <param name="logger">Optional logger for diagnostics.</param>
public class FileInstallationLocationTracker(ILogger<FileInstallationLocationTracker>? logger = null) : IInstallationLocationTracker
{
    private readonly ILogger<FileInstallationLocationTracker>? _logger = logger;

    /// <summary>
    /// Records the current installation directory in a user profile marker file when running from a custom install root.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public static void RecordInstallLocationStatic(ILogger? logger = null)
    {
        try
        {
            if (StorageMigrationService.IsCustomInstallRoot())
            {
                var customRoot = StorageMigrationService.GetSourceRootDirectory();
                var filePath = GetLocationFilePath();
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(filePath, customRoot);
                logger?.LogInformation("Recorded custom installation root in file: {CustomRoot}", customRoot);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            logger?.LogWarning(ex, "Failed to record custom installation location to file.");
        }
    }

    /// <summary>
    /// Retrieves the registered custom installation directory from the user profile marker file.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <returns>The registered custom installation path if found; otherwise, <see langword="null"/>.</returns>
    public static string? GetRegisteredCustomInstallPathStatic(ILogger? logger = null)
    {
        try
        {
            var filePath = GetLocationFilePath();
            if (File.Exists(filePath))
            {
                var path = File.ReadAllText(filePath).Trim();
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && StorageMigrationService.IsVelopackRoot(path))
                {
                    return path;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            logger?.LogWarning(ex, "Failed to read custom installation location from file.");
        }

        return null;
    }

    /// <summary>
    /// Clears the registered custom installation path from the user profile marker file.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public static void ClearCustomInstallPathStatic(ILogger? logger = null)
    {
        try
        {
            var filePath = GetLocationFilePath();
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                logger?.LogInformation("Cleared custom installation root file: {FilePath}", filePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            logger?.LogWarning(ex, "Failed to clear custom installation location file.");
        }
    }

    /// <summary>
    /// Gets the absolute path of the custom install location tracking file in the user profile directory.
    /// </summary>
    /// <returns>The path to the tracking file.</returns>
    public static string GetLocationFilePath()
    {
        var profileDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profileDir, StorageMigrationConstants.GenHubConfigDirectoryName, StorageMigrationConstants.CustomInstallPathFileName);
    }

    /// <inheritdoc />
    public virtual void RecordInstallLocation() => RecordInstallLocationStatic(_logger);

    /// <inheritdoc />
    public virtual string? GetRegisteredCustomInstallPath() => GetRegisteredCustomInstallPathStatic(_logger);

    /// <inheritdoc />
    public virtual void ClearCustomInstallPath() => ClearCustomInstallPathStatic(_logger);
}
