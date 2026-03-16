using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.ModDB;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// Discovers content from ModDB website using Playwright to bypass WAF/Bot protections.
/// </summary>
public class ModDBDiscoverer : IContentDiscoverer
{
    private static readonly SemaphoreSlim _browserLock = new(1, 1);
    private static IPlaywright? _playwright;
    private static IBrowser? _browser;
    private readonly ILogger<ModDBDiscoverer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModDBDiscoverer"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public ModDBDiscoverer(ILogger<ModDBDiscoverer> logger)
    {
        _logger = logger;
    }

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
            await EnsurePlaywrightInitializedAsync();

            var gameType = query.TargetGame ?? GameType.ZeroHour;
            _logger.LogInformation("Discovering ModDB content for {Game} using Playwright", gameType);

            List<ContentSearchResult> results = [];
            bool hasMoreItems = false;

            // Determine which sections to search based on query filters
            var sectionsToSearch = DetermineSectionsToSearch(query);

            foreach (var section in sectionsToSearch)
            {
                var (sectionResults, sectionHasMore) = await DiscoverFromSectionAsync(section, gameType, query, cancellationToken);
                results.AddRange(sectionResults);
                if (sectionHasMore)
                {
                    hasMoreItems = true;
                }
            }

            _logger.LogInformation(
                "Discovered {Count} ModDB items across {Sections} sections",
                results.Count,
                sectionsToSearch.Count);

