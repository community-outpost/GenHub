using GenHub.Common.Services.SecureStorage;
using GenHub.Core.Constants;
using System.Collections.Generic;

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
        return FileTokenPathResolver.GetPrimaryTokenFilePath(applicationDataPath, AppConstants.TokenFileName);
    }

    /// <summary>
    /// Gets the fallback token file path inside the default data root.
    /// </summary>
    /// <param name="applicationDataPath">The application data directory.</param>
    /// <returns>The fallback token file path, or null when the primary directory already is the default root.</returns>
    public static string? GetFallbackTokenFilePath(string applicationDataPath)
    {
        return FileTokenPathResolver.GetFallbackTokenFilePath(applicationDataPath, AppConstants.TokenFileName);
    }

    /// <summary>
    /// Removes the obsolete fallback copy without failing the caller when the stale file
    /// is locked or access is denied. The primary token is already persisted at that point,
    /// and a surviving copy is removed by the next save or sign-out.
    /// </summary>
    /// <param name="fallbackTokenFilePath">The fallback token file path, or null when there is none.</param>
    public static void DeleteFallbackCopyBestEffort(string? fallbackTokenFilePath)
    {
        FileTokenPathResolver.DeleteFallbackCopyBestEffort(fallbackTokenFilePath);
    }

    /// <summary>
    /// Gets the token files that currently exist, primary first, so a load can retry
    /// with the fallback copy when the primary copy fails to decrypt.
    /// </summary>
    /// <param name="primaryTokenFilePath">The primary token file path.</param>
    /// <param name="fallbackTokenFilePath">The fallback token file path, or null when there is none.</param>
    /// <returns>The existing token file paths in load order.</returns>
    public static IReadOnlyList<string> GetExistingTokenFilePaths(string primaryTokenFilePath, string? fallbackTokenFilePath)
    {
        return FileTokenPathResolver.GetExistingTokenFilePaths(primaryTokenFilePath, fallbackTokenFilePath);
    }

    /// <summary>
    /// Resolves the token file to read: the primary path when it exists, otherwise the fallback path.
    /// </summary>
    /// <param name="primaryTokenFilePath">The primary token file path.</param>
    /// <param name="fallbackTokenFilePath">The fallback token file path, or null when there is none.</param>
    /// <returns>The active token file path, or null when neither file exists.</returns>
    public static string? ResolveActiveTokenFilePath(string primaryTokenFilePath, string? fallbackTokenFilePath)
    {
        return FileTokenPathResolver.ResolveActiveTokenFilePath(primaryTokenFilePath, fallbackTokenFilePath);
    }
}
