using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Parsers;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Default implementation of <see cref="IContentCacheService"/> using an in-memory cache.
/// </summary>
public sealed class ContentCacheService(ILogger<ContentCacheService> logger) : IContentCacheService
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly TimeSpan _defaultTtl = TimeSpan.FromHours(1);

    private record CacheEntry(ParsedWebPage Data, DateTime ExpiresAt);

    /// <inheritdoc/>
    public Task<ParsedWebPage?> GetAsync(string cacheKey, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(cacheKey, out var entry))
        {
            if (DateTime.UtcNow < entry.ExpiresAt)
            {
                logger.LogDebug("Cache hit for {CacheKey}", cacheKey);
                return Task.FromResult<ParsedWebPage?>(entry.Data);
            }

            // Expired, remove it
            _cache.TryRemove(cacheKey, out _);
        }

        logger.LogDebug("Cache miss for {CacheKey}", cacheKey);
        return Task.FromResult<ParsedWebPage?>(null);
    }

    /// <inheritdoc/>
    public Task SetAsync(string cacheKey, ParsedWebPage data, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var expiresAt = DateTime.UtcNow + (ttl ?? _defaultTtl);
        _cache[cacheKey] = new CacheEntry(data, expiresAt);
        logger.LogDebug("Cached {CacheKey} until {ExpiresAt}", cacheKey, expiresAt);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public bool HasValidCache(string cacheKey)
    {
        return _cache.TryGetValue(cacheKey, out var entry) && DateTime.UtcNow < entry.ExpiresAt;
    }

    /// <inheritdoc/>
    public void Invalidate(string cacheKey)
    {
        _cache.TryRemove(cacheKey, out _);
        logger.LogDebug("Invalidated cache for {CacheKey}", cacheKey);
    }

    /// <summary>
    /// Clears all cached data.
    /// </summary>
    public void ClearAll()
    {
        _cache.Clear();
        logger.LogInformation("Cleared all content cache");
    }
}
