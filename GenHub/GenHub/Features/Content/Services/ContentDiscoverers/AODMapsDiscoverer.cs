using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
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

    [GeneratedRegex(@"(?:^|[?&=])(?<players>[1-8])P(?:[_&]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex PlayerCountFromDownloadIdRegex();

    [GeneratedRegex(@"\b(?<players>[1-8])\s*(?:players?|p)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PlayerCountFromTextRegex();

    [GeneratedRegex(@"/(?<players>[1-8])_players", RegexOptions.IgnoreCase)]
    private static partial Regex PlayerCountFromPageUrlRegex();

    /// <inheritdoc />
    public string SourceName => AODMapsConstants.DiscovererSourceName;

    /// <inheritdoc />
    public string Description => AODMapsConstants.DiscovererDescription;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresDiscovery;

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Critical Code Smell", "S3776:Cognitive Complexity of methods should not be too high", Justification = "AODMaps discovery iterates irregular legacy HTML pages and handles custom pagination.")]
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

            using var client = httpClientFactory.CreateClient(AODMapsConstants.PublisherType);
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
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    // 404: we ran past the last site page — not an error.
                    logger.LogInformation(ex, "AODMaps site page {SitePage} not available ({Message}); stopping pagination", sitePage, ex.Message);
                    break;
                }

                var document = await context.OpenAsync(req => req.Content(html), cancellationToken);
                var items = ExtractItems(document, url);

                int rawAccepted = 0;
                foreach (var item in items)
                {
                    if (!seenIds.Add(item.Id ?? string.Empty))
                    {
                        continue;
                    }

                    rawAccepted++;
                    if (!MatchesSearchTerm(item, query.SearchTerm))
                    {
                        continue;
                    }

                    if (MatchesPlayerCountFilter(item, expectedPlayerCount))
                    {
                        collected.Add(item);
                    }
                }

                // If this site page had no new unique items at all, we reached the end.
                if (rawAccepted == 0)
                {
                    break;
                }

                sitePage++;
            }

            bool hasMore = collected.Count > skip + take;
            var pageItems = collected.Skip(skip).Take(take).ToList();

            logger.LogInformation(
                "AODMaps discovery finished: collected {Collected}, returning page {Page} with {Count} items (hasMore={HasMore})",
                collected.Count,
                uiPage,
                pageItems.Count,
                hasMore);

            return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = pageItems,
                HasMoreItems = hasMore,
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to discover AODMaps content");
            return OperationResult<ContentDiscoveryResult>.CreateFailure($"AODMaps discovery failed: {ex.Message}");
        }
    }

    private static int? ExtractPlayerCountFromDownloadId(string? downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return null;
        }

        var match = PlayerCountFromDownloadIdRegex().Match(downloadUrl);
        return match.Success && int.TryParse(match.Groups["players"].Value, out var players)
            ? players
            : null;
    }

    private static int? ExtractPlayerCountFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = PlayerCountFromTextRegex().Match(text);
        return match.Success && int.TryParse(match.Groups["players"].Value, out var players)
            ? players
            : null;
    }

    private static int? ExtractPlayerCountFromPageUrl(string? pageUrl)
    {
        if (string.IsNullOrWhiteSpace(pageUrl))
        {
            return null;
        }

        var match = PlayerCountFromPageUrlRegex().Match(pageUrl);
        return match.Success && int.TryParse(match.Groups["players"].Value, out var players)
            ? players
            : null;
    }

    private static int? ExtractPlayerCount(IElement? element, string? downloadUrl, string? pageUrl)
    {
        var fromId = ExtractPlayerCountFromDownloadId(downloadUrl);
        if (fromId.HasValue)
        {
            return fromId;
        }

        var text = element?.TextContent;
        var fromText = ExtractPlayerCountFromText(text);
        if (fromText.HasValue)
        {
            return fromText;
        }

        return ExtractPlayerCountFromPageUrl(pageUrl);
    }

    private static string? InferCategoryFromUrl(string? pageUrl)
    {
        if (string.IsNullOrWhiteSpace(pageUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri))
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

    private static bool MatchesSearchTerm(ContentSearchResult item, string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return true;
        }

        return (item.Name?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true)
            || string.Equals(item.Id, searchTerm, StringComparison.OrdinalIgnoreCase);
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

        string safeDownloadUrl = downloadUrl ?? string.Empty;
        string safeHashCode = ComputeStableHash(safeDownloadUrl);
        var playerCount = ExtractPlayerCount(item, safeDownloadUrl, sourceUrl);
        var author = AODMapsHelper.ExtractAuthor(name, sourceUrl) ?? AODMapsConstants.DefaultAuthorName;
        var description = BuildMapDescription(name, playerCount, sourceUrl, author);

        var result = CreateAODMapSearchResult(
            safeHashCode,
            name,
            description,
            author,
            safeDownloadUrl,
            thumbnailUrl,
            sourceUrl);

        result.Tags.Add("AODMaps");
        if (!string.IsNullOrWhiteSpace(author) && !author.Equals(AODMapsConstants.DefaultAuthorName, StringComparison.OrdinalIgnoreCase))
        {
            result.Tags.Add($"author:{author.ToLowerInvariant()}");
        }

        ApplyDiscoveryBadges(result, playerCount, sourceUrl);

        return result;
    }

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
        var author = AODMapsHelper.ExtractAuthor(title, sourceUrl) ?? AODMapsConstants.DefaultAuthorName;
        var description = AODMapsHelper.ExtractMapMakerDescription(content, playerCount, InferCategoryFromUrl(sourceUrl), author);

        var result = CreateAODMapSearchResult(
            safeHashCode,
            title,
            description,
            author,
            safeDownloadUrl,
            thumbnailUrl,
            sourceUrl);

        result.Tags.Add("AODMaps");
        if (!string.IsNullOrWhiteSpace(author) && !author.Equals(AODMapsConstants.DefaultAuthorName, StringComparison.OrdinalIgnoreCase))
        {
            result.Tags.Add($"author:{author.ToLowerInvariant()}");
        }

        ApplyDiscoveryBadges(result, playerCount, sourceUrl);

        return result;
    }

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

    private static ContentSearchResult CreateAODMapSearchResult(
        string id,
        string name,
        string description,
        string author,
        string safeDownloadUrl,
        string? thumbnailUrl,
        string sourceUrl)
    {
        return new ContentSearchResult
        {
            Id = id,
            Name = name,
            Description = description,
            AuthorName = author,
            Version = string.Empty,
            ProviderName = AODMapsConstants.DiscovererSourceName,
            SourceUrl = safeDownloadUrl,
            IconUrl = thumbnailUrl ?? PublisherInfoConstants.AODMaps.LogoSource,
            ContentType = ContentType.Map,
            TargetGame = GameType.ZeroHour,
            RequiresResolution = true,
            ResolverId = AODMapsConstants.ResolverId,
            LastUpdated = null,
            ResolverMetadata =
            {
                { AODMapsConstants.DownloadUrlMetadataKey, safeDownloadUrl },
                { AODMapsConstants.MapIdMetadataKey, id },
                { AODMapsConstants.ContentIdMetadataKey, id },
                { AODMapsConstants.IconUrlMetadataKey, thumbnailUrl ?? string.Empty },
                { AODMapsConstants.ListPageUrlMetadataKey, sourceUrl },
            },
        };
    }

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

    private static List<ContentSearchResult> ExtractItems(IDocument document, string sourceUrl)
    {
        var results = new List<ContentSearchResult>();

        ExtractGalleryItems(document, sourceUrl, results);
        ExtractMapMakerItems(document, sourceUrl, results);

        return results;
    }

    private static string? MakeAbsoluteUrl(string? url, string sourceUrl)
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        // PashaCNC links - they are dead, replace with current domain
        if (url.Contains("pashacnc.com", StringComparison.OrdinalIgnoreCase))
        {
            url = url.Replace("pashacnc.com", "aodmaps.com", StringComparison.OrdinalIgnoreCase);
            url = url.Replace("www.pashacnc.com", "aodmaps.com", StringComparison.OrdinalIgnoreCase);
        }

        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        try
        {
            var baseUri = new Uri(sourceUrl);
            return new Uri(baseUri, url).ToString();
        }
        catch
        {
            return $"{AODMapsConstants.BaseUrl.TrimEnd('/')}/{url.TrimStart('/')}";
        }
    }
}
