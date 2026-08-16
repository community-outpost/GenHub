using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;
using MessagePack;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Manages build cache for change detection with MD5 hashing and modification time optimization.
/// Implements the change detection algorithm from the Python ModBuilder.
/// </summary>
public sealed class BuildCacheService : IBuildCacheService
{
    private const int MinimumCacheCapacity = 100;
    private const int MaximumCacheCapacity = 10000;
    private const double CapacityGrowthFactor = 0.1; // 10% buffer

    private readonly IMd5HashProvider _md5Provider;
    private readonly IFileHashRegistryService? _registryService;
    private readonly ILogger<BuildCacheService> _logger;
    private readonly Dictionary<string, BuildFilePathInfo> _oldCache = new(MinimumCacheCapacity, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BuildFilePathInfo> _newCache = new(MinimumCacheCapacity, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="BuildCacheService"/> class.
    /// </summary>
    /// <param name="md5Provider">The MD5 hash provider.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="registryService">Optional file hash registry service.</param>
    public BuildCacheService(
        IMd5HashProvider md5Provider,
        ILogger<BuildCacheService> logger,
        IFileHashRegistryService? registryService = null)
    {
        _md5Provider = md5Provider;
        _logger = logger;
        _registryService = registryService;
    }

    /// <inheritdoc/>
    public async Task<bool> LoadCacheAsync(string cachePath, CancellationToken cancellationToken = default)
    {
        try
        {
            // Try MessagePack format first (.msgpack extension)
            var msgpackPath = Path.ChangeExtension(cachePath, ".msgpack");
            if (File.Exists(msgpackPath))
            {
                return await LoadMessagePackCacheAsync(msgpackPath, cancellationToken).ConfigureAwait(false);
            }

            // Fallback to JSON format for backward compatibility
            if (File.Exists(cachePath))
            {
                return await LoadJsonCacheAsync(cachePath, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogDebug("Cache file not found at {CachePath}", cachePath);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load build cache from {CachePath}", cachePath);
            return false;
        }
    }

    /// <summary>
    /// Loads cache from MessagePack format (10x faster than JSON).
    /// </summary>
    /// <param name="cachePath">The cache file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if loaded successfully; otherwise, false.</returns>
    private async Task<bool> LoadMessagePackCacheAsync(string cachePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(cachePath);
        var cache = await MessagePackSerializer.DeserializeAsync<Dictionary<string, BuildFilePathInfo>>(
            stream,
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (cache != null)
        {
            var estimatedCapacity = EstimateCacheCapacity(cache.Count);
            _oldCache.Clear();
            _oldCache.EnsureCapacity(estimatedCapacity);

            foreach (var kvp in cache)
            {
                _oldCache[kvp.Key] = kvp.Value;
            }

            _logger.LogInformation("Loaded MessagePack build cache with {Count} entries from {CachePath}", _oldCache.Count, cachePath);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Loads cache from legacy JSON format (backward compatibility).
    /// </summary>
    /// <param name="cachePath">The cache file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if loaded successfully; otherwise, false.</returns>
    private async Task<bool> LoadJsonCacheAsync(string cachePath, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(cachePath, cancellationToken).ConfigureAwait(false);
        var cache = JsonSerializer.Deserialize<Dictionary<string, BuildFilePathInfo>>(json);

        if (cache != null)
        {
            var estimatedCapacity = EstimateCacheCapacity(cache.Count);
            _oldCache.Clear();
            _oldCache.EnsureCapacity(estimatedCapacity);

            foreach (var kvp in cache)
            {
                _oldCache[kvp.Key] = kvp.Value;
            }

            _logger.LogInformation("Loaded JSON build cache with {Count} entries from {CachePath}", _oldCache.Count, cachePath);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task<bool> SaveCacheAsync(string cachePath, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check cancellation before starting
            cancellationToken.ThrowIfCancellationRequested();

            var directory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Save as MessagePack format (10x faster than JSON)
            var msgpackPath = Path.ChangeExtension(cachePath, ".msgpack");
            await using var stream = File.Create(msgpackPath);
            await MessagePackSerializer.SerializeAsync(
                stream,
                _newCache,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("Saved MessagePack build cache with {Count} entries to {CachePath}", _newCache.Count, msgpackPath);
            return true;
        }
        catch (OperationCanceledException)
        {
            // Re-throw cancellation exceptions
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save build cache to {CachePath}", cachePath);
            return false;
        }
    }

    /// <inheritdoc/>
    public void AddFile(string filePath, double modifiedTime, string md5, Dictionary<string, object>? @params = null)
    {
        var normalizedPath = NormalizePath(filePath);

        // Pre-allocate capacity based on old cache size to avoid rehashing
        if (_newCache.Count == 0 && _oldCache.Count > 0)
        {
            var estimatedCapacity = EstimateCacheCapacity(_oldCache.Count);
            _newCache.EnsureCapacity(estimatedCapacity);
        }

        _newCache[normalizedPath] = new BuildFilePathInfo
        {
            Path = filePath,
            ModifiedTime = modifiedTime,
            Md5 = md5,
            Params = @params,
        };
    }

    /// <inheritdoc/>
    public BuildFilePathInfo? FindOldFile(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        return _oldCache.TryGetValue(normalizedPath, out var info) ? info : null;
    }

    /// <inheritdoc/>
    public async Task<string> ComputeOrReuseMd5Async(string filePath, CancellationToken cancellationToken = default)
    {
        // Optimization: Reuse cached MD5 if modification time unchanged
        var oldInfo = FindOldFile(filePath);
        if (oldInfo != null)
        {
            var currentMtime = GetFileModificationTime(filePath);
            if (Math.Abs(currentMtime - oldInfo.ModifiedTime) < 0.001) // Compare with small epsilon
            {
                _logger.LogTrace("Reusing cached MD5 for {FilePath} (mtime unchanged)", filePath);
                return oldInfo.Md5;
            }
        }

        // Compute new MD5
        return await _md5Provider.ComputeFileHashAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public BuildFileStatus DetermineFileStatus(string filePath, string currentMd5, Dictionary<string, object>? @params = null)
    {
        // Check FileHashRegistry FIRST (before cache) - 20-30% performance gain
        if (_registryService?.IsFileIrrelevant(filePath, currentMd5) == true)
        {
            _logger.LogTrace("File {FilePath} is Irrelevant (matches registry hash)", filePath);
            return BuildFileStatus.Irrelevant;
        }

        var oldInfo = FindOldFile(filePath);

        // Not in cache → Added
        if (oldInfo == null)
        {
            _logger.LogTrace("File {FilePath} is Added (not in cache)", filePath);
            return BuildFileStatus.Added;
        }

        // In cache, compare MD5 + params
        var currentInfo = new BuildFilePathInfo
        {
            Path = filePath,
            Md5 = currentMd5,
            Params = @params,
        };

        if (currentInfo.Matches(oldInfo))
        {
            _logger.LogTrace("File {FilePath} is Unchanged", filePath);
            return BuildFileStatus.Unchanged;
        }

        _logger.LogTrace("File {FilePath} is Changed (MD5 or params differ)", filePath);
        return BuildFileStatus.Changed;
    }

    /// <inheritdoc/>
    public void Clear()
    {
        _oldCache.Clear();
        _newCache.Clear();
        _logger.LogDebug("Build cache cleared");
    }

    /// <summary>
    /// Gets the file modification time as Unix timestamp.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <returns>The modification time as Unix timestamp.</returns>
    private static double GetFileModificationTime(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        return fileInfo.LastWriteTimeUtc.Subtract(DateTime.UnixEpoch).TotalSeconds;
    }

    /// <summary>
    /// Normalizes file path for case-insensitive comparison.
    /// </summary>
    /// <param name="filePath">The file path to normalize.</param>
    /// <returns>The normalized file path.</returns>
    private static string NormalizePath(string filePath)
    {
        return filePath.ToLowerInvariant();
    }

    /// <summary>
    /// Estimates optimal dictionary capacity based on previous cache size.
    /// Adds 10% growth buffer and clamps between minimum and maximum limits.
    /// </summary>
    /// <param name="previousCount">Number of entries in previous cache.</param>
    /// <returns>Estimated capacity for dictionary pre-allocation.</returns>
    private static int EstimateCacheCapacity(int previousCount)
    {
        if (previousCount <= 0)
        {
            return MinimumCacheCapacity;
        }

        var estimatedCapacity = previousCount + (int)(previousCount * CapacityGrowthFactor);
        return Math.Clamp(estimatedCapacity, MinimumCacheCapacity, MaximumCacheCapacity);
    }
}
