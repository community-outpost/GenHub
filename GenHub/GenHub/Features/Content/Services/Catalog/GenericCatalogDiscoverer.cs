using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Generic discoverer for publisher catalogs following the GenHub schema.
/// Works for ALL subscribed publishers - no custom code needed per publisher.
/// </summary>
public class GenericCatalogDiscoverer(
    ILogger<GenericCatalogDiscoverer> logger,
    IHttpClientFactory httpClientFactory,
    IPublisherCatalogParser catalogParser,
    IVersionSelector versionSelector) : IContentDiscoverer
{
    private readonly ILogger<GenericCatalogDiscoverer> _logger = logger;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IPublisherCatalogParser _catalogParser = catalogParser;
    private readonly IVersionSelector _versionSelector = versionSelector;

    private PublisherSubscription? _subscription;
    private PublisherCatalog? _cachedCatalog;

    /// <summary>
    /// Gets the unique identifier of the resolver used by this discoverer.
    /// </summary>
    public static string ResolverId => CatalogConstants.GenericCatalogResolverId;

    private static bool MatchesQuery(CatalogContentItem content, ContentSearchQuery query)
    {
        // Filter by game type
        if (query.TargetGame.HasValue && content.TargetGame != query.TargetGame.Value)
        {
            return false;
        }

        // Filter by content type
        if (query.ContentType.HasValue && content.ContentType != query.ContentType.Value)
        {
            return false;
        }

        // Filter by search text
        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var searchLower = query.SearchTerm.ToLowerInvariant();
            if (!content.Name.Contains(searchLower, StringComparison.OrdinalIgnoreCase) &&
                !content.Description.Contains(searchLower, StringComparison.OrdinalIgnoreCase) &&
                !content.Tags.Any(t => t.Contains(searchLower, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
    }

    private static int ExtractVersionNumber(string version)
    {
        if (int.TryParse(new string([.. version.Where(char.IsDigit)]), out var result))
        {
            return result;
        }

        return 0;
    }

    /// <inheritdoc />
    public string SourceName => _subscription?.PublisherName ?? "Generic Catalog";

    /// <inheritdoc />
    public string Description => _subscription != null
        ? $"Content from {_subscription.PublisherName}"
        : "Generic catalog-based content source";

    /// <inheritdoc />
    public bool IsEnabled => _subscription != null;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresDiscovery | ContentSourceCapabilities.SupportsManifestGeneration;

    /// <summary>
    /// Configures this discoverer for a specific publisher subscription.
    /// </summary>
    /// <param name="subscription">The publisher subscription.</param>
    public void Configure(PublisherSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        _subscription = subscription;
        _logger.LogDebug("Configured discoverer for publisher: {PublisherId}", subscription.PublisherId);
    }

    /// <inheritdoc />
    public async Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        if (_subscription == null)
        {
            return OperationResult<ContentDiscoveryResult>.CreateFailure(
                "Discoverer not configured with subscription");
        }

        try
        {
            // Fetch and parse catalog
            var catalogResult = await FetchCatalogAsync(cancellationToken);
            if (!catalogResult.Success)
            {
                return OperationResult<ContentDiscoveryResult>.CreateFailure(catalogResult);
            }

            var catalog = catalogResult.Data!;
            _cachedCatalog = catalog;

            // Convert catalog items to search results
            var searchResults = ConvertCatalogToSearchResults(catalog, query).ToList();

            var result = new ContentDiscoveryResult
            {
                Items = searchResults,
                TotalItems = searchResults.Count,
                HasMoreItems = false, // All results returned at once from catalog
            };

            _logger.LogInformation(
                "Discovered {Count} content items from publisher '{PublisherId}'",
                searchResults.Count,
                _subscription.PublisherId);

            return OperationResult<ContentDiscoveryResult>.CreateSuccess(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover content from publisher '{PublisherId}'", _subscription.PublisherId);
            return OperationResult<ContentDiscoveryResult>.CreateFailure($"Discovery failed: {ex.Message}");
        }
    }

    private async Task<OperationResult<PublisherCatalog>> FetchCatalogAsync(CancellationToken cancellationToken)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            _logger.LogDebug("Fetching catalog from: {CatalogUrl}", _subscription!.CatalogUrl);

            var response = await httpClient.GetAsync(_subscription.CatalogUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            // Check size limit
            if (response.Content.Headers.ContentLength > CatalogConstants.MaxCatalogSizeBytes)
            {
                return OperationResult<PublisherCatalog>.CreateFailure(
                    $"Catalog exceeds maximum size of {CatalogConstants.MaxCatalogSizeBytes} bytes");
            }

            var catalogJson = await response.Content.ReadAsStringAsync(cancellationToken);

            // Parse catalog
            return await _catalogParser.ParseCatalogAsync(catalogJson, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching catalog");
            return OperationResult<PublisherCatalog>.CreateFailure($"Failed to fetch catalog: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Catalog fetch timed out");
            return OperationResult<PublisherCatalog>.CreateFailure("Catalog fetch timed out");
        }
    }

    private List<ContentSearchResult> ConvertCatalogToSearchResults(
        PublisherCatalog catalog,
        ContentSearchQuery query)
    {
        var results = new List<ContentSearchResult>();

        foreach (var contentItem in catalog.Content)
        {
            // Apply version filtering (default: latest only)
            var policy = query.IncludeOlderVersions
                ? VersionPolicy.AllVersions
                : VersionPolicy.LatestStableOnly;

            var selectedReleases = _versionSelector.SelectReleases(contentItem.Releases, policy);

            foreach (var release in selectedReleases)
            {
                // Apply search filters
                if (!MatchesQuery(contentItem, query))
                {
                    continue;
                }

                var searchResult = new ContentSearchResult
                {
                    Id = ManifestIdGenerator.GeneratePublisherContentId(
                        catalog.Publisher.Id,
                        contentItem.ContentType,
                        contentItem.Id,
                        ExtractVersionNumber(release.Version)),
                    Name = contentItem.Name,
                    Description = contentItem.Description,
                    Version = release.Version,
                    ContentType = contentItem.ContentType,
                    TargetGame = contentItem.TargetGame,
                    ProviderName = catalog.Publisher.Name,
                    AuthorName = contentItem.Metadata?.Author ?? catalog.Publisher.Name,
                    ResolverId = ResolverId,
                    IconUrl = catalog.Publisher.AvatarUrl, // Default to publisher avatar
                    BannerUrl = contentItem.Metadata?.BannerUrl,
                    LastUpdated = release.ReleaseDate,
                    RequiresResolution = true,
                };

                // Add screenshots
                if (contentItem.Metadata?.ScreenshotUrls != null)
                {
                    foreach (var url in contentItem.Metadata.ScreenshotUrls)
                    {
                        searchResult.ScreenshotUrls.Add(url);
                    }
                }

                // Add tags (read-only collection, use .Add())
                foreach (var tag in contentItem.Tags)
                {
                    searchResult.Tags.Add(tag);
                }

                // Add resolver metadata (read-only dictionary, use .Add())
                searchResult.ResolverMetadata["catalogItemJson"] = System.Text.Json.JsonSerializer.Serialize(contentItem);
                searchResult.ResolverMetadata["releaseJson"] = System.Text.Json.JsonSerializer.Serialize(release);
                searchResult.ResolverMetadata["publisherProfileJson"] = System.Text.Json.JsonSerializer.Serialize(catalog.Publisher);

                results.Add(searchResult);
            }
        }

        return results;
    }
}
