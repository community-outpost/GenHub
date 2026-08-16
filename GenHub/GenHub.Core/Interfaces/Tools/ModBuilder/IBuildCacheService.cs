using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Tools.ModBuilder;

namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Manages build cache for change detection with MD5 hashing and modification time optimization.
/// </summary>
public interface IBuildCacheService
{
    /// <summary>
    /// Loads the previous build cache from disk.
    /// </summary>
    /// <param name="cachePath">Path to the cache file (.json).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if cache was loaded successfully.</returns>
    Task<bool> LoadCacheAsync(string cachePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the current build cache to disk.
    /// </summary>
    /// <param name="cachePath">Path to the cache file (.json).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if cache was saved successfully.</returns>
    Task<bool> SaveCacheAsync(string cachePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds or updates a file in the new cache registry.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="modifiedTime">The file modification time.</param>
    /// <param name="md5">The MD5 hash.</param>
    /// <param name="params">Build parameters.</param>
    void AddFile(string filePath, double modifiedTime, string md5, Dictionary<string, object>? @params = null);

    /// <summary>
    /// Finds a file in the old cache registry.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <returns>The cached file info, or null if not found.</returns>
    BuildFilePathInfo? FindOldFile(string filePath);

    /// <summary>
    /// Computes the MD5 hash for a file, with optimization to reuse cached hash if mtime unchanged.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The MD5 hash.</returns>
    Task<string> ComputeOrReuseMd5Async(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines the change status of a file based on cache comparison.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="currentMd5">The current MD5 hash.</param>
    /// <param name="params">Build parameters.</param>
    /// <returns>The build file status.</returns>
    BuildFileStatus DetermineFileStatus(string filePath, string currentMd5, Dictionary<string, object>? @params = null);

    /// <summary>
    /// Clears the current cache.
    /// </summary>
    void Clear();
}
