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
    private const string RecordLocationFailureMessage = "Failed to record custom installation location to file.";
    private const string ReadLocationFailureMessage = "Failed to read custom installation location from file.";
    private const string ClearLocationFailureMessage = "Failed to clear custom installation location file.";

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
        catch (IOException ex)
        {
            logger?.LogWarning(ex, RecordLocationFailureMessage);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger?.LogWarning(ex, RecordLocationFailureMessage);
        }
        catch (SecurityException ex)
        {
            logger?.LogWarning(ex, RecordLocationFailureMessage);
        }
        catch (ArgumentException ex)
        {
            logger?.LogWarning(ex, RecordLocationFailureMessage);
        }
        catch (InvalidOperationException ex)
        {
            logger?.LogWarning(ex, RecordLocationFailureMessage);
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
        catch (IOException ex)
        {
            logger?.LogWarning(ex, ReadLocationFailureMessage);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger?.LogWarning(ex, ReadLocationFailureMessage);
        }
        catch (SecurityException ex)
        {
            logger?.LogWarning(ex, ReadLocationFailureMessage);
        }
        catch (ArgumentException ex)
        {
            logger?.LogWarning(ex, ReadLocationFailureMessage);
        }
        catch (InvalidOperationException ex)
        {
            logger?.LogWarning(ex, ReadLocationFailureMessage);
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
        catch (IOException ex)
        {
            logger?.LogWarning(ex, ClearLocationFailureMessage);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger?.LogWarning(ex, ClearLocationFailureMessage);
        }
        catch (SecurityException ex)
        {
            logger?.LogWarning(ex, ClearLocationFailureMessage);
        }
        catch (ArgumentException ex)
        {
            logger?.LogWarning(ex, ClearLocationFailureMessage);
        }
        catch (InvalidOperationException ex)
        {
            logger?.LogWarning(ex, ClearLocationFailureMessage);
        }
    }

    private static string? _locationFilePathOverride;

    /// <summary>
    /// Gets the absolute path of the custom install location tracking file in the user profile directory.
    /// </summary>
    /// <returns>The path to the tracking file.</returns>
    public static string GetLocationFilePath()
    {
        if (_locationFilePathOverride != null)
        {
            return _locationFilePathOverride;
        }

        var profileDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profileDir))
        {
            profileDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        if (string.IsNullOrWhiteSpace(profileDir))
        {
            throw new InvalidOperationException("Could not determine user profile or local application data directory for tracking installation location.");
        }

        return Path.Combine(profileDir, StorageMigrationConstants.GenHubConfigDirectoryName, StorageMigrationConstants.CustomInstallPathFileName);
    }

    /// <inheritdoc />
    public virtual void RecordInstallLocation() => RecordInstallLocationStatic(logger);

    /// <inheritdoc />
    public virtual string? GetRegisteredCustomInstallPath() => GetRegisteredCustomInstallPathStatic(logger);

    /// <inheritdoc />
    public virtual void ClearCustomInstallPath() => ClearCustomInstallPathStatic(logger);

    /// <summary>
    /// Sets an override for the location file path for unit testing.
    /// </summary>
    /// <param name="path">The override file path, or <see langword="null"/> to reset.</param>
    internal static void SetLocationFilePathOverrideForTesting(string? path) => _locationFilePathOverride = path;
}
