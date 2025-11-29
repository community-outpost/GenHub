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
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.CommunityOutpost.Models;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Discovers content from Community Outpost (legi.cc) using the GenPatcher dl.dat catalog.
/// The catalog contains official patches, tools, addons, and other game content.
/// </summary>
/// <param name="httpClientFactory">HTTP client factory.</param>
/// <param name="logger">Logger instance.</param>
public class CommunityOutpostDiscoverer(
    IHttpClientFactory httpClientFactory,
    ILogger<CommunityOutpostDiscoverer> logger) : IContentDiscoverer
{
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
    public async Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Discovering content from Community Outpost...");

            var results = new List<ContentSearchResult>();

            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(CommunityOutpostConstants.CatalogDownloadTimeoutSeconds);

            // First, discover the Community Patch GameClient from legi.cc/patch
            var communityPatchResult = await DiscoverCommunityPatchAsync(client, cancellationToken);
            if (communityPatchResult != null && MatchesQuery(communityPatchResult, query))
            {
                results.Add(communityPatchResult);
                logger.LogInformation("Discovered Community Patch: {Version}", communityPatchResult.Version);
            }

            // Then, fetch the GenPatcher dl.dat catalog for other content
            try
            {
                var catalogContent = await client.GetStringAsync(CommunityOutpostConstants.CatalogUrl, cancellationToken);
                var parser = new GenPatcherDatParser(logger);
                var catalog = parser.Parse(catalogContent);

                if (catalog.Items.Count > 0)
                {
                    logger.LogInformation(
                        "Found {ItemCount} content items in GenPatcher catalog (version {Version})",
                        catalog.Items.Count,
                        catalog.CatalogVersion);

                    foreach (var item in catalog.Items)
                    {
                        var searchResult = ConvertToContentSearchResult(item, catalog.CatalogVersion);
                        if (searchResult != null && MatchesQuery(searchResult, query))
                        {
                            results.Add(searchResult);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch GenPatcher catalog, continuing with Community Patch only");
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
    /// Discovers the Community Patch (TheSuperHackers Patch Build) from legi.cc/patch.
    /// </summary>
    private async Task<ContentSearchResult?> DiscoverCommunityPatchAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogDebug("Fetching Community Patch page from {Url}", CommunityOutpostConstants.PatchPageUrl);

            var pageContent = await client.GetStringAsync(CommunityOutpostConstants.PatchPageUrl, cancellationToken);

            // Look for the download link pattern: generalszh-weekly-YYYY-MM-DD*.zip
            var downloadUrlMatch = Regex.Match(
                pageContent,
                @"href=[""']([^""']*generalszh-weekly-(\d{4}-\d{2}-\d{2})[^""']*\.zip)[""']",
                RegexOptions.IgnoreCase);

            if (!downloadUrlMatch.Success)
            {
                logger.LogWarning("Could not find Community Patch download link on {Url}", CommunityOutpostConstants.PatchPageUrl);
                return null;
            }

            var downloadUrl = downloadUrlMatch.Groups[1].Value;
            var versionDate = downloadUrlMatch.Groups[2].Value;

            // Make the URL absolute if it's relative
            // Relative URLs should be resolved against the page URL (legi.cc/patch/)
            if (!downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                // The file is hosted in the same directory as the page (patch/)
                var baseUrl = CommunityOutpostConstants.PatchPageUrl.TrimEnd('/');
                downloadUrl = $"{baseUrl}/{downloadUrl.TrimStart('/')}";
            }

            logger.LogDebug("Found Community Patch download: {Url} (version {Version})", downloadUrl, versionDate);

            var result = new ContentSearchResult
            {
                Id = $"{CommunityOutpostConstants.PublisherId}.community-patch",
                Name = "Community Patch (TheSuperHackers Build)",
                Description = "The latest TheSuperHackers patch build for Zero Hour. Includes bug fixes, balance changes, and quality of life improvements.",
                Version = versionDate,
                ContentType = ContentType.GameClient,
                TargetGame = GameType.ZeroHour,
                ProviderName = SourceName,
                AuthorName = "TheSuperHackers",
                SourceUrl = downloadUrl,
                RequiresResolution = true,
                ResolverId = CommunityOutpostConstants.PublisherId,
                LastUpdated = DateTime.Now,
            };

            // Add tags
            result.Tags.Add("community-patch");
            result.Tags.Add("thesuperhackers");
            result.Tags.Add("weekly");
            result.Tags.Add("game-client");

            // Store metadata for resolver
            result.ResolverMetadata["contentCode"] = "community-patch";
            result.ResolverMetadata["downloadUrl"] = downloadUrl;
            result.ResolverMetadata["category"] = "CommunityPatch";

            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to discover Community Patch from {Url}", CommunityOutpostConstants.PatchPageUrl);
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
            var preferredUrl = GenPatcherDatParser.GetPreferredDownloadUrl(item);
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
                Description = metadata.Description,
                Version = metadata.Version,
                ContentType = metadata.ContentType,
                TargetGame = metadata.TargetGame,
                ProviderName = SourceName,
                AuthorName = CommunityOutpostConstants.PublisherName,
                SourceUrl = preferredUrl,
                DownloadSize = item.FileSize,
                RequiresResolution = true,
                ResolverId = CommunityOutpostConstants.PublisherId,
                LastUpdated = DateTime.Now, // dl.dat doesn't include timestamps
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
