using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// Discovers maps from CNC Labs website.
/// </summary>
public partial class CNCLabsMapDiscoverer(HttpClient httpClient, ILogger<CNCLabsMapDiscoverer> logger) : IContentDiscoverer
{
    private static readonly char[] TagSeparator = [',', ';', ' '];

    [GeneratedRegex(@"(?:Date submitted|Date reviewed|Date added|Date updated|Added|Updated|reviewed):\s*(\d{1,2}/\d{1,2}/\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"(?:File Size|Size):\s*([\d\.]+\s*[KMGT]?B)", RegexOptions.IgnoreCase)]
    private static partial Regex FileSizeRegex();

    [GeneratedRegex(@"(\d+)\s*downloads|Downloads:\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex DownloadCountRegex();

    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<CNCLabsMapDiscoverer> _logger = logger;

    /// <summary>
    /// Gets the source name for this discoverer.
    /// </summary>
    public string SourceName => CNCLabsConstants.SourceName;

    /// <summary>
    /// Gets the description for this discoverer.
    /// </summary>
    public string Description => CNCLabsConstants.Description;

    /// <summary>
    /// Gets a value indicating whether this discoverer is enabled.
    /// </summary>
    public bool IsEnabled => true;

    /// <summary>
    /// Gets the capabilities of this discoverer.
    /// </summary>
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresDiscovery;

    /// <summary>
    /// Discovers maps from CNC Labs using either a free-text search or structured query
    /// (game/content type). If <paramref name="query"/> is null, or contains neither
    /// a <see cref="ContentSearchQuery.SearchTerm"/> nor both <see cref="ContentSearchQuery.TargetGame"/>
    /// and <see cref="ContentSearchQuery.ContentType"/>, a failure result is returned.
    /// </summary>
    /// <param name="query">Search criteria.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An operation result containing <see cref="ContentSearchResult"/> items on success;
    /// otherwise, a failure with a message describing the error.
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled.</exception>
    public async Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (query is null || (string.IsNullOrWhiteSpace(query.SearchTerm) && (!query.TargetGame.HasValue || !query.ContentType.HasValue)))
            {
                return OperationResult<ContentDiscoveryResult>
                    .CreateFailure(CNCLabsConstants.QueryNullErrorMessage);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var (discoveredMaps, hasMoreItems) = !string.IsNullOrWhiteSpace(query.SearchTerm)
                       ? await SearchByTextAsync(query.SearchTerm, cancellationToken).ConfigureAwait(false)
                       : await SearchByFiltersAsync(query, cancellationToken).ConfigureAwait(false);

            var results = discoveredMaps.Select(map => new ContentSearchResult
            {
                Id = string.Format(CNCLabsConstants.MapIdFormat, map.Id),
                Name = map.Name,

                // USE THE PARSED DESCRIPTION, NOT THE TEMPLATE
                Description = !string.IsNullOrWhiteSpace(map.Description) ? map.Description : CNCLabsConstants.MapDescriptionTemplate,
                AuthorName = map.Author,
                ContentType = map.ContentType ?? ContentType.UnknownContentType,
                TargetGame = map.TargetGame ?? GameType.Unknown,
                ProviderName = SourceName,

                // If we have a good description, we might not strictly "require" resolution for details,
                // but we still need it for the download link.
                RequiresResolution = true,
                ResolverId = CNCLabsConstants.ResolverId,
                SourceUrl = map.DetailUrl,
                LastUpdated = map.LastUpdated != DateTime.MinValue ? map.LastUpdated : null,
                DownloadCount = (int)(map.DownloadCount ?? 0),
                DownloadSize = (!string.IsNullOrEmpty(map.FileSize) ? ParseFileSize(map.FileSize) : null) ?? 0,
                IconUrl = map.IconUrl, // Ensure image is passed
                ResolverMetadata =
                {
                    [CNCLabsConstants.MapIdMetadataKey] = map.Id.ToString(),
                    ["fileSize"] = map.FileSize ?? string.Empty,
                    ["downloadCount"] = map.DownloadCount?.ToString() ?? "0",
                },
            }).ToList();

            foreach (var res in results)
            {
                var map = discoveredMaps.First(m => string.Format(CNCLabsConstants.MapIdFormat, m.Id) == res.Id);
                foreach (var tag in map.Tags)
                {
                    res.Tags.Add(tag);
                }
            }

            return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = results,
                HasMoreItems = hasMoreItems,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, CNCLabsConstants.DiscoveryFailureLogMessage);
            return OperationResult<ContentDiscoveryResult>.CreateFailure(string.Format(CNCLabsConstants.DiscoveryFailedErrorTemplate, ex.Message));
        }
    }

    /// <summary>
    /// Performs a text-based search using Playwright, parsing the results list for detail links and names.
    /// </summary>
    /// <param name="searchTerm">User-entered search term.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of minimally populated map list items and HasMoreItems flag.</returns>
    private async Task<(List<MapListItem> Items, bool HasMoreItems)> SearchByTextAsync(
        string searchTerm,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            throw new ArgumentException(CNCLabsConstants.SearchTermEmptyErrorMessage, nameof(searchTerm));
        }

        var url = $"{CNCLabsConstants.SearchUrlBase}{Uri.EscapeDataString(searchTerm)}";

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            throw new UriFormatException(CNCLabsConstants.InvalidAbsoluteUri);
        }

        var mapList = new List<MapListItem>();

        // Playwright setup & navigation
        using var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true }).ConfigureAwait(false);

        var context = await browser.NewContextAsync().ConfigureAwait(false);
        var page = await context.NewPageAsync().ConfigureAwait(false);
        page.SetDefaultNavigationTimeout(30_000);

        await page.GotoAsync(url).ConfigureAwait(false);

        var results = await page.QuerySelectorAllAsync(CNCLabsConstants.ResultSelector).ConfigureAwait(false);

        foreach (var result in results)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var linkHandle = await result.QuerySelectorAsync(CNCLabsConstants.LinkSelector).ConfigureAwait(false);
            if (linkHandle is null)
            {
                continue;
            }

            var detailUrl =
                await linkHandle.GetAttributeAsync(CNCLabsConstants.CanonicalHrefAttr).ConfigureAwait(false)
                ?? await linkHandle.GetAttributeAsync(CNCLabsConstants.HrefAttribute).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(detailUrl))
            {
                continue;
            }

            var checkName = (await linkHandle.InnerTextAsync().ConfigureAwait(false))?.Trim();
            if (string.IsNullOrWhiteSpace(checkName))
            {
                continue;
            }

            // Try to extract numeric id from URLs like .../details.aspx?id=123
            if (CNCLabsHelper.TryExtractMapIdFromUrl(detailUrl, CNCLabsConstants.DetailsPathMarker, out var id))
            {
                var map = await GetMapDetailsAsync(id, detailUrl, cancellationToken);
                mapList.Add(map);
            }
            else
            {
                // Non-details results: preserve a hook for future handling (e.g., list pages).
                var lower = detailUrl.ToLowerInvariant();
                _ = lower.Contains(CNCLabsConstants.GeneralsPathMarker);
            }
        }

        return (mapList, false);
    }

    /// <summary>
    /// Performs a structured search using server-rendered list pages (no headless browser).
    /// </summary>
    /// <param name="query">Structured query containing target game and content type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of map list items parsed from the list page and HasMoreItems flag.</returns>
    private async Task<(List<MapListItem> Items, bool HasMoreItems)> SearchByFiltersAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var url = CNCLabsHelper.BuildSearchUrl(query);
        _logger.LogInformation("[CNCLabs] Fetching from URL: {Url}", url);
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            throw new UriFormatException(CNCLabsConstants.InvalidAbsoluteUri);
        }

        var mapList = new List<MapListItem>();

        var html = await _httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), cancellationToken).ConfigureAwait(false);

        var results = document.QuerySelectorAll(CNCLabsConstants.ListItemSelector);

        foreach (var item in results)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var idValue = item.QuerySelector(CNCLabsConstants.FileIdHiddenSelector)?.GetAttribute(CNCLabsConstants.ValueAttribute);
            if (!string.IsNullOrWhiteSpace(idValue) && int.TryParse(idValue, out var id))
            {
                var nameAnchor = item.QuerySelector(CNCLabsConstants.DisplayNameAnchorSelector);
                var name = nameAnchor?.TextContent?.Trim();
                var detailsHref = nameAnchor?.GetAttribute(CNCLabsConstants.HrefAttribute);

                string? description = null;
                var descEl = item.QuerySelector(CNCLabsConstants.DescriptionSelector);
                if (descEl != null)
                {
                    var htmlDesc = descEl.InnerHtml;
                    description = CNCLabsHelper.NormalizeHtmlDescription(htmlDesc);
                }

                var authorStrong = item.QuerySelectorAll(CNCLabsConstants.DescriptionCellStrongSelector)
                    .FirstOrDefault(s => string.Equals(
                        s.TextContent?.Trim(),
                        CNCLabsConstants.AuthorLabelText,
                        StringComparison.OrdinalIgnoreCase));

                var author = CNCLabsHelper.GetNextNonEmptyTextSibling(authorStrong);

                // Attempt to parse Date, Size, Downloads from the specs block
                DateTime? lastUpdated = null;
                long? dlCount = null;
                string? fSize = null;

                var specsText = item.TextContent;

                // Typical structure: <strong>Author:</strong> Name <br> <strong>Size:</strong> 1.5 MB <br> <strong>Date:</strong> ...
                var strongs = item.QuerySelectorAll("strong");
                foreach (var s in strongs)
                {
                    var label = s.TextContent?.Trim();
                    if (string.IsNullOrEmpty(label)) continue;

                    var value = CNCLabsHelper.GetNextNonEmptyTextSibling(s);
                    if (string.IsNullOrEmpty(value)) continue;

                    if (label.StartsWith("Updated:", StringComparison.OrdinalIgnoreCase) ||
                        label.StartsWith("Added:", StringComparison.OrdinalIgnoreCase) ||
                        label.StartsWith("Date:", StringComparison.OrdinalIgnoreCase) ||
                        label.StartsWith("reviewed:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                        {
                            lastUpdated = parsed;
                        }
                    }
                    else if (label.StartsWith("Size:", StringComparison.OrdinalIgnoreCase))
                    {
                        fSize = value;
                    }
                    else if (label.StartsWith("Downloads:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (long.TryParse(value.Replace(",", string.Empty), out var count))
                        {
                            dlCount = count;
                        }
                    }
                }

                // If date is still missing, try a simpler regex on the whole cell text
                if (!lastUpdated.HasValue)
                {
                    var match = DateRegex().Match(specsText);
                    if (match.Success && DateTime.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallbackDate))
                    {
                        lastUpdated = fallbackDate;
                    }
                }

                // Try to find image in list item
                string? imgUrl = null;
                var img = item.QuerySelector(".screenshot img") ?? item.QuerySelector("img");
                if (img != null)
                {
                    var src = img.GetAttribute("src");
                    if (!string.IsNullOrEmpty(src))
                    {
                        imgUrl = src.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                            ? src
                            : new Uri(new Uri("https://www.cnclabs.com"), src).ToString();
                    }
                }

                mapList.Add(new MapListItem(id, name ?? string.Empty, description ?? string.Empty, author ?? CNCLabsConstants.DefaultAuthorName, detailsHref ?? string.Empty, query.TargetGame, query.ContentType, lastUpdated ?? DateTime.MinValue, dlCount, fSize, imgUrl, []));
            }
        }

        // Check for 'Next' button in pagination
        bool hasMoreItems = false;
        var pagingLinks = document.QuerySelectorAll(".paging a, .pager a, #ctl00_MainContent_Pager1 a, #ctl00_Main_NextPageLink, #ctl00_MainContent_NextPageLink, a[id*='NextPageLink']");

        if (pagingLinks.Length > 0)
        {
            _logger.LogInformation("[CNCLabs] Found {Count} paging links", pagingLinks.Length);
            foreach (var link in pagingLinks)
            {
                var text = link.TextContent.Trim();
                var href = link.GetAttribute("href");
                _logger.LogDebug("[CNCLabs] Paging link: Text='{Text}', Href='{Href}'", text, href);

                // Check for "Next" or "..."
                if (text.Contains("Next", StringComparison.OrdinalIgnoreCase) || text.Contains("...", StringComparison.Ordinal) || (href != null && href.Contains("page=" + (query.Page + 1))))
                {
                    _logger.LogInformation("[CNCLabs] Found Next/Ellipsis link match: {Text} (href: {Href})", text, href);
                    hasMoreItems = true;

                    // Don't break, keep logging for debug
                }

                // Check for page numbers greater than current
                if (int.TryParse(text, out var pNum))
                {
                    int currentPage = query.Page ?? 1;
                    if (pNum > currentPage)
                    {
                        _logger.LogInformation("[CNCLabs] Found page {PageNum} > current {CurrentPage}", pNum, currentPage);
                        hasMoreItems = true;
                    }
                }
            }
        }
        else
        {
            _logger.LogInformation("[CNCLabs] No paging links found using selectors: .paging a, .pager a, #ctl00_MainContent_Pager1 a, etc.");
        }

        return (mapList, hasMoreItems);
    }

    /// <summary>
    /// Downloads a C&amp;C Labs map details page and extracts the map's
    /// <c>Name</c>, <c>Description</c>, and <c>Author</c>.
    /// </summary>
    /// <param name="id">Map numeric identifier.</param>
    /// <param name="detailsPageUrl">
    /// Absolute (or resolvable) URL to the map details page,
    /// e.g. <c>https://www.cnclabs.com/downloads/details.aspx?id=3238</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the HTTP fetch and parsing work.</param>
    /// <returns>
    /// A tuple <c>(Name, Description, Author)</c>. If a field cannot be found,
    /// an empty string is returned for that field (never <c>null</c>).
    /// </returns>
    /// <remarks>
    /// Parsing strategy:
    /// <list type="number">
    /// <item><description>
    /// Name: try <see cref="CNCLabsConstants.NameSelector"/>; if missing,
    /// fall back to the last segment of <see cref="CNCLabsConstants.BreadcrumbHeaderSelector"/>
    /// split by <see cref="CNCLabsConstants.BreadcrumbSeparator"/>.
    /// </description></item>
    /// <item><description>
    /// Description: take the HTML from <see cref="CNCLabsConstants.DescriptionSelector"/>
    /// and normalize it with <c>CNCLabsHelper.NormalizeHtmlDescription</c>.
    /// </description></item>
    /// <item><description>
    /// Author: find a <c>&lt;strong&gt;</c> with text <see cref="CNCLabsConstants.AuthorLabelText"/>
    /// inside <see cref="CNCLabsConstants.AuthorLabelContainerSelector"/>, then read the next
    /// non-empty text node via <c>CNCLabsHelper.GetNextNonEmptyTextSibling</c>.
    /// </description></item>
    /// </list>
    /// </remarks>
    private async Task<MapListItem> GetMapDetailsAsync(int id, string detailsPageUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(detailsPageUrl))
        {
            throw new ArgumentException(CNCLabsConstants.UrlRequiredMessage, nameof(detailsPageUrl));
        }

        var html = await _httpClient.GetStringAsync(detailsPageUrl, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), cancellationToken).ConfigureAwait(false);

        // 1) Name (primary selector, then fallback to last breadcrumb segment)
        var name =
            document.QuerySelector(CNCLabsConstants.NameSelector)?.TextContent?.Trim()
            ?? document.QuerySelector(CNCLabsConstants.BreadcrumbHeaderSelector)
                       ?.TextContent?
                       .Split(CNCLabsConstants.BreadcrumbSeparator)
                       .LastOrDefault()?
                       .Trim()
            ?? string.Empty;

        // 2) Description (span id ends with _DescriptionLabel)
        var descEl = document.QuerySelector(CNCLabsConstants.DescriptionSelector);
        var description = descEl is null
            ? string.Empty
            : CNCLabsHelper.NormalizeHtmlDescription(descEl.InnerHtml) ?? string.Empty;

        // 3) Author (text node immediately after <strong>Author:</strong>)
        var authorStrong = document.QuerySelectorAll(CNCLabsConstants.AuthorLabelContainerSelector)
                                   .FirstOrDefault(s => string.Equals(
                                       s.TextContent?.Trim(),
                                       CNCLabsConstants.AuthorLabelText,
                                       StringComparison.OrdinalIgnoreCase));

        var author = CNCLabsHelper.GetNextNonEmptyTextSibling(authorStrong) ?? string.Empty;

        var (gameType, contentType) = CNCLabsHelper.ExtractBreadcrumbCategory(document);

        // 4) Date Parsing (try multiple labels)
        DateTime lastUpdated = DateTime.MinValue;
        var dateLabels = new[] { "Updated:", "Added:", "Submitted:", "reviewed:", "Date:" };

        foreach (var label in dateLabels)
        {
            var dateEl = document.QuerySelectorAll("strong").FirstOrDefault(e => e.TextContent.Contains(label, StringComparison.OrdinalIgnoreCase));
            if (dateEl != null)
            {
                var dateText = CNCLabsHelper.GetNextNonEmptyTextSibling(dateEl);
                if (!string.IsNullOrWhiteSpace(dateText) && DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                {
                    lastUpdated = parsedDate;
                    break;
                }
            }
        }

        // Parse additional metadata
        var docText = document.Body?.TextContent ?? string.Empty;

        long? downloadCount = null;
        string? fileSize = null;
        string? iconUrl = null;

        // Date Backup (Date submitted)
        if (lastUpdated == DateTime.MinValue)
        {
            var dateMatch = DateRegex().Match(docText);
            if (dateMatch.Success && DateTime.TryParse(dateMatch.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                lastUpdated = date;
            }
        }

        // File Size
        var sizeMatch = FileSizeRegex().Match(docText);
        if (sizeMatch.Success)
        {
            fileSize = sizeMatch.Groups[1].Value.Trim();
        }
        else
        {
            // Fallback for size if regex fails
            var sizeLabels = new[] { "File Size:", "Size:" };
            foreach (var label in sizeLabels)
            {
                var sizeEl = document.QuerySelectorAll("strong").FirstOrDefault(e => e.TextContent.Contains(label, StringComparison.OrdinalIgnoreCase));
                if (sizeEl != null)
                {
                    fileSize = CNCLabsHelper.GetNextNonEmptyTextSibling(sizeEl);
                    if (!string.IsNullOrEmpty(fileSize)) break;
                }
            }
        }

        // Download Count
        var downloadMatch = DownloadCountRegex().Match(docText);
        if (downloadMatch.Success)
        {
            var valGroup = !string.IsNullOrEmpty(downloadMatch.Groups[1].Value) ? 1 : 2;
            var val = downloadMatch.Groups[valGroup].Value;
            if (long.TryParse(val.Replace(",", string.Empty, StringComparison.Ordinal), out var dl))
            {
                downloadCount = dl;
            }
        }

        // Image
        var mainImage = document.QuerySelector("#ctl00_MainContent_Image1") ?? document.QuerySelector(".screenshot img") ?? document.QuerySelector("img[src*='preview']");
        if (mainImage != null)
        {
            var src = mainImage.GetAttribute("src");
            if (!string.IsNullOrEmpty(src))
            {
                iconUrl = src.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? src
                    : new Uri(new Uri("https://www.cnclabs.com"), src).ToString();
            }
        }

        // Tags parsing
        var tags = new List<string>();
        var taggedAsIdx = docText.IndexOf("Tagged as:", StringComparison.OrdinalIgnoreCase);
        if (taggedAsIdx != -1)
        {
            var tagLineEnd = docText.IndexOf('\n', taggedAsIdx);
            if (tagLineEnd == -1)
            {
                tagLineEnd = docText.Length;
            }

            var tagLine = docText[(taggedAsIdx + "Tagged as:".Length)..tagLineEnd].Trim();
            var parts = tagLine.Split(TagSeparator, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var t = part.Trim();
                if (!string.IsNullOrEmpty(t))
                {
                    tags.Add(t);
                }
            }
        }

        return new MapListItem(id, name, description, string.IsNullOrEmpty(author) ? CNCLabsConstants.DefaultAuthorName : author, detailsPageUrl, gameType, contentType, lastUpdated, downloadCount, fileSize, iconUrl, tags);
    }

    /// <summary>
    /// Small immutable record used internally to shuttle minimal map info between parsing and projection.
    /// </summary>
    /// <param name="Id">Map numeric identifier.</param>
    /// <param name="Name">Map display name.</param>
    /// <param name="Description">Short description text.</param>
    /// <param name="Author">Author display name.</param>
    /// <param name="DetailUrl">Absolute detail page URL.</param>
    /// <param name="TargetGame">Target game.</param>
    /// <param name="ContentType">Content type.</param>
    /// <param name="LastUpdated">Last updated date.</param>
    /// <param name="DownloadCount">Download count.</param>
    /// <param name="FileSize">File size string.</param>
    /// <param name="IconUrl">Icon/Preview image URL.</param>
    /// <param name="Tags">Tags associated with the map.</param>
    private sealed record MapListItem(
        int Id,
        string Name,
        string Description,
        string Author,
        string DetailUrl,
        GameType? TargetGame,
        ContentType? ContentType,
        DateTime LastUpdated,
        long? DownloadCount,
        string? FileSize,
        string? IconUrl,
        IEnumerable<string> Tags);

    private long? ParseFileSize(string size)
    {
        // Simple parser for "7.2 MB" etc if needed, or return generic
        // For now just return null as the UI uses the formatted string usually,
        // but ContentSearchResult.DownloadSize is Nullable<long> (bytes).
        // Let's try to parse simple cases.
        if (string.IsNullOrEmpty(size))
        {
            return null;
        }

        try
        {
            var parts = size.Trim().Split(' ');
            if (parts.Length >= 1 && double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            {
                var unit = parts.Length > 1 ? parts[1].Trim().ToUpperInvariant() : "B";
                long multiplier = unit switch
                {
                    "GB" => ConversionConstants.BytesPerGigabyte,
                    "MB" => ConversionConstants.BytesPerMegabyte,
                    "KB" => ConversionConstants.BytesPerKilobyte,
                    _ => 1,
                };
                return (long)(val * multiplier);
            }
        }
        catch (Exception ex)
        {
            // Logging failure to parse file size, though it's acceptable to return null
            // and fallback to the display string.
            _logger.LogWarning("Failed to parse file size '{Size}': {Error}", size, ex.Message);
        }

        return null;
    }
}
