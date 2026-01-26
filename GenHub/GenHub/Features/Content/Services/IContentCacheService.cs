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
    /// <summary>
/// Retrieves a cached parsed web page for the specified cache key.
/// </summary>
/// <param name="cacheKey">The cache key identifying the entry (for example, "publisher:contentId" or a URL).</param>
/// <returns>The cached <see cref="ParsedWebPage"/> if present and not expired, or <c>null</c> otherwise.</returns>
    Task<ParsedWebPage?> GetAsync(string cacheKey, CancellationToken ct = default);

    /// <summary>
    /// Stores parsed page data in the cache.
    /// </summary>
    /// <param name="cacheKey">The cache key.</param>
    /// <param name="data">The parsed data to cache.</param>
    /// <param name="ttl">Optional custom TTL. Uses default (1 hour) if not specified.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <summary>
/// Stores parsed web page data in the cache under the specified key with an optional time-to-live.
/// </summary>
/// <param name="cacheKey">Cache key identifying the entry (for example: publisher:contentId or a URL).</param>
/// <param name="data">The parsed web page data to cache.</param>
/// <param name="ttl">Optional time-to-live for the cache entry; if null the default TTL of 1 hour is used.</param>
/// <param name="ct">Cancellation token for the operation.</param>
    Task SetAsync(string cacheKey, ParsedWebPage data, TimeSpan? ttl = null, CancellationToken ct = default);

    /// <summary>
    /// Checks if cached data exists and is valid.
    /// </summary>
    /// <param name="cacheKey">The cache key to check.</param>
    /// <summary>
/// Determines whether a valid cached ParsedWebPage exists for the given cache key.
/// </summary>
/// <param name="cacheKey">The cache key identifying the entry (e.g., "publisher:contentId" or a URL).</param>
/// <returns>`true` if valid cached data exists for the specified cache key, `false` otherwise.</returns>
    bool HasValidCache(string cacheKey);

    /// <summary>
    /// Invalidates a specific cache entry.
    /// </summary>
    /// <summary>
/// Invalidate the cached entry identified by the specified cache key.
/// </summary>
/// <param name="cacheKey">The cache key of the entry to invalidate (for example, "publisher:contentId" or a URL).</param>
    void Invalidate(string cacheKey);

    /// <summary>
    /// Clears all cached data.
    /// <summary>
/// Removes all entries from the content cache.
/// </summary>
    void ClearAll();
}