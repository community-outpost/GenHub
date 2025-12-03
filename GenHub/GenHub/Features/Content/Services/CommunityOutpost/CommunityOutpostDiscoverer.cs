using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Discovers content from Community Outpost (legi.cc) using the GenPatcher dl.dat catalog.
/// Uses data-driven configuration from provider.json for endpoints, timeouts, and mirrors.
/// Metadata is sourced from <see cref="Models.GenPatcherContentRegistry"/>.
/// </summary>
public class CommunityOutpostDiscoverer : IContentDiscoverer
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProviderDefinitionLoader _providerLoader;
    private readonly ICatalogParserFactory _catalogParserFactory;
    private readonly ILogger<CommunityOutpostDiscoverer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommunityOutpostDiscoverer"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="providerLoader">Provider definition loader.</param>
    /// <param name="catalogParserFactory">Factory for getting catalog parsers.</param>
    /// <param name="logger">Logger instance.</param>
    public CommunityOutpostDiscoverer(
        IHttpClientFactory httpClientFactory,
        IProviderDefinitionLoader providerLoader,
        ICatalogParserFactory catalogParserFactory,
        ILogger<CommunityOutpostDiscoverer> logger)
    {
        _httpClientFactory = httpClientFactory;
        _providerLoader = providerLoader;
        _catalogParserFactory = catalogParserFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string SourceName => CommunityOutpostConstants.PublisherType;

    /// <inheritdoc/>
    public string Description => CommunityOutpostConstants.DiscovererDescription;

    /// <inheritdoc/>
    public bool IsEnabled => true;

    /// <inheritdoc/>
    public ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.RequiresDiscovery |
        ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <summary>
    /// Gets the provider ID for registration.
    /// </summary>
    public string ProviderId => CommunityOutpostConstants.PublisherId;

    /// <inheritdoc/>
    public Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        // Call the provider-aware overload with null provider (uses defaults from constants)
        return DiscoverAsync(provider: null, query, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(
        ProviderDefinition? provider,
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Discovering content from Community Outpost...");

            // Get provider definition if not provided
            provider ??= _providerLoader.GetProvider(CommunityOutpostConstants.PublisherId);
            if (provider == null)
            {
                _logger.LogError("Provider definition not found for {ProviderId}", CommunityOutpostConstants.PublisherId);
                return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(
                    $"Provider definition '{CommunityOutpostConstants.PublisherId}' not found. Ensure communityoutpost.provider.json exists.");
            }

            // Get configuration from provider definition
            var catalogUrl = provider.Endpoints.CatalogUrl;
            var patchPageUrl = provider.Endpoints.GetEndpoint("patchPageUrl");
            var catalogTimeout = provider.Timeouts.CatalogTimeoutSeconds;

            if (string.IsNullOrEmpty(catalogUrl))
            {
                return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(
                    "CatalogUrl not configured in provider definition.");
            }

            if (string.IsNullOrEmpty(patchPageUrl))
            {
                return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(
                    "PatchPageUrl not configured in provider definition.");
            }

            _logger.LogInformation(
                "Using provider configuration - CatalogUrl: {CatalogUrl}, CatalogFormat: {Format}",
                catalogUrl,
                provider.CatalogFormat);

            var results = new List<ContentSearchResult>();

            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(catalogTimeout);

            // First, discover the Community Patch GameClient from legi.cc/patch
            var communityPatchResult = await DiscoverCommunityPatchAsync(client, patchPageUrl, provider, cancellationToken);
            if (communityPatchResult != null && MatchesQuery(communityPatchResult, query))
            {
                results.Add(communityPatchResult);
                _logger.LogInformation("Discovered Community Patch: {Version}", communityPatchResult.Version);
            }

            // Then, fetch and parse the catalog using the appropriate parser
            try
            {
                var catalogContent = await client.GetStringAsync(catalogUrl, cancellationToken);

                // Get the catalog parser for this provider's format
                var parser = _catalogParserFactory.GetParser(provider.CatalogFormat);
                if (parser == null)
                {
                    _logger.LogError("No parser found for catalog format '{Format}'", provider.CatalogFormat);
                    return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(results);
                }

                // Parse the catalog - the parser uses GenPatcherContentRegistry for metadata
                var parseResult = await parser.ParseAsync(catalogContent, provider, cancellationToken);
                if (parseResult.Success && parseResult.Data != null)
                {
                    var catalogResults = parseResult.Data.Where(r => MatchesQuery(r, query)).ToList();
                    results.AddRange(catalogResults);

                    _logger.LogInformation(
                        "Found {ItemCount} content items from catalog (after filtering: {FilteredCount})",
                        parseResult.Data.Count(),
                        catalogResults.Count);
                }
                else
                {
                    _logger.LogWarning("Failed to parse catalog: {Error}", parseResult.FirstError);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch/parse GenPatcher catalog, returning Community Patch only");
            }

            _logger.LogInformation(
                "Returning {ResultCount} content items from Community Outpost",
                results.Count);

            return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover Community Outpost content");
            return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure($"Discovery failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Discovers the Community Patch (TheSuperHackers Patch Build) from legi.cc/patch.
    /// </summary>
    private async Task<ContentSearchResult?> DiscoverCommunityPatchAsync(
        HttpClient client,
        string patchPageUrl,
        ProviderDefinition? provider,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Fetching Community Patch page from {Url}", patchPageUrl);

            var pageContent = await client.GetStringAsync(patchPageUrl, cancellationToken);

            // Look for the download link pattern: generalszh-weekly-YYYY-MM-DD*.zip
            var downloadUrlMatch = Regex.Match(
                pageContent,
                @"href=[""']([^""']*generalszh-weekly-(\d{4}-\d{2}-\d{2})[^""']*\.zip)[""']",
                RegexOptions.IgnoreCase);

            if (!downloadUrlMatch.Success)
            {
                _logger.LogWarning("Could not find Community Patch download link on {Url}", patchPageUrl);
                return null;
            }

            var downloadUrl = downloadUrlMatch.Groups[1].Value;
            var versionDate = downloadUrlMatch.Groups[2].Value;

            // Make the URL absolute if it's relative
            if (!downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var baseUrl = patchPageUrl.TrimEnd('/');
                downloadUrl = $"{baseUrl}/{downloadUrl.TrimStart('/')}";
            }

            _logger.LogDebug("Found Community Patch download: {Url} (version {Version})", downloadUrl, versionDate);

            var providerId = provider?.ProviderId ?? CommunityOutpostConstants.PublisherId;
            var providerName = provider?.PublisherType ?? CommunityOutpostConstants.PublisherType;

            var result = new ContentSearchResult
            {
                Id = $"{providerId}.community-patch",
                Name = "Community Patch (TheSuperHackers Build)",
                Description = "The latest TheSuperHackers patch build for Zero Hour. Includes bug fixes, balance changes, and quality of life improvements.",
                Version = versionDate,
                ContentType = ContentType.GameClient,
                TargetGame = GameType.ZeroHour,
                ProviderName = providerName,
                AuthorName = "TheSuperHackers",
                SourceUrl = downloadUrl,
                RequiresResolution = true,
                ResolverId = providerId,
                LastUpdated = DateTime.Now,
            };

            // Add tags
            result.Tags.Add("community-patch");
            result.Tags.Add("thesuperhackers");
            result.Tags.Add("weekly");
            result.Tags.Add("game-client");

            // Add default tags from provider
            if (provider != null)
            {
                foreach (var tag in provider.DefaultTags)
                {
                    if (!result.Tags.Contains(tag))
                    {
                        result.Tags.Add(tag);
                    }
                }
            }

            // Store metadata for resolver
            result.ResolverMetadata["contentCode"] = "community-patch";
            result.ResolverMetadata["downloadUrl"] = downloadUrl;
            result.ResolverMetadata["category"] = "CommunityPatch";

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to discover Community Patch from {Url}", patchPageUrl);
            return null;
        }
    }

    /// <summary>
    /// Checks if a search result matches the query filters.
    /// </summary>
    private bool MatchesQuery(ContentSearchResult result, ContentSearchQuery query)
    {
        // If no filters specified, include all
        if (string.IsNullOrWhiteSpace(query.SearchTerm) &&
            !query.ContentType.HasValue &&
            !query.TargetGame.HasValue)
        {
            return true;
        }

        // Check search term
        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.ToLowerInvariant();
            var nameMatches = result.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false;
            var descMatches = result.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false;
            var tagMatches = result.Tags.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase));

            if (!nameMatches && !descMatches && !tagMatches)
            {
                return false;
            }
        }

        // Check content type filter
        if (query.ContentType.HasValue && result.ContentType != query.ContentType.Value)
        {
            return false;
        }

        // Check target game filter
        if (query.TargetGame.HasValue && result.TargetGame != query.TargetGame.Value)
        {
            return false;
        }

        return true;
    }
}
