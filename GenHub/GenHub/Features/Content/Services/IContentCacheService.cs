using System;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Parsers;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Defines the in-memory caching contract for parsed web page data across content providers.
/// Caches parsed web page models to prevent redundant http network requests and expensive html re-parsing when users browse or filter content items in the downloads browser.
/// </summary>
public interface IContentCacheService
{
    /// <summary>
    /// Retrieves cached parsed web page data for the specified cache key if present and not expired.
    /// </summary>
    /// <param name="cacheKey">The unique cache key representing the web page (e.g. provider:content-id or target url).</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The cached parsed web page instance if valid and unexpired; otherwise null.</returns>
    Task<ParsedWebPage?> GetAsync(string cacheKey, CancellationToken ct = default);

    /// <summary>
    /// Stores parsed web page data in the in-memory cache with an optional custom time-to-live duration.
    /// </summary>
    /// <param name="cacheKey">The unique cache key identifying the cached entry.</param>
    /// <param name="data">The parsed web page instance to cache.</param>
    /// <param name="ttl">Optional time-to-live duration; defaults to one hour if omitted.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Asynchronous task representing the cache storage operation.</returns>
    Task SetAsync(string cacheKey, ParsedWebPage data, TimeSpan? ttl = null, CancellationToken ct = default);

    /// <summary>
    /// Checks whether a valid, unexpired cache entry exists for the specified cache key.
    /// </summary>
    /// <param name="cacheKey">The cache key to evaluate.</param>
    /// <returns>True if an entry exists and its expiration time has not passed; otherwise false.</returns>
    bool HasValidCache(string cacheKey);

    /// <summary>
    /// Invalidates and removes a specific cached entry by its key.
    /// </summary>
    /// <param name="cacheKey">The unique cache key to remove from cache.</param>
    void Invalidate(string cacheKey);

    /// <summary>
    /// Clears all cached web page entries from memory.
    /// </summary>
    void ClearAll();
}
