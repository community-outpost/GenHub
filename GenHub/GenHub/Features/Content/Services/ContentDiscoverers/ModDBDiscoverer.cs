using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.ModDB;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// Discovers content from ModDB website using Playwright to bypass WAF/Bot protections.
/// </summary>
public partial class ModDBDiscoverer(
    ILogger<ModDBDiscoverer> logger,
    IPlaywrightService playwrightService,
    IHttpClientFactory httpClientFactory) : IContentDiscoverer
{
    /// <inheritdoc />
    public string SourceName => ModDBConstants.DiscovererSourceName;

    /// <inheritdoc />
    public string Description => ModDBConstants.DiscovererDescription;

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
            var gameType = query.TargetGame ?? GameType.ZeroHour;
            logger.LogInformation("Discovering ModDB content for {Game} using Playwright", gameType);

            List<ContentSearchResult> results = [];
            bool hasMoreItems = false;
            bool keepPageOpenForVerification = false;
            bool challengeDetected = false;

            // Determine which sections to search based on query filters
            var sectionsToSearch = DetermineSectionsToSearch(query);

            IPage? page = null;
            try
            {
                page = await playwrightService.CreatePersistentPageAsync(ModDBConstants.BrowserProfileName, cancellationToken);

                foreach (var section in sectionsToSearch)
                {
                    var (sectionResults, sectionHasMore, sectionKeepOpen, sectionChallenge) = await DiscoverFromSectionAsync(page, section, gameType, query, cancellationToken);
                    results.AddRange(sectionResults);
                    if (sectionHasMore)
                    {
                        hasMoreItems = true;
                    }

                    if (sectionKeepOpen)
                    {
                        keepPageOpenForVerification = true;
                    }

                    if (sectionChallenge)
                    {
                        challengeDetected = true;
                    }
                }
            }
            finally
            {
                if (page != null)
                {
                    await playwrightService.ClosePersistentPageAsync(page, keepPageOpenForVerification);
                }
            }

            var orderedResults = OrderDiscoveredResults(results, query);

            logger.LogInformation(
                "Discovered {Count} ModDB items across {Sections} sections",
                orderedResults.Count,
                sectionsToSearch.Count);

            return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = orderedResults,
                HasMoreItems = hasMoreItems,
                ChallengeDetected = challengeDetected,
            });
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "ModDB discovery cancelled");
            return OperationResult<ContentDiscoveryResult>.CreateFailure(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to discover ModDB content");
            return OperationResult<ContentDiscoveryResult>.CreateFailure($"Discovery failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts the ModDB identifier slug from a ModDB URL.
    /// </summary>
    /// <param name="url">The ModDB page or download URL.</param>
    /// <returns>The extracted slug identifier or a generated fallback GUID string.</returns>
    internal static string ExtractModDBIdFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);

            // http://.../mods/contra
            // http://.../downloads/contra-009
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length > 0 ? segments[^1] : Guid.NewGuid().ToString();
        }
        catch
        {
            return Guid.NewGuid().ToString();
        }
    }

    private static List<ContentSearchResult> OrderDiscoveredResults(List<ContentSearchResult> results, ContentSearchQuery query)
    {
        var sortParam = query.Sort;
        if (string.Equals(sortParam, ModDBConstants.SortNameAsc, StringComparison.OrdinalIgnoreCase) ||
            query.SortOrder == ContentSortField.Name)
        {
            return results.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        if (string.Equals(sortParam, ModDBConstants.SortNameDesc, StringComparison.OrdinalIgnoreCase))
        {
            return results.OrderByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        if (string.Equals(sortParam, ModDBConstants.SortDateAsc, StringComparison.OrdinalIgnoreCase))
        {
            return results.OrderBy(r => r.LastUpdated ?? DateTime.MaxValue).ToList();
        }

        // Default: newest first (date-desc)
        return results.OrderByDescending(r => r.LastUpdated ?? DateTime.MinValue).ToList();
    }

    private static List<string> DetermineSectionsToSearch(ContentSearchQuery query)
    {
        // Use explicit section from query if provided
        if (!string.IsNullOrEmpty(query.ModDBSection))
        {
            return [query.ModDBSection];
        }

        // Map ContentType to section if possible
        if (query.ContentType.HasValue)
        {
            return query.ContentType.Value switch
            {
                ContentType.Mod or ContentType.Patch or ContentType.Video => ["downloads"],
                ContentType.Map or ContentType.Skin or ContentType.LanguagePack => ["addons"],
                _ => ["downloads", "addons"],
            };
        }

        // Default: browse both sections so the grid has more content.
        return ["downloads", "addons"];
    }

    private static ModDBFilter BuildFilterFromQuery(ContentSearchQuery query)
    {
        var filter = new ModDBFilter
        {
            Keyword = query.SearchTerm,
            Page = query.Page ?? 1,
            Sort = ResolveSort(query),
        };

        // Apply Category filter (for downloads section)
        if (!string.IsNullOrWhiteSpace(query.ModDBCategory))
        {
            filter.Category = query.ModDBCategory;
        }

        // Apply AddonCategory filter (for categoryaddon param)
        if (!string.IsNullOrWhiteSpace(query.ModDBAddonCategory))
        {
            filter.AddonCategory = query.ModDBAddonCategory;
        }

        // Apply License filter
        if (!string.IsNullOrWhiteSpace(query.ModDBLicense))
        {
            filter.Licence = query.ModDBLicense;
        }

        // Apply Timeframe filter
        if (!string.IsNullOrWhiteSpace(query.ModDBTimeframe))
        {
            filter.Timeframe = query.ModDBTimeframe;
        }

        return filter;
    }

    private static string ResolveSort(ContentSearchQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Sort))
        {
            return query.Sort;
        }

        return query.SortOrder switch
        {
            ContentSortField.Name => ModDBConstants.SortNameAsc,
            ContentSortField.DownloadCount => ModDBConstants.SortVisitDesc,
            ContentSortField.Rating => ModDBConstants.SortRatingDesc,
            ContentSortField.DateCreated => ModDBConstants.SortDateDesc,
            _ => ModDBConstants.DefaultSort,
        };
    }

    private static string? MapContentTypeToCategory(ContentType contentType, string section)
    {
        if (section == "downloads")
        {
            return contentType switch
            {
                ContentType.Mod => ModDBConstants.CategoryFullVersion,
                ContentType.Patch => ModDBConstants.CategoryPatch,
                ContentType.Video => ModDBConstants.CategoryMovie,
                ContentType.ModdingTool => ModDBConstants.CategoryMappingTool,
                ContentType.LanguagePack => ModDBConstants.CategoryLanguagePack,
                _ => null,
            };
        }

        if (section == "addons")
        {
            return contentType switch
            {
                ContentType.Map => ModDBConstants.AddonMultiplayerMap,
                ContentType.Skin => ModDBConstants.AddonPlayerSkin,
                ContentType.LanguagePack => ModDBConstants.AddonLanguageSounds,
                _ => null,
            };
        }

        return null;
    }

    private static ContentSearchResult? ParseContentItem(AngleSharp.Dom.IElement item, GameType gameType, string section)
    {
        var titleAndUrl = ExtractTitleAndDetailUrl(item);
        if (titleAndUrl == null) return null;

        var (title, detailUrl) = titleAndUrl.Value;
        var author = ExtractAuthor(item);
        var iconUrl = ExtractIconUrl(item);

        var descEl = item.QuerySelector("p, div.summary, span.summary, td.content.name span.summary");
        var description = HtmlTextHelper.NormalizeHtml(descEl?.TextContent?.Trim());

        var categoryEl = item.QuerySelector(".category, .type, span.category");
        var category = categoryEl?.TextContent?.Trim();

        var lastUpdated = ExtractLastUpdatedDate(item);
        var contentType = DetermineContentType(section, category, detailUrl);
        var moddbId = ExtractModDBIdFromUrl(detailUrl);
        var prospectiveId = lastUpdated.HasValue && lastUpdated.Value > DateTime.MinValue
            ? ManifestIdGenerator.GeneratePublisherContentId(ModDBConstants.PublisherPrefix, contentType, title, lastUpdated.Value)
            : ManifestIdGenerator.GeneratePublisherContentId(ModDBConstants.PublisherPrefix, contentType, title, 0);

        var result = new ContentSearchResult
        {
            Id = prospectiveId,
            Name = title,
            Description = description,
            AuthorName = author,
            ContentType = contentType,
            TargetGame = gameType,
            ProviderName = ModDBConstants.DiscovererSourceName,
            IconUrl = iconUrl,
            RequiresResolution = true,
            ResolverId = ModDBConstants.ResolverId,
            SourceUrl = detailUrl,
            LastUpdated = lastUpdated,
        };

        result.ResolverMetadata[ModDBConstants.ContentIdMetadataKey] = moddbId;
        result.ResolverMetadata[ModDBConstants.SectionMetadataKey] = section;
        ContentCardBadgeHelper.ApplyCategory(result, category);

        ApplyParentModMetadata(result, detailUrl);

        return result;
    }

    private static (string Title, string DetailUrl)? ExtractTitleAndDetailUrl(AngleSharp.Dom.IElement item)
    {
        var titleLink = item.QuerySelector("h4 a, h3 a, a.title") ?? item.QuerySelector("td.content.name a");
        if (titleLink == null) return null;

        var title = titleLink.TextContent?.Trim();
        var href = titleLink.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(href)) return null;

        if (!href.Contains("/mods/") && !href.Contains("/downloads/") && !href.Contains("/addons/")) return null;

        var detailUrl = href.StartsWith("http") ? href : ModDBConstants.BaseUrl + href;
        return (title, detailUrl);
    }

    private static string ExtractAuthor(AngleSharp.Dom.IElement item)
    {
        var authorLink = item.QuerySelector("a[href*='/members/']") ??
                        item.QuerySelector("span.by a") ??
                        item.QuerySelector("span.author a");
        var author = authorLink?.TextContent?.Trim();
        return string.IsNullOrWhiteSpace(author) ? "Unknown" : author;
    }

    private static string ExtractIconUrl(AngleSharp.Dom.IElement item)
    {
        var img = item.QuerySelector("img.image, img.screenshot, div.image img, td.content.image img") ?? item.QuerySelector("img");
        var iconUrl = img?.GetAttribute("src") ?? string.Empty;
        if (!string.IsNullOrEmpty(iconUrl))
        {
            if (iconUrl.Contains("blank.gif")) iconUrl = string.Empty;
            else if (!iconUrl.StartsWith("http")) iconUrl = ModDBConstants.BaseUrl + iconUrl;
        }

        return iconUrl;
    }

    private static DateTime? ExtractLastUpdatedDate(AngleSharp.Dom.IElement item)
    {
        var dateEl = item.QuerySelector("time[datetime]") ?? item.QuerySelector("abbr.timeago");
        var dateStr = dateEl?.GetAttribute("datetime") ?? dateEl?.GetAttribute("title");
        if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var parsedDate))
        {
            return parsedDate;
        }

        return null;
    }

    private static void ApplyParentModMetadata(ContentSearchResult result, string detailUrl)
    {
        var isMod = detailUrl.Contains("/mods/") && !detailUrl.Contains("/addons/");
        result.ResolverMetadata[ModDBConstants.IsModMetadataKey] = isMod.ToString();

        if (detailUrl.Contains("/mods/") && detailUrl.Contains("/addons/"))
        {
            var modMatch = ParentModUrlRegex().Match(detailUrl);
            if (modMatch.Success)
            {
                result.ResolverMetadata[ModDBConstants.ParentModUrlMetadataKey] = modMatch.Groups[1].Value;
            }
        }
    }

    private static ContentType DetermineContentType(string section, string? category, string url)
    {
        if (!string.IsNullOrEmpty(category))
        {
            var mapped = ModDBCategoryMapper.MapCategoryByName(category);
            if (mapped != ContentType.Addon)
            {
                return mapped;
            }
        }

        var isModUrl = url.Contains("/mods/", StringComparison.OrdinalIgnoreCase);
        var isAddonUrl = url.Contains("/addons/", StringComparison.OrdinalIgnoreCase);

        return section switch
        {
            "mods" => ContentType.Mod,
            "downloads" when url.Contains("/maps/", StringComparison.OrdinalIgnoreCase) => ContentType.Map,
            "downloads" when isModUrl && !isAddonUrl => ContentType.Mod,
            "downloads" => ContentType.Addon,
            "addons" => url.Contains("/maps/", StringComparison.OrdinalIgnoreCase) ? ContentType.Map : ContentType.Addon,
            _ => isModUrl && !isAddonUrl ? ContentType.Mod : ContentType.Addon,
        };
    }

    /// <summary>
    /// Determines whether a page title indicates a bot-protection interstitial rather than real
    /// ModDB content. Cloudflare's challenge ("Just a moment...") and the legacy "Attention Required"
    /// page never resolve unattended.
    /// </summary>
    /// <param name="title">The browser page title to inspect.</param>
    /// <returns><see langword="true"/> if the title looks like a bot-protection challenge.</returns>
    private static bool IsChallengePage(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        return title.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Attention Required", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"(https?://[^/]+/mods/[^/]+)")]
    private static partial Regex ParentModUrlRegex();

    private async Task<(List<ContentSearchResult> Items, bool HasMoreItems, bool KeepPageOpen, bool ChallengeDetected)> DiscoverFromSectionAsync(
        IPage page,
        string section,
        GameType gameType,
        ContentSearchQuery query,
        CancellationToken cancellationToken)
    {
        // Playwright scrape is the primary source: it paginates and exposes the full catalog.
        // The headed browser persistent context in PlaywrightService preserves the Cloudflare
        // clearance cookie after user verification so real listing markup loads.
        var (scrapeResults, hasMore, keepOpen, challengeDetected) = await DiscoverFromScrapeAsync(page, section, gameType, query, cancellationToken);
        if (challengeDetected)
        {
            // Do not replace a blocked, interactive browser flow with RSS. RSS is intentionally
            // capped at ten items and made the verified catalogue appear to regress. The browser
            // page stays open for the user to complete Cloudflare, then a refresh loads the real
            // paginated list from the persisted clearance profile.
            return ([], true, keepOpen, true);
        }

        if (scrapeResults.Count > 0)
        {
            return (scrapeResults, hasMore, keepOpen, false);
        }

        // Scrape returned nothing (transient WAF block, outage, or the browser failed to launch).
        // Fall back to the public RSS feed so the grid is never empty. RSS cannot paginate, so
        // HasMoreItems is false regardless of what the scrape thought.
        logger.LogWarning("[ModDB] Scrape returned no items for '{Section}', falling back to RSS", section);
        var rssSection = string.Equals(section, "mods", StringComparison.OrdinalIgnoreCase)
            ? "downloads"
            : section;
        var rssResults = await DiscoverFromRssFeedAsync(rssSection, gameType, cancellationToken);
        return (rssResults, false, keepOpen, false);
    }

    private async Task<(List<ContentSearchResult> Items, bool HasMoreItems, bool KeepPageOpen, bool ChallengeDetected)> DiscoverFromScrapeAsync(
        IPage page,
        string section,
        GameType gameType,
        ContentSearchQuery query,
        CancellationToken cancellationToken)
    {
        var keepPageOpenForVerification = false;
        try
        {
            var filter = BuildFilterFromQuery(query);
            var url = BuildSectionUrl(section, gameType, filter);

            logger.LogInformation(
                "[ModDB] Fetching page {Page} from section '{Section}': {Url}",
                filter.Page,
                section,
                url);

            await page.GotoAsync(url, new PageGotoOptions { Timeout = ModDBConstants.DefaultGotoTimeout, WaitUntil = WaitUntilState.Commit });

            var (listingReady, challengeObserved) = await WaitForListingOrChallengeAsync(page, url, cancellationToken);
            if (!listingReady)
            {
                var isChallenge = await HandleVerificationFailureAsync(page, url, challengeObserved);
                if (isChallenge)
                {
                    keepPageOpenForVerification = !page.IsClosed;
                    return ([], false, keepPageOpenForVerification, true);
                }
            }

            if (page.IsClosed)
            {
                return ([], false, false, challengeObserved);
            }

            var html = await page.ContentAsync();
            var browsingContext = BrowsingContext.New(Configuration.Default);
            var document = await browsingContext.OpenAsync(req => req.Content(html), cancellationToken);

            var results = ParseDocumentSearchResults(document, gameType, section);
            if (results.Count == 0)
            {
                logger.LogWarning("[ModDB] Scrape returned no items for section '{Section}'", section);
            }

            var hasMoreItems = HasMorePages(document);
            if (hasMoreItems)
            {
                logger.LogInformation("[ModDB] More items available for {Section}", section);
            }

            return (results, hasMoreItems, keepPageOpenForVerification, false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to discover from {Section} with Playwright", section);
            return ([], false, keepPageOpenForVerification, false);
        }
    }

    private static string BuildSectionUrl(string section, GameType gameType, ModDBFilter filter)
    {
        var baseUrl = gameType == GameType.Generals
            ? $"{ModDBConstants.GeneralsBaseUrl}/{section}"
            : $"{ModDBConstants.ZeroHourBaseUrl}/{section}";
        var pageSuffix = filter.Page > 1 ? $"/page/{filter.Page}" : string.Empty;
        return baseUrl + pageSuffix + filter.ToQueryString();
    }

    private async Task<(bool ListingReady, bool ChallengeObserved)> WaitForListingOrChallengeAsync(
        IPage page,
        string url,
        CancellationToken cancellationToken)
    {
        var challengeObserved = false;
        var deadline = DateTime.UtcNow.AddMilliseconds(ModDBConstants.VerificationWaitTimeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (page.IsClosed)
            {
                logger.LogInformation("[ModDB] Browser page was closed; ending verification wait for {Url}", url);
                return (false, challengeObserved);
            }

            try
            {
                var title = await page.TitleAsync();
                if (IsChallengePage(title))
                {
                    if (!challengeObserved)
                    {
                        challengeObserved = true;
                        logger.LogWarning(
                            "[ModDB] Cloudflare challenge is blocking {Url} (title: '{Title}'). Waiting for the user to solve it in the browser window.",
                            url,
                            title);
                        try
                        {
                            await page.BringToFrontAsync();
                        }
                        catch (PlaywrightException)
                        {
                        }
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                    continue;
                }

                if (await page.QuerySelectorAsync(ModDBConstants.DefaultListItemSelector) != null)
                {
                    if (challengeObserved)
                    {
                        logger.LogInformation("[ModDB] Cloudflare challenge cleared for {Url}; parsing the listing.", url);
                    }

                    return (true, challengeObserved);
                }
            }
            catch (PlaywrightException ex) when (Tools.PlaywrightService.IsContextClosedError(ex))
            {
                if (page.IsClosed)
                {
                    logger.LogInformation("[ModDB] Browser page was closed; ending verification wait for {Url}", url);
                    return (false, challengeObserved);
                }

                logger.LogDebug(ex, "[ModDB] Transient navigation while waiting for listing {Url}; retrying", url);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        return (false, challengeObserved);
    }

    private async Task<bool> HandleVerificationFailureAsync(IPage page, string url, bool challengeObserved)
    {
        string? pageTitle = null;
        try
        {
            if (!page.IsClosed)
            {
                pageTitle = await page.TitleAsync();
            }
        }
        catch (PlaywrightException)
        {
        }

        if (IsChallengePage(pageTitle) || challengeObserved)
        {
            logger.LogWarning(
                "[ModDB] Verification was not completed within {Timeout} ms for {Url}. The page stays open; the user can retry after solving it.",
                ModDBConstants.VerificationWaitTimeoutMs,
                url);
            return true;
        }

        logger.LogWarning(
            "ModDB did not expose a listing selector within {Timeout} ms for {Url} (page title: '{Title}'), parsing the current document...",
            ModDBConstants.VerificationWaitTimeoutMs,
            url,
            pageTitle ?? "Unknown");
        return false;
    }

    private static List<ContentSearchResult> ParseDocumentSearchResults(
        IDocument document,
        GameType gameType,
        string section)
    {
        List<ContentSearchResult> results = [];
        var contentItems = document.QuerySelectorAll(ModDBConstants.DefaultListItemSelector);

        foreach (var item in contentItems)
        {
            try
            {
                var searchResult = ParseContentItem(item, gameType, section);
                if (searchResult != null)
                {
                    results.Add(searchResult);
                }
            }
            catch
            {
                // Ignore parse errors for individual items
            }
        }

        return results;
    }

    private static bool HasMorePages(IDocument document)
    {
        var nextLink = document.QuerySelector("div.pages a.next") ?? document.QuerySelector("a.next");
        return nextLink != null;
    }

    /// <summary>
    /// Fallback discovery via ModDB's public RSS feeds (https://rss.moddb.com/...), which are not
    /// behind the site's bot protection. Returns up to the feed size (currently 10 items).
    /// </summary>
    /// <param name="section">The ModDB section (e.g. "downloads" or "addons").</param>
    /// <param name="gameType">The target game.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The parsed search results (empty on any failure).</returns>
    private async Task<List<ContentSearchResult>> DiscoverFromRssFeedAsync(
        string section,
        GameType gameType,
        CancellationToken cancellationToken)
    {
        var results = new List<ContentSearchResult>();

        try
        {
            var gameSlug = gameType == GameType.Generals ? "cc-generals" : "cc-generals-zero-hour";
            var feedUrl = $"https://rss.moddb.com/games/{gameSlug}/{section}/feed/rss.xml";

            using var client = httpClientFactory.CreateClient();
            if (client.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            }

            var xml = await client.GetStringAsync(feedUrl, cancellationToken);
            var feed = System.Xml.Linq.XDocument.Parse(xml);

            foreach (var item in feed.Descendants("item"))
            {
                var title = item.Element("title")?.Value?.Trim();
                var link = item.Element("link")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
                {
                    continue;
                }

                var rawDescription = item.Element("description")?.Value?.Trim() ?? string.Empty;
                var description = HtmlTextHelper.NormalizeHtml(rawDescription);
                DateTime? published = null;
                if (DateTime.TryParse(item.Element("pubDate")?.Value, out var parsedDate))
                {
                    published = parsedDate;
                }

                System.Xml.Linq.XNamespace media = "http://search.yahoo.com/mrss/";
                var fullImage = item.Descendants(media + "content").FirstOrDefault()?.Attribute("url")?.Value;
                var thumbnail = item.Descendants(media + "thumbnail").FirstOrDefault()?.Attribute("url")?.Value
                    ?? fullImage
                    ?? string.Empty;

                var moddbId = ExtractModDBIdFromUrl(link);
                var contentType = DetermineContentType(section, null, link);
                var prospectiveId = published.HasValue && published.Value > DateTime.MinValue
                    ? ManifestIdGenerator.GeneratePublisherContentId(ModDBConstants.PublisherPrefix, contentType, title, published.Value)
                    : ManifestIdGenerator.GeneratePublisherContentId(ModDBConstants.PublisherPrefix, contentType, title, 0);

                var result = new ContentSearchResult
                {
                    Id = prospectiveId,
                    Name = title,
                    Description = description,
                    AuthorName = "Unknown",
                    ContentType = contentType,
                    TargetGame = gameType,
                    ProviderName = ModDBConstants.DiscovererSourceName,
                    IconUrl = thumbnail,
                    RequiresResolution = true,
                    ResolverId = ModDBConstants.ResolverId,
                    SourceUrl = link,
                    LastUpdated = published,
                };

                if (!string.IsNullOrEmpty(fullImage))
                {
                    result.ScreenshotUrls.Add(fullImage);
                }

                result.ResolverMetadata[ModDBConstants.ContentIdMetadataKey] = moddbId;
                result.ResolverMetadata[ModDBConstants.SectionMetadataKey] = section;
                result.ResolverMetadata[ModDBConstants.IsModMetadataKey] = (link.Contains("/mods/") && !link.Contains("/addons/")).ToString();

                results.Add(result);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[ModDB] RSS fallback failed for section '{Section}'", section);
        }

        return results;
    }
}
