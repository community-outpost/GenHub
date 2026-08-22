using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Helpers;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// Discovers maps from AODMaps (Art of Defense Maps) website.
/// </summary>
[SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Domain acronym")]
public partial class AODMapsDiscoverer(
    IHttpClientFactory httpClientFactory,
    ILogger<AODMapsDiscoverer> logger) : IContentDiscoverer
{
    [GeneratedRegex(@"(\d+(?:,\d{3})*)\s*downloads?", RegexOptions.IgnoreCase)]
    private static partial Regex DownloadCountRegex();

    [GeneratedRegex(@"(\d+)\.html", RegexOptions.None)]
    private static partial Regex HtmlPageNumberRegex();

    [GeneratedRegex(@"(?:^|[?&=])(?<players>[1-8])P(?:[_&]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex PlayerCountFromDownloadIdRegex();

    [GeneratedRegex(@"\b(?<players>[1-8])\s*(?:players?|p)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PlayerCountFromTextRegex();

    [GeneratedRegex(@"/(?<players>[1-8])_players", RegexOptions.IgnoreCase)]
    private static partial Regex PlayerCountFromPageUrlRegex();

    /// <summary>
    /// Makes a relative URL absolute by prepending the AODMaps base URL.
    /// </summary>
    /// <param name="url">The URL to make absolute.</param>
    /// <param name="sourceUrl">The page containing the URL.</param>
    /// <returns>The absolute URL, or the original URL if already absolute or null/empty.</returns>
    private static string? MakeAbsoluteUrl(string? url, string sourceUrl)
    {
        if (string.IsNullOrEmpty(url))
        {
            return url;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.AbsoluteUri;
        }

        // AOD category pages use relative image paths. Resolve against the page rather than the
        // site root so "AOA/map.png" and "../haritalar/map.png" both remain valid.
        var baseUri = Uri.TryCreate(sourceUrl, UriKind.Absolute, out var pageUri)
            ? pageUri
            : new Uri(AODMapsConstants.BaseUrl, UriKind.Absolute);

        return Uri.TryCreate(baseUri, url, out var resolvedUri)
            ? resolvedUri.AbsoluteUri
            : null;
    }

    private static int? ExtractPlayerCount(IElement item, string downloadUrl, string sourceUrl)
    {
        foreach (var value in new[] { downloadUrl, item.TextContent, sourceUrl })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var match = PlayerCountFromDownloadIdRegex().Match(value);
            if (!match.Success)
            {
                match = PlayerCountFromTextRegex().Match(value);
            }

            if (!match.Success)
            {
                match = PlayerCountFromPageUrlRegex().Match(value);
            }

            if (match.Success && int.TryParse(match.Groups["players"].Value, out var playerCount))
            {
                return playerCount;
            }
        }

        return null;
    }

    private static string? InferCategoryFromUrl(string sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl) || !Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var path = uri.AbsolutePath;
        if (ContainsAny(path, "/AOA/", "aoamaps"))
        {
            return AODMapsConstants.CategoryAoa;
        }

        if (ContainsAny(path, "/race/", "racemaps"))
        {
            return AODMapsConstants.CategoryRace;
        }

        if (ContainsAny(path, "/air/", "airmaps"))
        {
            return AODMapsConstants.CategoryAir;
        }

        if (ContainsAny(path, "/ContraAOD/", "contraaod"))
        {
            return AODMapsConstants.CategoryContra;
        }

        if (path.Contains("/compstomp/", StringComparison.OrdinalIgnoreCase))
        {
            return AODMapsConstants.CategoryCompstomp;
        }

        if (ContainsAny(path, "/packs/", "Map_Packs"))
        {
            return AODMapsConstants.CategoryMapPacks;
        }

        return null;
    }

    private static bool ContainsAny(string source, string text1, string text2) =>
        source.Contains(text1, StringComparison.OrdinalIgnoreCase) ||
        source.Contains(text2, StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        var trimmed = category.Trim();
        if (trimmed.Equals("Contra AOD", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals(AODMapsConstants.CategoryContra, StringComparison.OrdinalIgnoreCase))
        {
            return AODMapsConstants.CategoryContra;
        }

        if (trimmed.Equals(AODMapsConstants.CategoryCompstomp, StringComparison.OrdinalIgnoreCase))
        {
            return AODMapsConstants.CategoryCompstomp;
        }

        if (trimmed.Equals(AODMapsConstants.CategoryMapPacks, StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("MapPacks", StringComparison.OrdinalIgnoreCase))
        {
            return AODMapsConstants.CategoryMapPacks;
        }

        if (trimmed.Equals(AODMapsConstants.CategoryAir, StringComparison.OrdinalIgnoreCase))
        {
            return AODMapsConstants.CategoryAir;
        }

        if (trimmed.Equals(AODMapsConstants.CategoryRace, StringComparison.OrdinalIgnoreCase))
        {
            return AODMapsConstants.CategoryRace;
        }

        if (trimmed.Equals(AODMapsConstants.CategoryAoa, StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Art of Attack", StringComparison.OrdinalIgnoreCase))
        {
            return AODMapsConstants.CategoryAoa;
        }

        return trimmed;
    }

    private static int? ParsePlayerCountFilter(string? playerCountFilter)
    {
        if (string.IsNullOrWhiteSpace(playerCountFilter))
        {
            return null;
        }

        var numPart = playerCountFilter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        return int.TryParse(numPart, out var count) ? count : null;
    }

    private static bool MatchesPlayerCountFilter(ContentSearchResult item, int? expectedPlayerCount)
    {
        if (!expectedPlayerCount.HasValue)
        {
            return true;
        }

        if (!item.ResolverMetadata.TryGetValue(AODMapsConstants.PlayerCountMetadataKey, out var raw) &&
            !item.Metadata.TryGetValue(AODMapsConstants.PlayerCountMetadataKey, out raw))
        {
            return false;
        }

        return int.TryParse(raw, out var actual) && actual == expectedPlayerCount.Value;
    }

    private static void ApplyDiscoveryBadges(ContentSearchResult result, int? playerCount, string sourceUrl)
    {
        if (playerCount.HasValue)
        {
            ContentCardBadgeHelper.ApplyPlayerCount(result, playerCount.Value);
        }

        ContentCardBadgeHelper.ApplyCategory(result, InferCategoryFromUrl(sourceUrl));
    }

    private static string BuildMapDescription(string? title, int? playerCount, string sourceUrl, string? author)
    {
        return AODMapsHelper.BuildRichMapDescription(title, playerCount, InferCategoryFromUrl(sourceUrl), author);
    }

    /// <summary>
    /// Parses a gallery item element into a ContentSearchResult.
    /// </summary>
    /// <param name="item">The HTML element representing a gallery item.</param>
    /// <param name="sourceUrl">The source URL where this item was found.</param>
    /// <returns>A ContentSearchResult if parsing succeeds, otherwise null.</returns>
    private static ContentSearchResult? ParseGalleryItem(IElement item, string sourceUrl)
    {
        // Name
        var nameEl = item.QuerySelector(AODMapsConstants.GalleryMapNameSelector);
        var name = nameEl?.TextContent?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        // Download URL
        var linkEl = item.QuerySelector(AODMapsConstants.GalleryDownloadLinkSelector);
        var downloadUrl = linkEl?.GetAttribute(AODMapsConstants.HrefAttribute);
        if (string.IsNullOrEmpty(downloadUrl))
        {
            return null;
        }

        downloadUrl = MakeAbsoluteUrl(downloadUrl, sourceUrl);

        // Thumbnail
        var imgEl = item.QuerySelector(AODMapsConstants.GalleryThumbnailSelector);
        var thumbnailUrl = imgEl?.GetAttribute(AODMapsConstants.SrcAttribute);
        thumbnailUrl = MakeAbsoluteUrl(thumbnailUrl, sourceUrl);

        // Downloads (parsed from script or text)
        // Simply store it in metadata if needed for sorting?
        // We really need it for the Manifest, but Discoverer just finds.
        string safeDownloadUrl = downloadUrl ?? string.Empty;
        string safeHashCode = ComputeStableHash(safeDownloadUrl);
        var playerCount = ExtractPlayerCount(item, safeDownloadUrl, sourceUrl);
        var author = AODMapsHelper.ExtractAuthor(name, sourceUrl) ?? AODMapsConstants.DefaultAuthorName;
        var description = BuildMapDescription(name, playerCount, sourceUrl, author);

        var result = new ContentSearchResult
        {
            Id = safeHashCode,
            Name = name,
            Description = description,
            AuthorName = author,
            Version = string.Empty,
            ProviderName = AODMapsConstants.DiscovererSourceName,
            SourceUrl = safeDownloadUrl, // Use download URL as source URL for unique identification
            IconUrl = thumbnailUrl ?? PublisherInfoConstants.AODMaps.LogoSource,
            ContentType = ContentType.Map,
            TargetGame = GameType.ZeroHour,
            RequiresResolution = true,
            ResolverId = AODMapsConstants.ResolverId,
            LastUpdated = null,
            ResolverMetadata =
            {
                { AODMapsConstants.DownloadUrlMetadataKey, safeDownloadUrl },
                { AODMapsConstants.MapIdMetadataKey, safeHashCode },
                { AODMapsConstants.ContentIdMetadataKey, safeHashCode },
                { AODMapsConstants.IconUrlMetadataKey, thumbnailUrl ?? string.Empty },
                { AODMapsConstants.ListPageUrlMetadataKey, sourceUrl },
            },
        };

        result.Tags.Add("AODMaps");
        if (!string.IsNullOrWhiteSpace(author) && !author.Equals(AODMapsConstants.DefaultAuthorName, StringComparison.OrdinalIgnoreCase))
        {
            result.Tags.Add($"author:{author.ToLowerInvariant()}");
        }

        ApplyDiscoveryBadges(result, playerCount, sourceUrl);

        return result;
    }

    /// <summary>
    /// Parses a map maker item element into a ContentSearchResult.
    /// </summary>
    /// <param name="content">The HTML element representing a map maker item.</param>
    /// <param name="sourceUrl">The source URL where this item was found.</param>
    /// <returns>A ContentSearchResult if parsing succeeds, otherwise null.</returns>
    private static ContentSearchResult? ParseMapMakerItem(IElement content, string sourceUrl)
    {
        // Title: <h1>- AOD rebel uprising</h1>
        var titleEl = content.QuerySelector(AODMapsConstants.MapMakerTitleSelector);
        var title = titleEl?.TextContent?.Trim().TrimStart('-').Trim() ?? "Unknown Map";

        // Download: <a href="..." download>
        var downloadEl = content.QuerySelector(AODMapsConstants.MapMakerDownloadSelector);
        var downloadUrl = downloadEl?.GetAttribute(AODMapsConstants.HrefAttribute);
        if (string.IsNullOrEmpty(downloadUrl))
        {
             // Try standard click php link if download attribute missing
             downloadEl = content.QuerySelector("a[href*='ccount/click.php']");
             downloadUrl = downloadEl?.GetAttribute("href");
        }

        if (string.IsNullOrEmpty(downloadUrl))
        {
            return null;
        }

        downloadUrl = MakeAbsoluteUrl(downloadUrl, sourceUrl);

        // Image
        var imgEl = content.QuerySelector(AODMapsConstants.MapMakerImageSelector);
        var thumbnailUrl = imgEl?.GetAttribute(AODMapsConstants.SrcAttribute);
        thumbnailUrl = MakeAbsoluteUrl(thumbnailUrl, sourceUrl);

        string safeDownloadUrl = downloadUrl ?? string.Empty;
        string safeHashCode = ComputeStableHash(safeDownloadUrl);
        var playerCount = ExtractPlayerCount(content, safeDownloadUrl, sourceUrl);
        var author = AODMapsHelper.ExtractAuthor(title, sourceUrl) ?? "MapMaker";
        var description = AODMapsHelper.ExtractMapMakerDescription(content, playerCount, InferCategoryFromUrl(sourceUrl), author);

        var result = new ContentSearchResult
        {
            Id = safeHashCode,
            Name = title,
            Description = description,
            AuthorName = author,
            Version = string.Empty,
            ProviderName = AODMapsConstants.DiscovererSourceName,
            SourceUrl = safeDownloadUrl, // Use download URL as source URL for unique identification
            IconUrl = thumbnailUrl ?? PublisherInfoConstants.AODMaps.LogoSource,
            ContentType = ContentType.Map,
            TargetGame = GameType.ZeroHour,
            RequiresResolution = true,
            ResolverId = AODMapsConstants.ResolverId,
            LastUpdated = null,
            ResolverMetadata =
            {
                { AODMapsConstants.DownloadUrlMetadataKey, safeDownloadUrl },
                { AODMapsConstants.MapIdMetadataKey, safeHashCode },
                { AODMapsConstants.ContentIdMetadataKey, safeHashCode },
                { AODMapsConstants.IconUrlMetadataKey, thumbnailUrl ?? string.Empty },
                { AODMapsConstants.ListPageUrlMetadataKey, sourceUrl },
            },
        };

        result.Tags.Add("AODMaps");
        if (!string.IsNullOrWhiteSpace(author) && !author.Equals(AODMapsConstants.DefaultAuthorName, StringComparison.OrdinalIgnoreCase) && !author.Equals("MapMaker", StringComparison.OrdinalIgnoreCase))
        {
            result.Tags.Add($"author:{author.ToLowerInvariant()}");
        }

        ApplyDiscoveryBadges(result, playerCount, sourceUrl);

        return result;
    }

    /// <summary>
    /// Computes a stable hash from the input string for use as a content identifier.
    /// </summary>
    /// <param name="input">The input string to hash.</param>
    /// <returns>A hexadecimal string representation of the hash.</returns>
    private static string ComputeStableHash(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return "0";
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var builder = new StringBuilder();
        foreach (var b in bytes)
        {
            builder.Append(b.ToString("x2"));
        }

        return builder.ToString();
    }

    private static void ExtractGalleryItems(IDocument document, string sourceUrl, List<ContentSearchResult> results)
    {
        var galleryItems = document.QuerySelectorAll(AODMapsConstants.GalleryItemSelector);
        foreach (var item in galleryItems)
        {
            var result = ParseGalleryItem(item, sourceUrl);
            if (result != null)
            {
                results.Add(result);
            }
        }
    }

    private static void ExtractMapMakerItems(IDocument document, string sourceUrl, List<ContentSearchResult> results)
    {
        var mmItems = document.QuerySelectorAll(AODMapsConstants.MapMakerContainerSelector);
        foreach (var item in mmItems)
        {
            var contentDiv = item.QuerySelector(AODMapsConstants.MapMakerContentSelector);
            if (contentDiv != null)
            {
                var result = ParseMapMakerItem(contentDiv, sourceUrl);
                if (result != null)
                {
                    results.Add(result);
                }
            }
        }
    }

    private static bool HasNextTextLink(IDocument document, out string? href)
    {
        href = null;
        var nextLink = document.QuerySelectorAll("a").FirstOrDefault(a =>
            a.TextContent != null &&
            a.TextContent.Trim().Equals("Next", StringComparison.OrdinalIgnoreCase));

        if (nextLink == null)
        {
            return false;
        }

        href = nextLink.GetAttribute("href");
        return !string.IsNullOrWhiteSpace(href) &&
               !href.Equals("#", StringComparison.Ordinal) &&
               !href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHigherPageLink(IElement a, int currentPage)
    {
        var href = a.GetAttribute("href");
        var text = a.TextContent?.Trim();

        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(href))
        {
            return false;
        }

        if (href.Contains("new") || href.Contains("players") || href.Contains("compstomp") || href.Contains("Map_Packs"))
        {
            var match = HtmlPageNumberRegex().Match(href);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var pageNum) && pageNum > currentPage)
            {
                return true;
            }

            if (int.TryParse(text, out var textPageNum) && textPageNum > currentPage)
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public string SourceName => AODMapsConstants.DiscovererSourceName;

    /// <inheritdoc />
    public string Description => AODMapsConstants.DiscovererDescription;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresDiscovery;

    /// <inheritdoc />
    public async Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Allow discovery if there is a search term OR if it's a browsing query (game/content type set)
            // If neither, return empty but success (or failure if strict)
            if (query is null)
            {
               return OperationResult<ContentDiscoveryResult>.CreateFailure("Query cannot be null");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // The AODMaps site paginates irregularly (e.g. NEW/new.html has 3 items while
            // NEW/new2.html holds the remaining archive), so site pages cannot be mapped 1:1
            // onto UI pages. Fetch site pages sequentially, de-duplicate, and slice the
            // aggregate list by the query's paging parameters instead.
            int uiPage = query.Page ?? 1;
            int take = query.Take > 0 ? query.Take : 24;
            int skip = (uiPage - 1) * take;

            var collected = new List<ContentSearchResult>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            const int maxSitePages = 30; // safety bound against runaway loops
            int sitePage = 1;
            var expectedPlayerCount = ParsePlayerCountFilter(query.AODMapsPlayerCount);

            using var client = httpClientFactory.CreateClient("AODMaps");
            if (client.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            }

            var context = BrowsingContext.New(Configuration.Default);

            // Fetch one item past the requested window so HasMoreItems is exact.
            // Player-count filtering is applied while collecting so combined category+players
            // queries still fill a full UI page.
            while (sitePage <= maxSitePages && collected.Count < skip + take + 1)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var url = BuildDiscoveryUrl(query, sitePage);
                logger.LogInformation("Discovering AODMaps content from: {Url} (site page {SitePage})", url, sitePage);

                string html = string.Empty;
                try
                {
                    html = await client.GetStringAsync(url, cancellationToken);
                }
                catch (HttpRequestException ex)
                {
                    // 404 or similar: we ran past the last site page — not an error.
                    logger.LogInformation("AODMaps site page {SitePage} not available ({Message}); stopping pagination", sitePage, ex.Message);
                    break;
                }

                var document = await context.OpenAsync(req => req.Content(html), cancellationToken);
                var (items, _) = ExtractItems(document, url, sitePage);

                int before = collected.Count;
                int rawAccepted = 0;
                foreach (var item in items)
                {
                    if (!seenIds.Add(item.Id ?? string.Empty))
                    {
                        continue;
                    }

                    rawAccepted++;
                    if (!MatchesPlayerCountFilter(item, expectedPlayerCount))
                    {
                        continue;
                    }

                    collected.Add(item);
                }

                if (rawAccepted == 0)
                {
                    // The page produced nothing new (unpaginated category URLs return the same
                    // content for every suffix) — stop.
                    break;
                }

                // If every unique item was filtered out by player count, keep paging when the site
                // still has more pages; otherwise stop to avoid empty spinning.
                if (collected.Count == before && expectedPlayerCount.HasValue)
                {
                    sitePage++;
                    continue;
                }

                if (collected.Count == before)
                {
                    break;
                }

                sitePage++;
            }

            var pageItems = collected.Skip(skip).Take(take).ToList();
            bool hasMoreItems = collected.Count > skip + take;

            logger.LogInformation(
                "Discovered {Count} AODMaps items for UI page {Page} (aggregate {Total}, HasMore: {HasMore})",
                pageItems.Count,
                uiPage,
                collected.Count,
                hasMoreItems);

            return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = pageItems,
                HasMoreItems = hasMoreItems,
            });
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("AODMaps discovery was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, AODMapsConstants.DiscoveryFailureLogMessage);
            return OperationResult<ContentDiscoveryResult>.CreateFailure(
                string.Format(AODMapsConstants.DiscoveryFailedErrorTemplate, ex.Message));
        }
    }

    /// <summary>
    /// Builds the discovery URL for a specific site page based on the provided query filters.
    /// </summary>
    /// <param name="query">The content search query containing filter criteria.</param>
    /// <param name="sitePage">The AODMaps site page number to build the URL for.</param>
    /// <returns>The constructed URL for discovering AODMaps content.</returns>
    private static string BuildDiscoveryUrl(ContentSearchQuery query, int sitePage)
    {
        string suffix = sitePage > 1 ? sitePage.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

        // Category pages and player-count pages are separate axes on aodmaps.com.
        // Prefer category when set; player count is applied as a post-fetch filter so the UI can
        // AND both dimensions (for example AOA + 4 players).
        var category = NormalizeCategory(query.AODMapsCategory);
        if (!string.IsNullOrEmpty(category))
        {
            return category switch
            {
                AODMapsConstants.CategoryCompstomp => string.Format(AODMapsConstants.CompstompPagePattern, suffix),
                AODMapsConstants.CategoryMapPacks => string.Format(AODMapsConstants.MapPacksPagePattern, suffix),
                AODMapsConstants.CategoryAir => AODMapsConstants.AirMapsUrl,
                AODMapsConstants.CategoryRace => AODMapsConstants.RaceMapsUrl,
                AODMapsConstants.CategoryAoa => AODMapsConstants.AoaMapsUrl,
                AODMapsConstants.CategoryContra => AODMapsConstants.ContraAodUrl,
                _ => string.Format(AODMapsConstants.NewMapsPagePattern, suffix),
            };
        }

        var expectedPlayerCount = ParsePlayerCountFilter(query.AODMapsPlayerCount);
        if (expectedPlayerCount.HasValue)
        {
            return string.Format(
                AODMapsConstants.PlayerPagePattern,
                expectedPlayerCount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                suffix);
        }

        // Priority 3: Check AODMaps-specific map type filter (future enhancement)
        if (!string.IsNullOrEmpty(query.AODMapsMapType))
        {
            // Map type URLs would go here when implemented
            // e.g., "1v1" → /1v1-maps, "2v2" → /2v2-maps, "FFA" → /ffa-maps
        }

        // Priority 4: Check Content Type
        if (query.ContentType == ContentType.MapPack)
        {
            return string.Format(AODMapsConstants.MapPacksPagePattern, suffix);
        }

        // Default: New Maps (Last Uploaded)
        return string.Format(AODMapsConstants.NewMapsPagePattern, suffix);
    }

    /// <summary>
    /// Extracts content items from the parsed HTML document.
    /// </summary>
    /// <param name="document">The parsed HTML document.</param>
    /// <param name="sourceUrl">The source URL of the document.</param>
    /// <param name="currentPage">The current page number.</param>
    /// <returns>A tuple containing the list of extracted items and a flag indicating if more items are available.</returns>
    private (List<ContentSearchResult> Items, bool HasMoreItems) ExtractItems(IDocument document, string sourceUrl, int currentPage)
    {
        var results = new List<ContentSearchResult>();

        ExtractGalleryItems(document, sourceUrl, results);
        ExtractMapMakerItems(document, sourceUrl, results);

        // Check for next page indicator to support progressive loading
        bool hasMoreItems = CheckForNextPage(document, currentPage);
        return (results, hasMoreItems);
    }

    /// <summary>
    /// Checks if there is a next page available in the pagination.
    /// </summary>
    /// <param name="document">The parsed HTML document.</param>
    /// <param name="currentPage">The current page number.</param>
    /// <returns>True if a next page is available, otherwise false.</returns>
    private bool CheckForNextPage(IDocument document, int currentPage)
    {
        if (HasNextTextLink(document, out var nextHref))
        {
            logger.LogInformation("[AODMaps] Found 'Next' link: {Url}", nextHref);
            return true;
        }

        var higherPageCount = document.QuerySelectorAll("a").Count(a => IsHigherPageLink(a, currentPage));
        if (higherPageCount > 0)
        {
            logger.LogInformation("[AODMaps] Found {Count} pagination links to higher pages", higherPageCount);
            return true;
        }

        var nextPagePattern = currentPage > 1
            ? $"new{currentPage + 1}.html"
            : "new2.html";

        var directNextLink = document.QuerySelectorAll("a").FirstOrDefault(a =>
            a.GetAttribute("href")?.Contains(nextPagePattern) == true);

        if (directNextLink != null)
        {
            logger.LogInformation("[AODMaps] Found direct next page link: {Url}", directNextLink.GetAttribute("href"));
            return true;
        }

        logger.LogInformation("[AODMaps] No next page link found on page {Page}", currentPage);
        return false;
    }
}
