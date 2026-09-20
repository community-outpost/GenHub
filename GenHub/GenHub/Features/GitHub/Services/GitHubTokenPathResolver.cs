using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using System.IO;

namespace GenHub.Features.GitHub.Services;

/// <summary>
/// Resolves the primary and fallback file paths for the persisted GitHub token.
/// </summary>
/// <remarks>
/// The token lives inside the application data directory, which the user can relocate. When the
/// current directory holds no token, the default data root is consulted as a fallback so a data
/// directory change does not silently sign the user out. Reads fall back while saves always target
/// the primary path and remove obsolete fallback credentials so credentials consolidate forward,
/// and sign-out removes both copies.
/// </remarks>
public static class GitHubTokenPathResolver
{
    /// <summary>
    /// Gets the primary token file path inside the given application data directory.
    /// </summary>
    /// <param name="applicationDataPath">The application data directory.</param>
    /// <returns>The primary token file path.</returns>
    public static string GetPrimaryTokenFilePath(string applicationDataPath)
    {
        return Path.Combine(applicationDataPath, AppConstants.TokenFileName);
    }

    /// <summary>
    /// Gets the fallback token file path inside the default data root.
    /// </summary>
    /// <param name="applicationDataPath">The application data directory.</param>
    /// <returns>The fallback token file path, or null when the primary directory already is the default root.</returns>
    public static string? GetFallbackTokenFilePath(string applicationDataPath)
    {
        var fallbackDirectory = StorageMigrationService.GetDefaultDataRoot();
        if (PathHelper.AreSamePath(applicationDataPath, fallbackDirectory))
        {
            return null;
        }

        return Path.Combine(fallbackDirectory, AppConstants.TokenFileName);
    }

    /// <summary>
    /// Resolves the token file to read: the primary path when it exists, otherwise the fallback path.
    /// </summary>
    /// <param name="primaryTokenFilePath">The primary token file path.</param>
    /// <param name="fallbackTokenFilePath">The fallback token file path, or null when there is none.</param>
    /// <returns>The active token file path, or null when neither file exists.</returns>
    public static string? ResolveActiveTokenFilePath(string primaryTokenFilePath, string? fallbackTokenFilePath)
    {
        if (File.Exists(primaryTokenFilePath))
        {
            return primaryTokenFilePath;
        }

        return fallbackTokenFilePath != null && File.Exists(fallbackTokenFilePath)
            ? fallbackTokenFilePath
            : null;
    }
}
