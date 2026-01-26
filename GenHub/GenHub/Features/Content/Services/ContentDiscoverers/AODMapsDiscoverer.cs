using AngleSharp;
using AngleSharp.Dom;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// Discovers maps from AODMaps (Age of Defense Maps) website.
/// </summary>
public partial class AODMapsDiscoverer(
    IHttpClientFactory httpClientFactory,
    ILogger<AODMapsDiscoverer> logger) : IContentDiscoverer
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<AODMapsDiscoverer> _logger = logger;

    /// <summary>
    /// Provides a regular expression that matches a download count (a number with optional thousands separators) followed by the word "download" or "downloads".
    /// </summary>
    /// <returns>A compiled <see cref="Regex"/> whose first capture group contains the numeric download count (digits with optional commas); matching is case-insensitive.</returns>
    [GeneratedRegex(@"(\d+(?:,\d{3})*)\s*downloads?", RegexOptions.IgnoreCase)]
    private static partial Regex DownloadCountRegex();

    /// <summary>
    /// Provides a regular expression that matches an integer page number in filenames ending with ".html".
    /// </summary>
    /// <returns>A <see cref="Regex"/> that captures a sequence of digits immediately followed by the literal ".html".</returns>
    [GeneratedRegex(@"(\d+)\.html", RegexOptions.None)]
    private static partial Regex HtmlPageNumberRegex();

    /// <summary>
    /// Convert a potentially relative AODMaps URL to an absolute URL using the AODMaps base URL.
    /// </summary>
    /// <param name="url">The URL to normalize; may be null, empty, relative, or already absolute.</param>
    /// <returns>The absolute URL when a relative URL was provided, or the original <paramref name="url"/> if it is null, empty, or already starts with "http".</returns>
    private static string? MakeAbsoluteUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return url;
        }

        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        // Handle ../ paths if necessary, but simple concatenation usually works if base is known
        // Or specific cleaning
        return $"{AODMapsConstants.BaseUrl.TrimEnd('/')}/{url.TrimStart('/')}";
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

        downloadUrl = MakeAbsoluteUrl(downloadUrl);

        // Thumbnail
        var imgEl = item.QuerySelector(AODMapsConstants.GalleryThumbnailSelector);
        var thumbnailUrl = imgEl?.GetAttribute(AODMapsConstants.SrcAttribute);
        thumbnailUrl = MakeAbsoluteUrl(thumbnailUrl);

        // Downloads (parsed from script or text)
        // Simply store it in metadata if needed for sorting?
        // We really need it for the Manifest, but Discoverer just finds.
        string safeDownloadUrl = downloadUrl ?? string.Empty;
        string safeHashCode = ComputeStableHash(safeDownloadUrl);

        return new ContentSearchResult
        {
            Id = safeHashCode,
            Name = name,
            Description = AODMapsConstants.MapDescriptionTemplate,
            AuthorName = AODMapsConstants.DefaultAuthorName,
            Version = "0",
            ProviderName = AODMapsConstants.DiscovererSourceName,
            SourceUrl = sourceUrl,
            IconUrl = thumbnailUrl,
            ContentType = ContentType.Map,
            TargetGame = GameType.Generals,
            RequiresResolution = true,
            ResolverId = AODMapsConstants.ResolverId,
            ResolverMetadata =
            {
                { AODMapsConstants.DownloadUrlMetadataKey, safeDownloadUrl },
                { AODMapsConstants.MapIdMetadataKey, safeHashCode },
                { AODMapsConstants.ContentIdMetadataKey, safeHashCode },
                { AODMapsConstants.IconUrlMetadataKey, thumbnailUrl ?? string.Empty },
            },
        };
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

        downloadUrl = MakeAbsoluteUrl(downloadUrl);

        // Image
        var imgEl = content.QuerySelector(AODMapsConstants.MapMakerImageSelector);
        var thumbnailUrl = imgEl?.GetAttribute(AODMapsConstants.SrcAttribute);
        thumbnailUrl = MakeAbsoluteUrl(thumbnailUrl);

        // Description/Info
        var p1 = content.QuerySelector(AODMapsConstants.MapMakerInfoSelector)?.TextContent;

        // p1 contains "- Type: Survival - Difficultly: Hard ..."
        string safeDownloadUrl = downloadUrl ?? string.Empty;
        string safeHashCode = ComputeStableHash(safeDownloadUrl);

        return new ContentSearchResult
        {
            Id = safeHashCode,
            Name = title,
            Description = p1 ?? AODMapsConstants.MapDescriptionTemplate,
            AuthorName = "MapMaker",
            Version = "0",
            ProviderName = AODMapsConstants.DiscovererSourceName,
            SourceUrl = sourceUrl,
            IconUrl = thumbnailUrl,
            ContentType = ContentType.Map,
            TargetGame = GameType.Generals,
            RequiresResolution = true,
            ResolverId = AODMapsConstants.ResolverId,
            ResolverMetadata =
            {
                { AODMapsConstants.DownloadUrlMetadataKey, safeDownloadUrl },
                { AODMapsConstants.MapIdMetadataKey, safeHashCode },
                { AODMapsConstants.ContentIdMetadataKey, safeHashCode },
                { AODMapsConstants.IconUrlMetadataKey, thumbnailUrl ?? string.Empty },
            },
        };
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

    /// <inheritdoc />
    public string SourceName => AODMapsConstants.DiscovererSourceName;

    /// <inheritdoc />
    public string Description => AODMapsConstants.DiscovererDescription;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresDiscovery;

    /// <summary>
    /// Discovers AODMaps content for the given query and returns the found items and pagination hint.
    /// </summary>
    /// <param name="query">Search or browsing query specifying filters (page, content type, tags, etc.).</param>
    /// <param name="cancellationToken">Token to cancel the discovery operation.</param>
    /// <returns>An <see cref="OperationResult{ContentDiscoveryResult}"/> whose value contains the discovered items and a flag indicating whether more pages are available; the result is a failure if discovery could not be completed.</returns>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via <paramref name="cancellationToken"/>.</exception>
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

            // Get page number from query (defaults to 1)
            int page = query.Page ?? 1;

            var results = new List<ContentSearchResult>();

            // Build the URL based on the query
            var url = BuildDiscoveryUrl(query);

            _logger.LogInformation("Discovering AODMaps content from: {Url} (Page {Page})", url, page);

            // Fetch HTML
            using var client = _httpClientFactory.CreateClient("AODMaps"); // Should be registered or falls back

            // Ensure we have a user agent just in case
            if (client.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            }

            var html = await client.GetStringAsync(url, cancellationToken);

            // Parse HTML
            var context = BrowsingContext.New(Configuration.Default);
            var document = await context.OpenAsync(req => req.Content(html), cancellationToken);

            // Extract items
            var (items, hasMoreItems) = ExtractItems(document, url, page);
            results.AddRange(items);

            _logger.LogInformation(
                "Discovered {Count} AODMaps items from {Url} (Page {Page}, HasMore: {HasMore})",
                results.Count,
                url,
                page,
                hasMoreItems);

            return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = results,
                HasMoreItems = hasMoreItems,
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("AODMaps discovery was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, AODMapsConstants.DiscoveryFailureLogMessage);
            return OperationResult<ContentDiscoveryResult>.CreateFailure(
                string.Format(AODMapsConstants.DiscoveryFailedErrorTemplate, ex.Message));
        }
    }

    /// <summary>
    /// Builds the AODMaps discovery page URL appropriate for the given search query.
    /// </summary>
    /// <param name="query">Search parameters including optional Page, ContentType, and CNCLabsMapTags (used to select author pages, player-count pages, compstomp category, or other special pages).</param>
    /// <returns>The fully formatted relative discovery URL for the query (e.g., a pattern like "new.html", "new2.html", a map maker page, player-count page, compstomp page, or map packs page).</returns>
    private static string BuildDiscoveryUrl(ContentSearchQuery query)
    {
        var page = query.Page ?? 1;
        var pageStr = page > 1 ? page.ToString() : string.Empty; // Some URLs use "2", "3". "1" is often empty or omitted.

        // Special case: Page 1 often has no suffix. Page 2 has '2'.
        // Format {0} in patterns usually denotes the number suffix.
        string suffix = page > 1 ? page.ToString() : string.Empty;

        // 1. Check for specific map makers in query or tags
        // If we want to browse a map maker
        // Not implemented in basic browsing yet unless we parse "tags" containing "author:xxx"
        if (query.CNCLabsMapTags != null && query.CNCLabsMapTags.Any(t => t.StartsWith("author:")))
        {
            var authorTag = query.CNCLabsMapTags.First(t => t.StartsWith("author:"));
            var authorName = authorTag.Replace("author:", string.Empty);

            // Look up mapping if needed
            // Try formatting
            return string.Format(AODMapsConstants.MapMakerPagePattern, authorName);
        }

        // 2. Check Content Type
        if (query.ContentType == ContentType.MapPack)
        {
            return string.Format(AODMapsConstants.MapPacksPagePattern, suffix);
        }

        // 3. Check Categories (Compstomp, Air, Race, etc - passed as Tags or specialized logic?)
        // Assuming user might pass these as Tags or we map ContentType?
        // Simplification: If "Compstomp" tag is present
        if (query.CNCLabsMapTags != null && query.CNCLabsMapTags.Contains("Compstomp", StringComparer.OrdinalIgnoreCase))
        {
            return string.Format(AODMapsConstants.CompstompPagePattern, suffix);
        }

        // 4. Browsing by Player Count (very common in AOD)
        // If we have a tag "6 Players", "3 Players" etc.
        if (query.CNCLabsMapTags != null)
        {
            var playerTag = query.CNCLabsMapTags.FirstOrDefault(t => t.EndsWith("Players", StringComparison.OrdinalIgnoreCase));
            if (playerTag != null)
            {
                var numPart = playerTag.Split(' ')[0];
                if (int.TryParse(numPart, out _))
                {
                    return string.Format(AODMapsConstants.PlayerPagePattern, numPart, suffix);
                }
            }
        }

        // 5. Default: New Maps (Last Uploaded)
        // Note: Page 1 is new.html, Page 2 is new2.html, Page 3 is new3.html
        return string.Format(AODMapsConstants.NewMapsPagePattern, suffix);
    }

    /// <summary>
    /// Extracts content items from an AODMaps HTML document and determines whether a subsequent page is available.
    /// </summary>
    /// <param name="document">The parsed HTML document to scan for content items.</param>
    /// <param name="sourceUrl">The originating page URL used as the source for any discovered item links.</param>
    /// <param name="currentPage">The current page number used when evaluating pagination/next-page hints.</param>
    /// <returns>
    /// A tuple where `Items` is the list of discovered ContentSearchResult entries parsed from the document,
    /// and `HasMoreItems` is `true` when the document contains indicators that a next page exists, `false` otherwise.
    /// </returns>
    private (List<ContentSearchResult> Items, bool HasMoreItems) ExtractItems(IDocument document, string sourceUrl, int currentPage)
    {
        var results = new List<ContentSearchResult>();

        // Strategy 1: Gallery Items (Common on Players, New, Packs pages)
        var galleryItems = document.QuerySelectorAll(AODMapsConstants.GalleryItemSelector);
        if (galleryItems.Length > 0)
        {
            foreach (var item in galleryItems)
            {
                var result = ParseGalleryItem(item, sourceUrl);
                if (result != null)
                {
                    results.Add(result);
                }
            }
        }

        // Strategy 2: Map Maker Page Items (Vertical layout)
        // Only if Gallery items were not found or we want to support mixed pages
        var mmItems = document.QuerySelectorAll(AODMapsConstants.MapMakerContainerSelector);
        if (mmItems.Length > 0)
        {
            foreach (var item in mmItems)
            {
                // Each 'main' block is an item on map maker pages
                // Need to go deeper into .content
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

        // Check for next page indicator to support progressive loading
        bool hasMoreItems = CheckForNextPage(document, currentPage);

        return (results, hasMoreItems);
    }

    /// <summary>
    /// Determines whether the provided HTML document indicates the presence of a next paginated page after the specified current page.
    /// </summary>
    /// <param name="document">The parsed HTML document to inspect for pagination links or next-page patterns.</param>
    /// <param name="currentPage">The current page number to compare against discovered page links.</param>
    /// <returns>`true` if a next page is detected, `false` otherwise.</returns>
    private bool CheckForNextPage(IDocument document, int currentPage)
    {
        // AODMaps uses pagination links at the bottom of pages
        // We need to check if there's a link to the next page

        // Method 1: Look for a "Next" link text
        var nextLink = document.QuerySelectorAll("a").FirstOrDefault(a =>
            a.TextContent != null &&
            a.TextContent.Trim().Equals("Next", StringComparison.OrdinalIgnoreCase));

        if (nextLink != null)
        {
            _logger.LogInformation("[AODMaps] Found 'Next' link: {Url}", nextLink.GetAttribute("href"));
            return true;
        }

        // Method 2: Look for numbered pagination links and check if any are greater than current page
        // AODMaps typically shows page numbers like: 1 2 3 4 ... Next
        var allLinks = document.QuerySelectorAll("a").Where(a =>
        {
            var href = a.GetAttribute("href");
            var text = a.TextContent?.Trim();

            // Look for links that might be page numbers (digits or patterns like "new2.html", "new3.html")
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(href))
                return false;

            // Check if href contains page pattern (new2.html, new3.html, etc.)
            if (href.Contains("new") || href.Contains("players") || href.Contains("compstomp") || href.Contains("Map_Packs"))
            {
                // Extract page number from href patterns
                // e.g., "new2.html" -> page 2, "6_players2.html" -> page 2
                var match = HtmlPageNumberRegex().Match(href);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var pageNum))
                {
                    return pageNum > currentPage;
                }

                // Also check the link text for page numbers
                if (int.TryParse(text, out var textPageNum))
                {
                    return textPageNum > currentPage;
                }
            }

            return false;
        }).ToList();

        if (allLinks.Count > 0)
        {
            _logger.LogInformation("[AODMaps] Found {Count} pagination links to higher pages", allLinks.Count);
            return true;
        }

        // Method 3: Check for any link that points to the next page based on URL patterns
        // Look for links with "new{N}.html" pattern where N > currentPage
        var nextPagePattern = currentPage > 1
            ? $"new{currentPage + 1}.html"
            : "new2.html";

        var directNextLink = document.QuerySelectorAll("a").FirstOrDefault(a =>
        {
            var href = a.GetAttribute("href");
            return href != null && href.Contains(nextPagePattern);
        });

        if (directNextLink != null)
        {
            _logger.LogInformation("[AODMaps] Found direct next page link: {Url}", directNextLink.GetAttribute("href"));
            return true;
        }

        _logger.LogInformation("[AODMaps] No next page link found on page {Page}", currentPage);
        return false;
    }
}