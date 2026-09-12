using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Helpers;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// Discovers maps from CNC Labs website.
/// </summary>
[SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "CNCLabs base domain URL")]
[SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "CNC Labs domain casing")]
public partial class CNCLabsMapDiscoverer(HttpClient httpClient, ILogger<CNCLabsMapDiscoverer> logger) : IContentDiscoverer
{
    [GeneratedRegex(@"(\d+)\s*downloads|Downloads:\s*(\\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex DownloadCountRegex();

    [GeneratedRegex(@"/downloads/details/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex DetailsIdRegex();

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

            var (discoveredMaps, hasMoreItems) = await SearchByFiltersAsync(query, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                discoveredMaps = discoveredMaps
                    .Where(m => (m.Name?.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) == true) ||
                                (m.Description?.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) == true) ||
                                (m.Author?.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) == true))
                    .ToList();
            }

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

                ContentCardBadgeHelper.PromoteFromTags(res);
            }

            return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = results,
                HasMoreItems = hasMoreItems,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, CNCLabsConstants.DiscoveryFailureLogMessage);
            return OperationResult<ContentDiscoveryResult>.CreateFailure(string.Format(CNCLabsConstants.DiscoveryFailedErrorTemplate, ex.Message));
        }
    }

    private static MapListItem? ParseMapListItem(IElement item, ContentSearchQuery query)
    {
        var nameAnchor = item.QuerySelector(CNCLabsConstants.DisplayNameAnchorSelector);
        var detailsHref = nameAnchor?.GetAttribute(CNCLabsConstants.HrefAttribute);
        var name = nameAnchor?.TextContent?.Trim();
        if (string.IsNullOrWhiteSpace(detailsHref) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var idMatch = DetailsIdRegex().Match(detailsHref);
        if (!idMatch.Success || !int.TryParse(idMatch.Groups[1].Value, out var id))
        {
            return null;
        }

        detailsHref = new Uri(new Uri(CNCLabsConstants.PublisherWebsite), detailsHref).ToString();

        var description = CNCLabsHelper.NormalizeHtmlDescription(
            item.QuerySelector(CNCLabsConstants.DescriptionSelector)?.InnerHtml) ?? string.Empty;

        var author = item.QuerySelectorAll("span")
            .FirstOrDefault(s => s.QuerySelector(CNCLabsConstants.PersonIconSelector) != null)?
            .TextContent?.Trim();

        long? dlCount = null;
        var dlSpan = item.QuerySelectorAll("span")
            .FirstOrDefault(s => s.QuerySelector(CNCLabsConstants.DownloadIconSelector) != null);
        var dlMatch = DownloadCountRegex().Match(dlSpan?.TextContent ?? string.Empty);
        if (dlMatch.Success && long.TryParse(dlMatch.Groups[1].Value.Replace(",", string.Empty), out var dl))
        {
            dlCount = dl;
        }

        var fSize = item.QuerySelector(CNCLabsConstants.FileSizeSelector)?.TextContent?.Trim();

        string? imgUrl = null;
        var img = item.QuerySelector(CNCLabsConstants.ThumbnailSelector);
        var src = img?.GetAttribute("src");
        if (!string.IsNullOrEmpty(src))
        {
            imgUrl = src.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? src
                : new Uri(new Uri(CNCLabsConstants.PublisherWebsite), src).ToString();
        }

        var tags = item.QuerySelectorAll(CNCLabsConstants.BadgeSelector)
            .Select(b => b.TextContent?.Trim())
            .OfType<string>()
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();

        return new MapListItem(id, name, description, author ?? CNCLabsConstants.DefaultAuthorName, detailsHref, query.TargetGame, query.ContentType, DateTime.MinValue, dlCount, fSize, imgUrl, tags);
    }

    private static bool CheckPaginationHasMore(IDocument document, int currentPage)
    {
        var pagingLinks = document.QuerySelectorAll(CNCLabsConstants.PaginationLinkSelector);
        foreach (var link in pagingLinks)
        {
            var text = link.TextContent?.Trim() ?? string.Empty;
            if (text.Contains("Next", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (int.TryParse(text, out var pNum) && pNum > currentPage)
            {
                return true;
            }
        }

        return false;
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
        logger.LogInformation("[CNCLabs] Fetching from URL: {Url}", url);
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            throw new UriFormatException(CNCLabsConstants.InvalidAbsoluteUri);
        }

        var mapList = new List<MapListItem>();
        string html;
        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogInformation("[CNCLabs] URL returned 404 Not Found (no items or past last page): {Url}", url);
                return (mapList, false);
            }

            response.EnsureSuccessStatusCode();
            html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogInformation(ex, "[CNCLabs] URL returned 404 Not Found (no items or past last page): {Url}", url);
            return (mapList, false);
        }

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), cancellationToken).ConfigureAwait(false);
        var results = document.QuerySelectorAll(CNCLabsConstants.ListItemSelector);

        foreach (var item in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = ParseMapListItem(item, query);
            if (parsed != null)
            {
                mapList.Add(parsed);
            }
        }

        var currentPage = query.Page ?? 1;
        var hasMoreItems = CheckPaginationHasMore(document, currentPage);

        return (mapList, hasMoreItems);
    }

    private long? ParseFileSize(string size)
    {
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
            logger.LogWarning(ex, "Failed to parse file size '{Size}'", size);
        }

        return null;
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
}
