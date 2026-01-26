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
public class ModDBDiscoverer(ILogger<ModDBDiscoverer> logger) : IContentDiscoverer
{
    private static readonly SemaphoreSlim _browserLock = new(1, 1);
    private static IPlaywright? _playwright;
    private static IBrowser? _browser;
    private readonly ILogger<ModDBDiscoverer> _logger = logger;

    /// <inheritdoc />
    public string SourceName => ModDBConstants.DiscovererSourceName;

    /// <inheritdoc />
    public string Description => ModDBConstants.DiscovererDescription;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresDiscovery;

    /// <summary>
    /// Discover ModDB content matching the provided search query using a Playwright-driven browser and aggregate results across relevant sections.
    /// </summary>
    /// <param name="query">Search parameters and filters (e.g., search term, content type, section, page, and target game) used to determine which ModDB sections and pages to query.</param>
    /// <param name="cancellationToken">Token to cancel the discovery operation.</param>
    /// <returns>
    /// An <see cref="OperationResult{ContentDiscoveryResult}"/> whose value contains the discovered items and a <see cref="ContentDiscoveryResult.HasMoreItems"/> flag indicating if additional pages are available; the result will represent failure with an error message if discovery could not be completed.
    /// </returns>
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

    /// <summary>
    /// Ensures the shared Playwright runtime and a headless Chromium browser are initialized for use by the discoverer.
    /// </summary>
    /// <remarks>
    /// Safe to call multiple times; this method serializes initialization so only one browser instance is created and stored in the static fields.
    /// </remarks>
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

    /// <summary>
    /// Selects which ModDB sections to search based on the provided query.
    /// </summary>
    /// <param name="query">Search query whose ModDBSection or ContentType determine the sections to search.</param>
    /// <returns>A list of section names (for example "downloads" or "addons"). If <c>query.ModDBSection</c> is set that single section is returned; otherwise sections are chosen based on <c>query.ContentType</c>, defaulting to "downloads".</returns>
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

    /// <summary>
    /// Builds a ModDBFilter populated from the provided ContentSearchQuery.
    /// </summary>
    /// <param name="query">Search parameters whose fields are mapped into the filter: SearchTerm -> Keyword, Page -> Page (defaults to 1), ModDBCategory -> Category, ModDBAddonCategory -> AddonCategory, ModDBLicense -> Licence, ModDBTimeframe -> Timeframe.</param>
    /// <returns>A ModDBFilter populated from the query's values.</returns>
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

    /// <summary>
    /// Maps a ContentType to the corresponding ModDB category identifier for the given section.
    /// </summary>
    /// <param name="contentType">The content type to map.</param>
    /// <param name="section">The ModDB section context; expected values are "downloads" or "addons".</param>
    /// <returns>The ModDB category identifier matching the content type and section, or <c>null</c> if no mapping exists.</returns>
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

    /// <summary>
    /// Parses a ModDB listing element into a ContentSearchResult.
    /// </summary>
    /// <param name="item">An AngleSharp element representing a single listing or row from a ModDB results page.</param>
    /// <param name="gameType">The target game to assign to the parsed result.</param>
    /// <param name="section">The ModDB section (e.g., "downloads" or "addons") used to infer content type and metadata.</param>
    /// <returns>The parsed ContentSearchResult populated with metadata (id, name, author, icon, description, content type, source URL, etc.), or `null` if the element lacks a valid title/link or ModDB URL.</returns>
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

        return result;
    }

    /// <summary>
    /// Determine the content type for a ModDB item from its section, optional category name, and URL.
    /// </summary>
    /// <param name="section">The ModDB section name (e.g., "mods", "downloads", "addons").</param>
    /// <param name="category">An optional category name used to map to a ContentType when available.</param>
    /// <param name="url">The item's URL; used to detect map items when it contains "/maps/".</param>
    /// <returns>
    /// The inferred <see cref="ContentType"/>: if a category maps to a non-Addon type that is returned; otherwise,
    /// for "mods" returns <c>ContentType.Mod</c>, for "downloads" or "addons" returns <c>ContentType.Map</c> when the URL contains "/maps/" and <c>ContentType.Addon</c> otherwise; defaults to <c>ContentType.Addon</c>.
    /// </returns>
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

    /// <summary>
    /// Extracts the ModDB identifier from the given content URL.
    /// </summary>
    /// <returns>The last non-empty path segment of the URL (the ModDB identifier); if the URL is invalid or contains no path segments, returns a newly generated GUID string.</returns>
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

    /// <summary>
    /// Discovers content items from a specific ModDB section for the given game and query, returning found items and whether another page exists.
    /// </summary>
    /// <param name="section">The ModDB section to search (e.g., "downloads", "addons").</param>
    /// <param name="gameType">Target game variant used to build the section base URL.</param>
    /// <param name="query">Search and filter parameters (search term, page, categories, etc.).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A tuple where `Items` is the list of discovered ContentSearchResult entries from the requested page, and `HasMoreItems` is `true` if a next page link was detected, `false` otherwise. On error the function returns an empty list and `false`.
    /// </returns>
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