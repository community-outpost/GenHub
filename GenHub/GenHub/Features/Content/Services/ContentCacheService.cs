using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Parsers;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Default thread-safe in-memory implementation of <see cref="IContentCacheService"/>.
/// Stores parsed web page models in a concurrent dictionary with timestamp-based time-to-live expiration to minimize remote web scraping overhead during downloads browser navigation.
/// </summary>
/// <param name="logger">The logger instance for recording cache hits, misses, and invalidation operations.</param>
public sealed class ContentCacheService(ILogger<ContentCacheService> logger) : IContentCacheService
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly TimeSpan _defaultTtl = TimeSpan.FromHours(1);

    private sealed record CacheEntry(ParsedWebPage Data, DateTime ExpiresAt);

    /// <inheritdoc/>
    public Task<ParsedWebPage?> GetAsync(string cacheKey, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(cacheKey, out var entry))
        {
            if (DateTime.UtcNow < entry.ExpiresAt)
            {
                logger.LogDebug("cache hit for {CacheKey}", cacheKey);
                return Task.FromResult<ParsedWebPage?>(entry.Data);
            }

            // Remove expired cache entry from concurrent dictionary
            _cache.TryRemove(cacheKey, out _);
        }

        logger.LogDebug("cache miss for {CacheKey}", cacheKey);
        return Task.FromResult<ParsedWebPage?>(null);
    }

    /// <inheritdoc/>
    public Task SetAsync(string cacheKey, ParsedWebPage data, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var expiresAt = DateTime.UtcNow + (ttl ?? _defaultTtl);
        _cache[cacheKey] = new CacheEntry(data, expiresAt);
        logger.LogDebug("cached {CacheKey} until {ExpiresAt}", cacheKey, expiresAt);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public bool HasValidCache(string cacheKey)
    {
        // Evaluates whether an unexpired entry exists for the given cache key
        return _cache.TryGetValue(cacheKey, out var entry) && DateTime.UtcNow < entry.ExpiresAt;
    }

    /// <inheritdoc/>
    public void Invalidate(string cacheKey)
    {
        // Removes specified cache key from memory dictionary
        _cache.TryRemove(cacheKey, out _);
        logger.LogDebug("invalidated cache for {CacheKey}", cacheKey);
    }

    /// <inheritdoc/>
    public void ClearAll()
    {
        // Purges all cached web page entries from memory
        _cache.Clear();
        logger.LogInformation("cleared all content cache");
    }
}
