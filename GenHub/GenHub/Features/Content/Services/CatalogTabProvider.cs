using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Catalog;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Loads dynamic custom tab definitions from remote publisher catalog manifests for the downloads browser content detail view.
/// When a user opens the detail page for a specific mod, map, or patch in the downloads section, publishers can display extra custom UI tabs (e.g. documentation, server stats, sub-addons, or custom web views) defined in their catalog json.
/// </summary>
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
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, (DateTime FetchedAt, string CatalogUrl, PublisherCatalog? Catalog)> _catalogCache = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public string ProviderId => "catalog-tabs";

    /// <inheritdoc/>
    public bool CanProvideTabsFor(ContentSearchResult searchResult)
    {
        // Verifies that the search result selected in the downloads browser has a non-empty provider name to perform catalog subscription lookups
        return !string.IsNullOrEmpty(searchResult.ProviderName);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CustomTabDefinition>> GetTabsAsync(
        ContentSearchResult searchResult,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var publisherId = ResolvePublisherId(searchResult);
            var catalog = await GetOrFetchCatalogAsync(publisherId, cancellationToken);

            if (catalog?.CustomTabs is not { Count: > 0 })
            {
                return [];
            }

            // Convert parsed catalog tab definitions into runtime tab definitions for the downloads detail view
            searchResult.ResolverMetadata.TryGetValue(CatalogConstants.CatalogContentIdMetadataKey, out var catalogContentId);
            var contentId = !string.IsNullOrWhiteSpace(catalogContentId) ? catalogContentId : searchResult.Id;
            var resultId = searchResult.Id;

            return catalog.CustomTabs
                .Where(catalogTab => TabAppliesToContent(catalogTab, contentId, resultId))
                .Select(catalogTab => MapToTabDefinition(catalogTab, searchResult))
                .ToList();
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
        if (searchResult.ResolverMetadata.TryGetValue(CatalogConstants.PublisherProfileJsonMetadataKey, out var publisherProfileJson))
        {
            var publisherProfile = JsonSerializer.Deserialize<PublisherProfile>(publisherProfileJson);
            if (!string.IsNullOrWhiteSpace(publisherProfile?.Id))
            {
                publisherId = publisherProfile.Id;
            }
        }

        return publisherId;
    }

    private static bool TabAppliesToContent(CatalogTabDefinition catalogTab, string contentId, string resultId)
    {
        if (catalogTab.AppliesTo is not { Count: > 0 })
        {
            return true;
        }

        return catalogTab.AppliesTo.Any(a =>
            a.Equals(contentId, StringComparison.OrdinalIgnoreCase) ||
            a.Equals(resultId, StringComparison.OrdinalIgnoreCase));
    }

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

        var subscriptionResult = await subscriptionStore.GetSubscriptionAsync(
            publisherId,
            cancellationToken);

        if (!subscriptionResult.Success || subscriptionResult.Data == null)
        {
            _catalogCache[publisherId] = (DateTime.UtcNow, string.Empty, null);
            return null;
        }

        var subscription = subscriptionResult.Data;
        var httpClient = httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        var catalogJson = await CatalogDocumentReader.ReadAsync(
            httpClient,
            subscription.CatalogUrl,
            CatalogConstants.MaxCatalogSizeBytes,
            cancellationToken: cancellationToken);

        var catalogResult = await catalogParser.ParseCatalogAsync(catalogJson, cancellationToken);
        var resolvedCatalog = catalogResult.Success && catalogResult.Data != null ? catalogResult.Data : null;
        _catalogCache[publisherId] = (DateTime.UtcNow, subscription.CatalogUrl, resolvedCatalog);
        return resolvedCatalog;
    }

    private CustomTabDefinition MapToTabDefinition(CatalogTabDefinition catalogTab, ContentSearchResult searchResult)
    {
        if (!Enum.TryParse<TabContentType>(catalogTab.ContentType, true, out var contentType) ||
            !Enum.IsDefined(contentType))
        {
            logger.LogWarning("invalid content type '{ContentType}' for tab '{TabId}' in publisher '{Publisher}'", catalogTab.ContentType, catalogTab.TabId, searchResult.ProviderName);
            contentType = TabContentType.Custom;
        }

        return new CustomTabDefinition
        {
            TabId = catalogTab.TabId,
            Header = catalogTab.Header,
            Icon = catalogTab.Icon,
            Order = catalogTab.Order,
            ContentType = contentType,
            DataSourceUrl = catalogTab.DataSourceUrl,
            ContentTemplate = catalogTab.ContentTemplate,
            Intro = catalogTab.Intro,
            Cards = catalogTab.Cards?.ConvertAll(card => new CustomTabCardDefinition
            {
                Title = card.Title,
                Description = card.Description,
                ImageUrl = card.ImageUrl,
                Label = card.Label,
                AccentColor = card.AccentColor,
            }) ?? [],
            Metadata = catalogTab.Metadata,
            IsVisible = catalogTab.IsVisible,
            LazyLoad = catalogTab.LazyLoad,
        };
    }
}
