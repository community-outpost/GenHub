using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using Microsoft.Extensions.Caching.Memory;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Implements a dynamic content cache using an in-memory store.
/// </summary>
public class MemoryDynamicContentCache(IMemoryCache memoryCache) : IDynamicContentCache
{
    private static readonly List<string> _keys = [];
    private readonly IMemoryCache _memoryCache = memoryCache;

    /// <inheritdoc/>
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        _memoryCache.TryGetValue(key, out T? value);
        return Task.FromResult(value);
    }

    /// <inheritdoc/>
    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        var options = new MemoryCacheEntryOptions();
        if (expiration.HasValue)
        {
            options.SetAbsoluteExpiration(expiration.Value);
        }
        else
        {
            options.SetSlidingExpiration(TimeSpan.FromMinutes(ContentConstants.DefaultCacheExpirationMinutes));
        }

        _memoryCache.Set(key, value, options);

        lock (_keys)
        {
            if (!_keys.Contains(key))
            {
                _keys.Add(key);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task InvalidateAsync(string pattern, CancellationToken cancellationToken = default)
    {
        var regex = new Regex(pattern.Replace("*", ".*"));
        List<string> keysToRemove;

        lock (_keys)
        {
            keysToRemove = [.._keys.Where(k => regex.IsMatch(k))];
        }

        foreach (var key in keysToRemove)
        {
            _memoryCache.Remove(key);
            lock (_keys)
            {
                _keys.Remove(key);
            }
        }

        return Task.CompletedTask;
    }
}