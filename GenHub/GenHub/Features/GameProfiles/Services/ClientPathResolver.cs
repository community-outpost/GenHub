using GenHub.Core.Constants;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Helper methods for resolving client executable paths and working directories.
/// </summary>
internal static class ClientPathResolver
{
    private static readonly ConcurrentDictionary<string, (string? ExecutablePath, string? WorkingDirectory)> ResolvedPathsCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Clears the cached client path resolutions.
    /// </summary>
    internal static void ClearCache() => ResolvedPathsCache.Clear();

    /// <summary>
    /// Attempts to resolve the executable path and working directory for a game client from its source path and entry point.
    /// </summary>
    /// <param name="sourcePath">The manifest or display item source path.</param>
    /// <param name="entryPoint">Optional entry point relative to directory source paths.</param>
    /// <returns>A tuple containing the resolved executable path and working directory, or nulls if unresolvable.</returns>
    internal static (string? ExecutablePath, string? WorkingDirectory) ResolveClientPaths(string? sourcePath, string? entryPoint = null)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return (null, null);
        }

        var cacheKey = string.IsNullOrEmpty(entryPoint) ? sourcePath : $"{sourcePath}|{entryPoint}";
        if (ResolvedPathsCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var ext = Path.GetExtension(sourcePath);
        if (!string.IsNullOrEmpty(ext) && IsArchiveOrPackageExtension(ext))
        {
            return ResolvedPathsCache.GetOrAdd(cacheKey, (null, null));
        }

        if (File.Exists(sourcePath))
        {
            var result = (sourcePath, Path.GetDirectoryName(sourcePath));
            return ResolvedPathsCache.GetOrAdd(cacheKey, result);
        }

        if (Directory.Exists(sourcePath) && !string.IsNullOrWhiteSpace(entryPoint))
        {
            var combined = Path.Combine(sourcePath, entryPoint);
            if (File.Exists(combined))
            {
                var result = (combined, sourcePath);
                return ResolvedPathsCache.GetOrAdd(cacheKey, result);
            }
        }

        return ResolvedPathsCache.GetOrAdd(cacheKey, (null, null));
    }

    /// <summary>
    /// Determines whether the given file extension corresponds to an archive or package format.
    /// </summary>
    /// <param name="extension">The file extension including the leading dot.</param>
    /// <returns>True if the extension is an archive or package; otherwise, false.</returns>
    private static bool IsArchiveOrPackageExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return false;
        }

        return ContentFormatConstants.UnderstoodArchiveExtensions.Any(archiveExt =>
                   string.Equals(extension, archiveExt, StringComparison.OrdinalIgnoreCase)) ||
               ContentFormatConstants.GuidedRejectionExtensions.Any(pkgExt =>
                   string.Equals(extension, pkgExt, StringComparison.OrdinalIgnoreCase));
    }
}
