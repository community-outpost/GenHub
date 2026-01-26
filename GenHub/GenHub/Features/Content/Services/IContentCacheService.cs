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
    /// Retrieves a cached parsed web page for the specified cache key.
    /// </summary>
    /// <param name="cacheKey">The cache key identifying the entry (for example, "publisher:contentId" or a URL).</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The cached <see cref="ParsedWebPage"/> if present and not expired, or <c>null</c> otherwise.</returns>
    Task<ParsedWebPage?> GetAsync(string cacheKey, CancellationToken ct = default);

    /// <summary>
    /// Stores parsed web page data in the cache under the specified key with an optional time-to-live.
    /// </summary>
    /// <param name="cacheKey">Cache key identifying the entry (for example: publisher:contentId or a URL).</param>
    /// <param name="data">The parsed web page data to cache.</param>
    /// <param name="ttl">Optional time-to-live for the cache entry; if null the implementation-defined default TTL is used (typically 1 hour).</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>A task that represents the asynchronous set operation.</returns>
    Task SetAsync(string cacheKey, ParsedWebPage data, TimeSpan? ttl = null, CancellationToken ct = default);

    /// <summary>
    /// Determines whether a valid cached ParsedWebPage exists for the given cache key.
    /// </summary>
    /// <param name="cacheKey">The cache key identifying the entry (e.g., "publisher:contentId" or a URL).</param>
    /// <returns>`true` if valid cached data exists for the specified cache key, `false` otherwise.</returns>
    bool HasValidCache(string cacheKey);

    /// <summary>
    /// Invalidate the cached entry identified by the specified cache key.
    /// </summary>
    /// <param name="cacheKey">The cache key of the entry to invalidate (for example, "publisher:contentId" or a URL).</param>
    void Invalidate(string cacheKey);

    /// <summary>
    /// Removes all entries from the content cache if the underlying implementation supports it (best‑effort).
    /// </summary>
    void ClearAll();
}