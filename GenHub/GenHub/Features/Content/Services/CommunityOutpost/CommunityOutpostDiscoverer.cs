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
public partial class CommunityOutpostDiscoverer(
    IHttpClientFactory httpClientFactory,
    IProviderDefinitionLoader providerLoader,
    ICatalogParserFactory catalogParserFactory,
    ILogger<CommunityOutpostDiscoverer> logger) : IContentDiscoverer
{
    /// <summary>
    /// Gets the provider ID for registration.
    /// </summary>
    public static string ProviderId => CommunityOutpostConstants.PublisherId;

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
            logger.LogInformation("Discovering content from Community Outpost...");

            // Get provider definition if not provided
            provider ??= providerLoader.GetProvider(CommunityOutpostConstants.PublisherId);
            if (provider == null)
            {
                logger.LogError("Provider definition not found for {ProviderId}", CommunityOutpostConstants.PublisherId);
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

            logger.LogInformation(
                "Using provider configuration - CatalogUrl: {CatalogUrl}, CatalogFormat: {Format}",
                catalogUrl,
                provider.CatalogFormat);

            var results = new List<ContentSearchResult>();

            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(catalogTimeout);

            // First, discover the Community Patch GameClient from legi.cc/patch
            var communityPatchResult = await DiscoverCommunityPatchAsync(client, patchPageUrl, provider, cancellationToken);
            if (communityPatchResult != null && MatchesQuery(communityPatchResult, query))
            {
                results.Add(communityPatchResult);
                logger.LogInformation("Discovered Community Patch: {Version}", communityPatchResult.Version);
            }

            // Then, fetch and parse the catalog using the appropriate parser
            try
            {
                var catalogContent = await client.GetStringAsync(catalogUrl, cancellationToken);

                // Get the catalog parser for this provider's format
                var parser = catalogParserFactory.GetParser(provider.CatalogFormat);
                if (parser == null)
                {
                    logger.LogError("No parser found for catalog format '{Format}'", provider.CatalogFormat);
                    return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(results);
                }

                // Parse the catalog - the parser uses GenPatcherContentRegistry for metadata
                var parseResult = await parser.ParseAsync(catalogContent, provider, cancellationToken);
                if (parseResult.Success && parseResult.Data != null)
                {
                    var catalogResults = parseResult.Data.Where(r => MatchesQuery(r, query)).ToList();
                    results.AddRange(catalogResults);

                    logger.LogInformation(
                        "Found {ItemCount} content items from catalog (after filtering: {FilteredCount})",
                        parseResult.Data.Count(),
                        catalogResults.Count);
                }
                else
                {
                    logger.LogWarning("Failed to parse catalog: {Error}", parseResult.FirstError);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch/parse GenPatcher catalog, returning Community Patch only");
            }

            logger.LogInformation(
                "Returning {ResultCount} content items from Community Outpost",
                results.Count);

            return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(results);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to discover Community Outpost content");
            return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure($"Discovery failed: {ex.Message}");
        }
    }

    [GeneratedRegex(@"href=[""']([^""']*generalszh-weekly-(\d{4}-\d{2}-\d{2})[^""']*\.zip)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex DownloadUrlRegex();

    /// <summary>
    /// Checks if a search result matches the query filters.
    /// </summary>
    private static bool MatchesQuery(ContentSearchResult result, ContentSearchQuery query)
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
            logger.LogDebug("Fetching Community Patch page from {Url}", patchPageUrl);

            var pageContent = await client.GetStringAsync(patchPageUrl, cancellationToken);

            // Look for the download link pattern: generalszh-weekly-YYYY-MM-DD*.zip
            var downloadUrlMatch = DownloadUrlRegex().Match(pageContent);

            if (!downloadUrlMatch.Success)
            {
                logger.LogWarning("Could not find Community Patch download link on {Url}", patchPageUrl);
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

            logger.LogDebug("Found Community Patch download: {Url} (version {Version})", downloadUrl, versionDate);

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
            logger.LogWarning(ex, "Failed to discover Community Patch from {Url}", patchPageUrl);
            return null;
        }
    }
}
