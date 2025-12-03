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
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// Discovers content from ModDB website across mods, downloads, and addons sections.
/// Supports comprehensive filtering and category-based discovery.
/// </summary>
public class ModDBDiscoverer(HttpClient httpClient, ILogger<ModDBDiscoverer> logger) : IContentDiscoverer
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<ModDBDiscoverer> _logger = logger;

    /// <inheritdoc />
    public string SourceName => ModDBConstants.DiscovererSourceName;

    /// <inheritdoc />
    public string Description => ModDBConstants.DiscovererDescription;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresDiscovery;

    /// <inheritdoc />
    public async Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var gameType = query.TargetGame ?? GameType.ZeroHour;
            _logger.LogInformation("Discovering ModDB content for {Game}", gameType);

            var results = new List<ContentSearchResult>();

            // Determine which sections to search based on ContentType filter
            var sectionsToSearch = DetermineSectionsToSearch(query.ContentType);

            foreach (var section in sectionsToSearch)
            {
                var sectionResults = await DiscoverFromSectionAsync(section, gameType, query, cancellationToken);
                results.AddRange(sectionResults);
            }

            _logger.LogInformation("Discovered {Count} ModDB items across {Sections} sections", 
                results.Count, sectionsToSearch.Count);

            return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover ModDB content");
            return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure($"Discovery failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Determines which sections to search based on content type filter.
    /// </summary>
    private List<string> DetermineSectionsToSearch(ContentType? contentType)
    {
        if (contentType == null)
        {
            // No filter - search all sections
            return ["mods", "downloads", "addons"];
        }

        return contentType switch
        {
            ContentType.Mod => ["mods", "downloads"], // Some full mods are in downloads
            ContentType.Patch => ["downloads"],
            ContentType.Map or ContentType.MapPack => ["addons", "downloads"],
            ContentType.Skin => ["addons"],
            ContentType.ModdingTool => ["downloads"],
            ContentType.LanguagePack => ["downloads", "addons"],
            ContentType.Video => ["downloads"],
            _ => ["downloads", "addons"]
        };
    }

    /// <summary>
    /// Discovers content from a specific section.
    /// </summary>
    private async Task<List<ContentSearchResult>> DiscoverFromSectionAsync(
        string section,
        GameType gameType,
        ContentSearchQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            // Build URL for the section
            var baseUrl = gameType == GameType.Generals
                ? $"{ModDBConstants.BaseUrl}/games/cc-generals/{section}"
                : $"{ModDBConstants.BaseUrl}/games/cc-generals-zero-hour/{section}";

            // Build filter query string if needed
            var filter = BuildFilterFromQuery(query, section);
            var queryString = filter.ToQueryString();
            var url = baseUrl + queryString;

            _logger.LogDebug("Fetching from URL: {Url}", url);

            var html = await _httpClient.GetStringAsync(url, cancellationToken);
            var context = BrowsingContext.New(Configuration.Default);
            var document = await context.OpenAsync(req => req.Content(html), cancellationToken);

            var results = new List<ContentSearchResult>();

            // Parse content items - ModDB uses consistent structure across sections
            // Usually div.row.rowcontent or div.table depending on the view, but rowcontent is common for lists
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
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse content item");
                }
            }

            _logger.LogDebug("Found {Count} items in {Section} section", results.Count, section);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to discover from {Section} section", section);
            return [];
        }
    }

    /// <summary>
    /// Builds a filter object from the search query.
    /// </summary>
    private ModDBFilter BuildFilterFromQuery(ContentSearchQuery query, string section)
    {
        var filter = new ModDBFilter
        {
            Keyword = query.SearchTerm,
            Page = query.Page ?? 1
        };

        // Map ContentType to ModDB category if specified
        if (query.ContentType.HasValue)
        {
            filter.Category = MapContentTypeToCategory(query.ContentType.Value, section);
            
            // For addons, the parameter is different
            if (section == "addons")
            {
                filter.AddonCategory = filter.Category;
                filter.Category = null; // Clear main category if it's an addon search
            }
        }

        return filter;
    }

    /// <summary>
    /// Maps GenHub ContentType to ModDB category code.
    /// </summary>
    private string? MapContentTypeToCategory(ContentType contentType, string section)
    {
        if (section == "downloads")
        {
            return contentType switch
            {
                ContentType.Mod => "2", // Full Version
                ContentType.Patch => "4", // Patch
                ContentType.Video => "8", // Movie
                ContentType.ModdingTool => "14", // Mapping Tool
                ContentType.LanguagePack => "30", // Language Pack
                _ => null
            };
        }
        else if (section == "addons")
        {
            return contentType switch
            {
                ContentType.Map => "101", // Multiplayer Map
                ContentType.Skin => "112", // Player Skin
                ContentType.LanguagePack => "138", // Language Sounds
                _ => null
            };
        }

        return null;
    }

    /// <summary>
    /// Parses a single content item from HTML.
    /// </summary>
    private ContentSearchResult? ParseContentItem(AngleSharp.Dom.IElement item, GameType gameType, string section)
    {
        // Extract title and link
        // ModDB structure: <h4><a href="...">Title</a></h4>
        var titleLink = item.QuerySelector("h4 a, h3 a, a.title");
        
        // Fallback for table rows (downloads/addons sometimes use tables)
        if (titleLink == null)
        {
            titleLink = item.QuerySelector("td.content.name a");
        }

        if (titleLink == null)
        {
            return null;
        }

        var title = titleLink.TextContent?.Trim();
        var href = titleLink.GetAttribute("href");

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        // Filter out non-content links if we grabbed something generic
        if (!href.Contains("/mods/") && !href.Contains("/downloads/") && !href.Contains("/addons/"))
        {
             return null;
        }

        // Make absolute URL
        var detailUrl = href.StartsWith("http") ? href : ModDBConstants.BaseUrl + href;

        // Extract author
        var authorLink = item.QuerySelector("a[href*='/members/'], a[href*='/company/']");
        var author = authorLink?.TextContent?.Trim() ?? ModDBConstants.DefaultAuthor;

        // Extract preview image
        var img = item.QuerySelector("img");
        var iconUrl = img?.GetAttribute("src") ?? string.Empty;
        if (!string.IsNullOrEmpty(iconUrl) && !iconUrl.StartsWith("http"))
        {
            iconUrl = ModDBConstants.BaseUrl + iconUrl;
        }

        // Extract description
        var descEl = item.QuerySelector("p, div.summary, span.summary");
        var description = descEl?.TextContent?.Trim() ?? string.Empty;

        // Try to extract category from the page
        var categoryEl = item.QuerySelector("span.category, div.category, span.subheading");
        var category = categoryEl?.TextContent?.Trim();

        // Map to ContentType
        var contentType = DetermineContentType(section, category, detailUrl);

        // Extract ModDB ID from URL
        var moddbId = ExtractModDBIdFromUrl(detailUrl);

        var searchResult = new ContentSearchResult
        {
            Id = $"{ModDBConstants.PublisherPrefix}-{moddbId}",
            Name = title,
            Description = description,
            AuthorName = author,
            ContentType = contentType,
            TargetGame = gameType,
            ProviderName = SourceName,
            IconUrl = iconUrl,
            RequiresResolution = true,
            ResolverId = ModDBConstants.ResolverId,
            SourceUrl = detailUrl,
        };

        // Add metadata
        searchResult.ResolverMetadata[ModDBConstants.ContentIdMetadataKey] = moddbId;
        searchResult.ResolverMetadata[ModDBConstants.SectionMetadataKey] = section;

        return searchResult;
    }

    /// <summary>
    /// Determines content type from section and category.
    /// </summary>
    private ContentType DetermineContentType(string section, string? category, string url)
    {
        // First try category-based mapping if available
        if (!string.IsNullOrEmpty(category))
        {
            var mapped = ModDBCategoryMapper.MapCategoryByName(category);
            if (mapped != ContentType.Addon) // Addon is the default fallback
            {
                return mapped;
            }
        }

        // Fallback to section-based heuristics
        return section switch
        {
            "mods" => ContentType.Mod,
            "downloads" => url.Contains("/maps/") ? ContentType.Map : ContentType.Addon,
            "addons" => url.Contains("/maps/") ? ContentType.Map : ContentType.Addon,
            _ => ContentType.Addon
        };
    }

    /// <summary>
    /// Extracts ModDB ID from a URL.
    /// </summary>
    private string ExtractModDBIdFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length > 0 ? segments[^1] : Guid.NewGuid().ToString();
        }
        catch
        {
            return Guid.NewGuid().ToString();
        }
    }
}
