using GenHub.Core.Constants;
using System;
using System.IO;

namespace GenHub.Core.Helpers;

/// <summary>
/// Resolves the default application data root shared by every GenHub assembly.
/// </summary>
public static class AppDataPathHelper
{
    private static string? _legacyRoamingRootOverride;

    /// <summary>
    /// Gets the default application data root, honouring the <see cref="StorageMigrationConstants.AppDataPathEnvVar"/> override.
    /// </summary>
    /// <returns>
    /// The override when it is a valid local path; otherwise the GenHub directory in LocalApplicationData,
    /// the user profile, or the temp directory, in that order.
    /// </returns>
    public static string GetDataRoot() =>
        ResolveDataRoot(Environment.GetEnvironmentVariable(StorageMigrationConstants.AppDataPathEnvVar));

    /// <summary>
    /// Gets the roaming GenHub directory that older versions wrote markers, backups and tokens to.
    /// </summary>
    /// <remarks>
    /// Legacy data belongs to the default data root only. When the root is overridden, migrating it
    /// would move the user's real files into a root that may be disposable.
    /// </remarks>
    /// <returns>The legacy roaming directory, or <see langword="null"/> when the data root is overridden.</returns>
    public static string? GetLegacyRoamingRoot()
    {
        if (_legacyRoamingRootOverride != null)
        {
            return _legacyRoamingRootOverride;
        }

        if (PathHelper.TrySanitizeLocalPath(Environment.GetEnvironmentVariable(StorageMigrationConstants.AppDataPathEnvVar), out _))
        {
            return null;
        }

        var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return string.IsNullOrWhiteSpace(roamingAppData) ? null : Path.Combine(roamingAppData, AppConstants.AppName);
    }

    /// <summary>
    /// Points <see cref="GetLegacyRoamingRoot"/> at a test directory.
    /// </summary>
    /// <param name="path">The directory to use, or <see langword="null"/> to reset.</param>
    internal static void SetLegacyRoamingRootOverrideForTesting(string? path) => _legacyRoamingRootOverride = path;

    /// <summary>
    /// Resolves the application data root from an explicit override value.
    /// </summary>
    /// <param name="configured">The override value, which is ignored unless it is a valid local path.</param>
    /// <returns>The resolved application data root.</returns>
    internal static string ResolveDataRoot(string? configured)
    {
        if (PathHelper.TrySanitizeLocalPath(configured, out var sanitized))
        {
            return sanitized;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return !string.IsNullOrWhiteSpace(localAppData)
            ? Path.Combine(localAppData, AppConstants.AppName)
            : Path.Combine(Path.GetTempPath(), AppConstants.AppName);
    }
}