            return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = results,
                HasMoreItems = hasMoreItems,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover ModDB content");
            return OperationResult<ContentDiscoveryResult>.CreateFailure($"Discovery failed: {ex.Message}");
        }
    }

    private static async Task EnsurePlaywrightInitializedAsync()
    {
        if (_browser != null) return;

        await _browserLock.WaitAsync();
        try
        {
            if (_browser != null) return;

            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = ["--disable-blink-features=AutomationControlled"], // Attempt to hide automation
            });
        }
        finally
        {
            _browserLock.Release();
        }
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

        // Default to both if no explicit type is set, or just downloads if that's safer
        return ["downloads"];
    }

    private static ModDBFilter BuildFilterFromQuery(ContentSearchQuery query)
    {
        var filter = new ModDBFilter
        {
            Keyword = query.SearchTerm,
            Page = query.Page ?? 1,
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
        else if (section == "addons")
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
        var titleLink = item.QuerySelector("h4 a, h3 a, a.title") ?? item.QuerySelector("td.content.name a");
        if (titleLink == null) return null;

        var title = titleLink.TextContent?.Trim();
        var href = titleLink.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(href)) return null;

        if (!href.Contains("/mods/") && !href.Contains("/downloads/") && !href.Contains("/addons/")) return null;

        var detailUrl = href.StartsWith("http") ? href : ModDBConstants.BaseUrl + href;

        // Try multiple selectors for author
        var authorLink = item.QuerySelector("a[href*='/members/']") ??
                        item.QuerySelector("span.by a") ??
                        item.QuerySelector("span.author a");
        var author = authorLink?.TextContent?.Trim();
        if (string.IsNullOrWhiteSpace(author)) author = "Unknown";

        var img = item.QuerySelector("img.image, img.screenshot, div.image img, td.content.image img") ?? item.QuerySelector("img");
        var iconUrl = img?.GetAttribute("src") ?? string.Empty;
        if (!string.IsNullOrEmpty(iconUrl))
        {
            if (iconUrl.Contains("blank.gif")) iconUrl = string.Empty;
            else if (!iconUrl.StartsWith("http")) iconUrl = ModDBConstants.BaseUrl + iconUrl;
        }

        var descEl = item.QuerySelector("p, div.summary, span.summary, td.content.name span.summary");
        var description = descEl?.TextContent?.Trim() ?? string.Empty;

        var categoryEl = item.QuerySelector("span.category, div.category, span.subheading");
        var category = categoryEl?.TextContent?.Trim();

        // Extract date from timeago or time element
        var dateEl = item.QuerySelector("time[datetime]") ?? item.QuerySelector("abbr.timeago");
        var dateStr = dateEl?.GetAttribute("datetime") ?? dateEl?.GetAttribute("title");
        DateTime? lastUpdated = null;
        if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var parsedDate))
        {
            lastUpdated = parsedDate;
        }

        var contentType = DetermineContentType(section, category, detailUrl);
        var moddbId = ExtractModDBIdFromUrl(detailUrl);

        var result = new ContentSearchResult
        {
            Id = $"{ModDBConstants.PublisherPrefix}-{moddbId}",
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

        // Detect if this is a mod (which may have addons) vs a standalone addon
        var isMod = detailUrl.Contains("/mods/") && !detailUrl.Contains("/addons/");
        result.ResolverMetadata["IsMod"] = isMod.ToString();

        // If this is from a mod's addon section, store the parent mod URL
        if (detailUrl.Contains("/mods/") && detailUrl.Contains("/addons/"))
        {
            // Extract parent mod URL: https://www.moddb.com/mods/the-end-of-days/addons/some-addon
            // becomes: https://www.moddb.com/mods/the-end-of-days
            var modMatch = System.Text.RegularExpressions.Regex.Match(detailUrl, @"(https?://[^/]+/mods/[^/]+)");
            if (modMatch.Success)
            {
                result.ResolverMetadata["ParentModUrl"] = modMatch.Groups[1].Value;
            }
        }

        return result;
    }

    private static ContentType DetermineContentType(string section, string? category, string url)
    {
        if (!string.IsNullOrEmpty(category))
        {
            var mapped = ModDBCategoryMapper.MapCategoryByName(category);
            if (mapped != ContentType.Addon) return mapped;
        }

        return section switch
        {
            "mods" => ContentType.Mod,
            "downloads" => url.Contains("/maps/") ? ContentType.Map : ContentType.Addon,
            "addons" => url.Contains("/maps/") ? ContentType.Map : ContentType.Addon,
            _ => ContentType.Addon,
        };
    }

    private static string ExtractModDBIdFromUrl(string url)
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

    private async Task<(List<ContentSearchResult> Items, bool HasMoreItems)> DiscoverFromSectionAsync(
        string section,
        GameType gameType,
        ContentSearchQuery query,
        CancellationToken cancellationToken)
    {
        IBrowserContext? context = null;
        IPage? page = null;
        try
        {
            // Build URL for the section
            var baseUrl = gameType == GameType.Generals
                ? $"{ModDBConstants.GeneralsBaseUrl}/{section}"
                : $"{ModDBConstants.ZeroHourBaseUrl}/{section}";

            var filter = BuildFilterFromQuery(query);
            var queryString = filter.ToQueryString();

            // ModDB uses path-based pagination: /page/2, /page/3, etc.
            var pageSuffix = filter.Page > 1 ? $"/page/{filter.Page}" : string.Empty;
            var url = baseUrl + pageSuffix + queryString;

            _logger.LogInformation(
                "[ModDB] Fetching page {Page} from section '{Section}': {Url}",
                filter.Page,
                section,
                url);

            if (_browser == null) throw new InvalidOperationException("Browser not initialized");

            // Create a new context/page for this request to ensure clean session or isolated cookies if needed
            context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            });
            page = await context.NewPageAsync();

            await page.GotoAsync(url, new PageGotoOptions { Timeout = 30000, WaitUntil = WaitUntilState.DOMContentLoaded });

            // Wait for content to load
            try
            {
                await page.WaitForSelectorAsync("div.row.rowcontent, div.table tr", new PageWaitForSelectorOptions { Timeout = 5000 });
            }
            catch
            {
                _logger.LogWarning("Timeout waiting for content selector on {Url}, parsing what we have...", url);
            }

            var html = await page.ContentAsync();

            // Use AngleSharp to parse the HTML (Robust and already implemented)
            var browsingContext = BrowsingContext.New(Configuration.Default);
            var document = await browsingContext.OpenAsync(req => req.Content(html), cancellationToken);

            List<ContentSearchResult> results = [];
            var contentItems = document.QuerySelectorAll("div.row.rowcontent, div.table tr");

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

            // NEW LOGGING:
            _logger.LogInformation("[ModDB] Pagination Logic Starting...");
            var pagesDiv = document.QuerySelector("div.pages");
            if (pagesDiv != null)
            {
                _logger.LogInformation("[ModDB] found div.pages. Html content length: {Length}", pagesDiv.InnerHtml.Length);
                var allLinks = pagesDiv.QuerySelectorAll("a");
                foreach (var link in allLinks)
                {
                    _logger.LogInformation("[ModDB] Link in pages: Text='{Text}', Href='{Href}', Class='{Class}'", link.TextContent?.Trim(), link.GetAttribute("href"), link.ClassName);
                }
            }
            else
            {
                _logger.LogWarning("[ModDB] div.pages NOT FOUND");
            }

            // Check for pagination "next" button
            // ModDB typically has a 'a.next' or 'span.next' inside a div.pages
            var nextLink = document.QuerySelector("div.pages a.next") ?? document.QuerySelector("a.next");

            if (nextLink == null)
            {
                 _logger.LogWarning("[ModDB] NEXT LINK IS NULL. Trying broader search...");
                 var anyNext = document.QuerySelectorAll("a").FirstOrDefault(a => a.TextContent != null && a.TextContent.Contains("next", StringComparison.OrdinalIgnoreCase));
                 if (anyNext != null)
                 {
                     _logger.LogInformation("[ModDB] Found a link containing 'next' (but not matching selector): Text='{Text}', Href='{Href}', Class='{Class}'", anyNext.TextContent, anyNext.GetAttribute("href"), anyNext.ClassName);
                 }
            }
            else
            {
                _logger.LogInformation("[ModDB] Found next link via selector: {Url}", nextLink.GetAttribute("href"));
            }

            var hasMoreItems = nextLink != null;

            return (results, hasMoreItems);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover from {Section} with Playwright", section);
            return ([], false);
        }
        finally
        {
            if (page != null) await page.CloseAsync();
            if (context != null) await context.DisposeAsync();
        }
    }
}
