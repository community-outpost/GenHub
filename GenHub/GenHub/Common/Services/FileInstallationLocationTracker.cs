using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Storage;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Security;

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

    private static string? _locationFilePathOverride;

    /// <summary>
    /// Records the current installation directory in a user profile marker file when running from a custom install root.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public static void RecordInstallLocationStatic(ILogger? logger = null)
    {
        if (StorageMigrationService.IsCustomInstallRoot())
        {
            var customRoot = StorageMigrationService.GetSourceRootDirectory();
            RecordCustomInstallPathStatic(customRoot, logger);
        }
    }

    /// <summary>
    /// Records an explicitly specified custom installation directory in the user profile marker file.
    /// </summary>
    /// <param name="customPath">The custom installation root directory path to record.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public static void RecordCustomInstallPathStatic(string customPath, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(customPath))
        {
            return;
        }

        try
        {
            var filePath = GetLocationFilePath();
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, customPath);
            logger?.LogInformation("Recorded custom installation root in file: {CustomRoot}", customPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or InvalidOperationException)
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
            if (!File.Exists(filePath))
            {
                return null;
            }

            var path = File.ReadAllText(filePath).Trim();
            if (PathHelper.TrySanitizeLocalPath(path, out var sanitized) &&
                IsValidCustomInstallCandidate(sanitized))
            {
                return sanitized;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or InvalidOperationException)
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or InvalidOperationException)
        {
            logger?.LogWarning(ex, ClearLocationFailureMessage);
        }
    }

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

    private static bool IsValidCustomInstallCandidate(string path)
    {
        var currentRoot = StorageMigrationService.GetSourceRootDirectory();
        if (PathHelper.AreSamePath(path, currentRoot))
        {
            return false;
        }

        var defaultRoot = StorageMigrationService.GetDefaultInstallRoot();
        if (!string.IsNullOrWhiteSpace(defaultRoot) && PathHelper.AreSamePath(path, defaultRoot))
        {
            return false;
        }

        return Directory.Exists(path) && StorageMigrationService.IsVelopackRoot(path);
    }
}
