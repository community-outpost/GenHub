using System;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Parsers;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Service for caching parsed content data to avoid repeated fetches.
/// </summary>
public interface IContentCacheService
{
    /// <summary>
    /// Gets cached parsed page data.
    /// </summary>
    /// <param name="cacheKey">The cache key (typically publisher:contentId or URL).</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Cached data if available and not expired, null otherwise.</returns>
    Task<ParsedWebPage?> GetAsync(string cacheKey, CancellationToken ct = default);

    /// <summary>
    /// Stores parsed page data in the cache.
    /// </summary>
    /// <param name="cacheKey">The cache key.</param>
    /// <param name="data">The parsed data to cache.</param>
    /// <param name="ttl">Optional custom TTL. Uses default (1 hour) if not specified.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetAsync(string cacheKey, ParsedWebPage data, TimeSpan? ttl = null, CancellationToken ct = default);

    /// <summary>
    /// Checks if cached data exists and is valid.
    /// </summary>
    /// <param name="cacheKey">The cache key to check.</param>
    /// <returns>True if cached data exists and is valid, otherwise false.</returns>
    bool HasValidCache(string cacheKey);

    /// <summary>
    /// Invalidates a specific cache entry.
    /// </summary>
    /// <param name="cacheKey">The cache key to invalidate.</param>
    void Invalidate(string cacheKey);

    /// <summary>
    /// Clears all cached data.
    /// </summary>
    void ClearAll();
}
