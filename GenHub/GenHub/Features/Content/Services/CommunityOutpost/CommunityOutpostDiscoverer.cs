using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Discovers content from Community Outpost (legi.cc) using the GenPatcher dl.dat catalog.
/// Uses data-driven configuration from provider.json for endpoints, timeouts, and mirrors.
/// Metadata is sourced from <see cref="GenPatcherContentRegistry"/>.
/// </summary>
/// <param name="httpClientFactory">HTTP client factory.</param>
/// <param name="providerLoader">Provider definition loader.</param>
/// <param name="catalogParserFactory">Factory for getting catalog parsers.</param>
/// <param name="logger">Logger instance.</param>
public partial class CommunityOutpostDiscoverer(
    IHttpClientFactory httpClientFactory,
    IProviderDefinitionLoader providerLoader,
    ICatalogParserFactory catalogParserFactory,
    ILogger<CommunityOutpostDiscoverer> logger) : IContentDiscoverer
{
    [GeneratedRegex(@"href=[""']([^""']*generalszh-weekly-(\d{4}-\d{2}-\d{2})[^""']*\.zip)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex CommunityPatchRegex();

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
    public Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        // Call the provider-aware overload with null provider (uses defaults from constants)
        return DiscoverAsync(provider: null, query, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ProviderDefinition? provider,
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation(
                "Discovering content from Community Outpost (Search: '{Search}', Type: {Type}, Game: {Game})",
                query.SearchTerm,
                query.ContentType,
                query.TargetGame);

            // Get provider definition if not provided
            provider ??= providerLoader.GetProvider(CommunityOutpostConstants.PublisherId);
            if (provider == null)
            {
                logger.LogError("Provider definition not found for {ProviderId}", CommunityOutpostConstants.PublisherId);
                return OperationResult<ContentDiscoveryResult>.CreateFailure(
                    $"Provider definition '{CommunityOutpostConstants.PublisherId}' not found. Ensure communityoutpost.provider.json exists.");
            }

            // Get configuration from provider definition
            var catalogUrl = provider.Endpoints.CatalogUrl;
            var patchPageUrl = provider.Endpoints.GetEndpoint("patchPageUrl");
            var catalogTimeout = provider.Timeouts.CatalogTimeoutSeconds;

            if (string.IsNullOrEmpty(catalogUrl))
            {
                return OperationResult<ContentDiscoveryResult>.CreateFailure(
                    "CatalogUrl not configured in provider definition.");
            }

            if (string.IsNullOrEmpty(patchPageUrl))
            {
                return OperationResult<ContentDiscoveryResult>.CreateFailure(
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

                    // Return success with just community patch if parser fails
                    return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
                    {
                        Items = results,
                        HasMoreItems = false,
                    });
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

            return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = results,
                HasMoreItems = false, // Catalog based, all items returned at once
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to discover Community Outpost content");
            return OperationResult<ContentDiscoveryResult>.CreateFailure($"Discovery failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets tags for a content category.
    /// </summary>
    private static string[] GetTagsForCategory(GenPatcherContentCategory category)
    {
        return category switch
        {
            GenPatcherContentCategory.CommunityPatch => ["community-patch", "thesuperhackers", "weekly", "game-client"],
            GenPatcherContentCategory.OfficialPatch => CommunityOutpostConstants.OfficialPatchTags,
            GenPatcherContentCategory.BaseGame => ["base-game", "vanilla"],
            GenPatcherContentCategory.ControlBar => ["addon", "control-bar", "ui"],
            GenPatcherContentCategory.Hotkeys => ["addon", "hotkeys", "keyboard"],
            GenPatcherContentCategory.Camera => ["addon", "camera"],
            GenPatcherContentCategory.Tools => CommunityOutpostConstants.ToolsTags,
            GenPatcherContentCategory.Maps => ["maps", "missions"],
            GenPatcherContentCategory.Visuals => ["addon", "visuals", "graphics"],
            GenPatcherContentCategory.Prerequisites => ["prerequisite", "system"],
            _ => CommunityOutpostConstants.AddonTags,
        };
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
                LogFilterMismatch(result, query, "Search term");
                return false;
            }
        }

        // Check content type filter
        if (query.ContentType.HasValue && result.ContentType != query.ContentType.Value)
        {
            LogFilterMismatch(result, query, "Content Type");
            return false;
        }

        // Check target game filter
        if (query.TargetGame.HasValue && result.TargetGame != query.TargetGame.Value)
        {
            LogFilterMismatch(result, query, "Target Game");
            return false;
        }

        return true;
    }

    private void LogFilterMismatch(ContentSearchResult result, ContentSearchQuery query, string reason)
    {
        logger.LogTrace(
            "Filtered out {Name} ({Code}): {Reason}. Query: Type={QType}, Game={QGame}. Item: Type={IType}, Game={IGame}",
            result.Name,
            result.Id,
            reason,
            query.ContentType,
            query.TargetGame,
            result.ContentType,
            result.TargetGame);
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
            var downloadUrlMatch = CommunityPatchRegex().Match(pageContent);

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
                IconUrl = "avares://GenHub/Assets/Logos/communityoutpost-logo.png", // Added missing icon
            };

            if (DateTime.TryParse(versionDate, out var date))
            {
                result.LastUpdated = date;
            }

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

    /// <summary>
    /// Converts a GenPatcher content item to a ContentSearchResult.
    /// </summary>
    private ContentSearchResult? ConvertToContentSearchResult(GenPatcherContentItem item, string catalogVersion)
    {
        try
        {
            var metadata = GenPatcherContentRegistry.GetMetadata(item.ContentCode);
            var preferredUrl = providerLoader.GetProvider(CommunityOutpostConstants.PublisherId)?.Endpoints.GetPreferredDownloadUrl(item);
            var allUrls = GenPatcherDatParser.GetOrderedDownloadUrls(item);

            if (string.IsNullOrEmpty(preferredUrl))
            {
                logger.LogWarning("No download URLs available for content code {Code}", item.ContentCode);
                return null;
            }

            // Skip official patches (104*, 108*) for now - these are language-specific patches
            // that clutter the UI. The base game clients (10gn, 10zh) already include 1.08/1.04.
            if (metadata.Category == GenPatcherContentCategory.OfficialPatch)
            {
                logger.LogDebug("Skipping official patch {Code} - not shown in UI", item.ContentCode);
                return null;
            }

            // Make URL absolute if it's relative (dl.dat URLs are usually relative like "generalszh-xxx.dat")
            // The files are hosted in the /patch/ directory on legi.cc
            if (!preferredUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var baseUrl = CommunityOutpostConstants.PatchPageUrl.TrimEnd('/');
                preferredUrl = $"{baseUrl}/{preferredUrl.TrimStart('/')}";
                logger.LogDebug("Made URL absolute: {Url}", preferredUrl);
            }

            // Also fix the allUrls list for mirror support
            var absoluteUrls = allUrls.Select(url =>
            {
                if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    var baseUrl = CommunityOutpostConstants.PatchPageUrl.TrimEnd('/');
                    return $"{baseUrl}/{url.TrimStart('/')}";
                }

                return url;
            }).ToList();

            var result = new ContentSearchResult
            {
                Id = $"{CommunityOutpostConstants.PublisherId}.{item.ContentCode}",
                Name = metadata.DisplayName,
                Description = metadata.Description ?? string.Empty,
                Version = metadata.Version ?? "1.0",
                ContentType = metadata.ContentType,
                TargetGame = metadata.TargetGame,
                ProviderName = SourceName,
                AuthorName = CommunityOutpostConstants.PublisherName,
                SourceUrl = preferredUrl,
                DownloadSize = item.FileSize,
                RequiresResolution = true,
                ResolverId = CommunityOutpostConstants.PublisherId,
                LastUpdated = DateTime.Now, // dl.dat doesn't include timestamps

                // Use publisher logo as default content icon
                IconUrl = "avares://GenHub/Assets/Logos/communityoutpost-logo.png",
            };

            // Add tags based on content category
            var tags = GetTagsForCategory(metadata.Category);
            foreach (var tag in tags)
            {
                result.Tags.Add(tag);
            }

            // Add language tag if applicable
            if (!string.IsNullOrEmpty(metadata.LanguageCode))
            {
                result.Tags.Add(metadata.LanguageCode);
            }

            // Store metadata for resolver
            result.ResolverMetadata["contentCode"] = item.ContentCode;
            result.ResolverMetadata["catalogVersion"] = catalogVersion;
            result.ResolverMetadata["fileSize"] = item.FileSize.ToString();
            result.ResolverMetadata["category"] = metadata.Category.ToString();

            // Store all mirror URLs as JSON for fallback support (absolute URLs)
            result.ResolverMetadata["mirrorUrls"] = JsonSerializer.Serialize(absoluteUrls);

            // Store mirror names for display
            result.ResolverMetadata["mirrors"] = string.Join(", ", item.Mirrors.Select(m => m.Name));

            logger.LogDebug(
                "Created ContentSearchResult for {Code}: {Name} ({ContentType}, {Game})",
                item.ContentCode,
                metadata.DisplayName,
                metadata.ContentType,
                metadata.TargetGame);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to convert content item {Code} to search result", item.ContentCode);
            return null;
        }
    }
}