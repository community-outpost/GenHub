using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Helper methods for resolving client executable paths and working directories.
/// </summary>
internal static class ClientPathResolver
{
    private static readonly ConcurrentDictionary<(string SourcePath, string? EntryPoint), (string? ExecutablePath, string? WorkingDirectory)> ResolvedPathsCache =
        new(ClientPathKeyComparer.Instance);

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

        var cacheKey = (sourcePath, entryPoint);
        if (ResolvedPathsCache.TryGetValue(cacheKey, out var cached))
        {
            if (cached.ExecutablePath != null && File.Exists(cached.ExecutablePath))
            {
                return cached;
            }

            ResolvedPathsCache.TryRemove(cacheKey, out _);
        }

        if (Directory.Exists(sourcePath) && !string.IsNullOrWhiteSpace(entryPoint))
        {
            var policyResult = ContentPathPolicy.ResolveContainedFile(sourcePath, entryPoint);
            if (policyResult.Success && File.Exists(policyResult.Data))
            {
                var result = (policyResult.Data, sourcePath);
                return ResolvedPathsCache.GetOrAdd(cacheKey, result);
            }
        }

        var ext = Path.GetExtension(sourcePath);
        if (!string.IsNullOrEmpty(ext) && IsArchiveOrPackageExtension(ext) && !Directory.Exists(sourcePath))
        {
            return (null, null);
        }

        if (File.Exists(sourcePath))
        {
            var result = (sourcePath, Path.GetDirectoryName(sourcePath));
            return ResolvedPathsCache.GetOrAdd(cacheKey, result);
        }

        return (null, null);
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

    private sealed class ClientPathKeyComparer : IEqualityComparer<(string SourcePath, string? EntryPoint)>
    {
        public static readonly ClientPathKeyComparer Instance = new();

        public bool Equals((string SourcePath, string? EntryPoint) x, (string SourcePath, string? EntryPoint) y)
        {
            return PathHelper.PathComparer.Equals(x.SourcePath, y.SourcePath) &&
                   PathHelper.PathComparer.Equals(x.EntryPoint, y.EntryPoint);
        }

        public int GetHashCode((string SourcePath, string? EntryPoint) obj)
        {
            return HashCode.Combine(
                PathHelper.PathComparer.GetHashCode(obj.SourcePath),
                obj.EntryPoint != null ? PathHelper.PathComparer.GetHashCode(obj.EntryPoint) : 0);
        }
    }
}
