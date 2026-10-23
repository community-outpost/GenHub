using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Catalog;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Loads publisher-defined custom tabs for the content item selected in the downloads browser detail view.
/// </summary>
/// <remarks>
/// Tabs come from the selected item's own <c>customTabs</c> entry, carried in its resolver metadata, and from
/// catalog-level tabs whose <c>appliesTo</c> list names the item. An item tab wins over a catalog-level tab with the same ID.
/// </remarks>
/// <param name="subscriptionStore">The publisher subscription store.</param>
/// <param name="catalogParser">The publisher catalog parser.</param>
/// <param name="httpClientFactory">The HTTP client factory.</param>
/// <param name="logger">The logger instance.</param>
public class CatalogTabProvider(
    IPublisherSubscriptionStore subscriptionStore,
    IPublisherCatalogParser catalogParser,
    IHttpClientFactory httpClientFactory,
    ILogger<CatalogTabProvider> logger) : ITabProvider
{
    private sealed class CatalogItemTabs
    {
        [JsonPropertyName("customTabs")]
        public List<CatalogTabDefinition>? CustomTabs { get; set; }
    }

    private const int MaxCacheEntries = 100;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromMinutes(1);
    private static readonly JsonSerializerOptions CatalogTabJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ConcurrentDictionary<string, (DateTime FetchedAt, PublisherCatalog? Catalog)> _catalogCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<PublisherCatalog?>>> _inFlightFetches = new(StringComparer.OrdinalIgnoreCase);

    private void TrimCacheIfNeeded()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _catalogCache)
        {
            var ttl = kvp.Value.Catalog != null ? CacheDuration : NegativeCacheDuration;
            if (now - kvp.Value.FetchedAt >= ttl)
            {
                _catalogCache.TryRemove(kvp.Key, out _);
            }
        }

        if (_catalogCache.Count >= MaxCacheEntries)
        {
            var toRemove = _catalogCache.OrderBy(kvp => kvp.Value.FetchedAt)
                .Take(_catalogCache.Count - MaxCacheEntries + 1)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                _catalogCache.TryRemove(key, out _);
            }
        }
    }

    /// <inheritdoc/>
    public string ProviderId => "catalog-tabs";

    /// <inheritdoc/>
    public bool CanProvideTabsFor(ContentSearchResult searchResult)
    {
        // Verifies that the search result selected in the downloads browser has a non-empty provider name to perform catalog subscription lookups
        return !string.IsNullOrEmpty(searchResult.ProviderName);
    }

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "Tab extraction catches all errors to prevent crashing the UI.")]
    public async Task<IReadOnlyList<CustomTabDefinition>> GetTabsAsync(
        ContentSearchResult searchResult,
        CancellationToken cancellationToken = default)
    {
        try
        {
            searchResult.ResolverMetadata.TryGetValue(CatalogConstants.CatalogContentIdMetadataKey, out var catalogContentId);
            var contentId = !string.IsNullOrWhiteSpace(catalogContentId) ? catalogContentId : searchResult.Id;
            var resultId = searchResult.Id;

            var itemTabs = ReadItemTabs(searchResult);
            if (itemTabs != null)
            {
                itemTabs = CatalogTabValidator.Normalize(itemTabs);
                CatalogTabValidator.Sanitize(
                    itemTabs,
                    issue => logger.LogWarning(
                        "Dropped publisher tab content: {Issue}",
                        CatalogTabValidator.Describe($"Content item '{contentId}'", issue)));
            }

            var publisherId = ResolvePublisherId(searchResult);
            var catalog = await GetOrFetchCatalogAsync(publisherId, cancellationToken);

            if (itemTabs == null && catalog != null)
            {
                itemTabs = catalog.Content?
                    .FirstOrDefault(item => item != null &&
                        (item.Id.Equals(contentId, StringComparison.OrdinalIgnoreCase) ||
                         item.Id.Equals(resultId, StringComparison.OrdinalIgnoreCase)))?
                    .CustomTabs;
            }

            var rootTabs = catalog?.CustomTabs?
                .Where(catalogTab => catalogTab != null && TabAppliesToContent(catalogTab, contentId, resultId))
                ?? [];

            var merged = new List<CustomTabDefinition>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var catalogTab in (itemTabs ?? []).Where(tab => tab != null).Concat(rootTabs))
            {
                var tab = CatalogTabMapper.Map(catalogTab);
                if (string.IsNullOrWhiteSpace(tab.TabId) || string.IsNullOrWhiteSpace(tab.Header) || !seenIds.Add(tab.TabId))
                {
                    continue;
                }

                merged.Add(tab);
            }

            return merged;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "error loading custom tabs for content '{ContentId}' from publisher '{Publisher}'", searchResult.Id, searchResult.ProviderName);
            return [];
        }
    }

    private static string ResolvePublisherId(ContentSearchResult searchResult)
    {
        var publisherId = searchResult.ProviderName;
        if (!searchResult.ResolverMetadata.TryGetValue(CatalogConstants.PublisherProfileJsonMetadataKey, out var publisherProfileJson))
        {
            return publisherId;
        }

        try
        {
            var publisherProfile = JsonSerializer.Deserialize<PublisherProfile>(publisherProfileJson, CatalogTabJsonOptions);
            return !string.IsNullOrWhiteSpace(publisherProfile?.Id) ? publisherProfile.Id : publisherId;
        }
        catch (JsonException)
        {
            return publisherId;
        }
    }

    private static List<CatalogTabDefinition>? ReadItemTabs(ContentSearchResult searchResult)
    {
        if (!searchResult.ResolverMetadata.TryGetValue(CatalogConstants.CatalogItemJsonMetadataKey, out var itemJson) ||
            string.IsNullOrWhiteSpace(itemJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CatalogItemTabs>(itemJson, CatalogTabJsonOptions)?.CustomTabs;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TabAppliesToContent(CatalogTabDefinition catalogTab, string contentId, string resultId)
    {
        return catalogTab.AppliesTo?.Any(a =>
            a != null &&
            (a.Equals(contentId, StringComparison.OrdinalIgnoreCase) ||
             a.Equals(resultId, StringComparison.OrdinalIgnoreCase))) == true;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "Background fetch failures are logged and returned as null.")]
    private async Task<PublisherCatalog?> GetOrFetchCatalogAsync(
        string publisherId,
        CancellationToken cancellationToken)
    {
        if (_catalogCache.TryGetValue(publisherId, out var cached))
        {
            var ttl = cached.Catalog != null ? CacheDuration : NegativeCacheDuration;
            if (DateTime.UtcNow - cached.FetchedAt < ttl)
            {
                return cached.Catalog;
            }
        }

        var lazyFetch = _inFlightFetches.GetOrAdd(
            publisherId,
            id => new Lazy<Task<PublisherCatalog?>>(
                () => FetchCatalogCoreAsync(id, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazyFetch.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch catalog for publisher '{PublisherId}'", publisherId);
            return null;
        }
        finally
        {
            if (lazyFetch.IsValueCreated && lazyFetch.Value.IsCompleted)
            {
                _inFlightFetches.TryRemove(KeyValuePair.Create(publisherId, lazyFetch));
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "Network and parsing failures are logged and negatively cached.")]
    private async Task<PublisherCatalog?> FetchCatalogCoreAsync(string publisherId, CancellationToken cancellationToken)
    {
        try
        {
            var subscriptionResult = await subscriptionStore.GetSubscriptionAsync(
                publisherId,
                cancellationToken).ConfigureAwait(false);

            if (!subscriptionResult.Success || subscriptionResult.Data == null)
            {
                TrimCacheIfNeeded();
                _catalogCache[publisherId] = (DateTime.UtcNow, null);
                return null;
            }

            var subscription = subscriptionResult.Data;
            var httpClient = httpClientFactory.CreateClient(CatalogConstants.CatalogHttpClientName);

            var catalogJson = await CatalogDocumentReader.ReadAsync(
                httpClient,
                subscription.CatalogUrl,
                CatalogConstants.MaxCatalogSizeBytes,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var catalogResult = await catalogParser.ParseCatalogAsync(catalogJson, cancellationToken).ConfigureAwait(false);
            var resolvedCatalog = catalogResult.Success && catalogResult.Data != null ? catalogResult.Data : null;
            TrimCacheIfNeeded();
            _catalogCache[publisherId] = (DateTime.UtcNow, resolvedCatalog);
            return resolvedCatalog;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error fetching catalog for publisher '{PublisherId}', recording negative cache entry", publisherId);
            TrimCacheIfNeeded();
            _catalogCache[publisherId] = (DateTime.UtcNow, null);
            return null;
        }
        finally
        {
            if (_inFlightFetches.TryGetValue(publisherId, out var currentLazy))
            {
                _inFlightFetches.TryRemove(KeyValuePair.Create(publisherId, currentLazy));
            }
        }
    }
}
