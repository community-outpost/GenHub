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
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// Discovers maps from CNC Labs website.
/// </summary>
public class CNCLabsMapDiscoverer(HttpClient httpClient, ILogger<CNCLabsMapDiscoverer> logger) : IContentDiscoverer
{
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

            List<MapListItem> discoveredMaps =
                   !string.IsNullOrWhiteSpace(query.SearchTerm)
                       ? await SearchByTextAsync(query.SearchTerm, cancellationToken).ConfigureAwait(false)
                       : await SearchByFiltersAsync(query, cancellationToken).ConfigureAwait(false);

            var results = discoveredMaps.Select(map => new ContentSearchResult
            {
                Id = string.Format(CNCLabsConstants.MapIdFormat, map.Id),
                Name = map.Name,
                Description = CNCLabsConstants.MapDescriptionTemplate,
                AuthorName = map.Author,
                ContentType = map.ContentType ?? ContentType.UnknownContentType,
                TargetGame = map.TargetGame ?? GameType.Unknown,
                ProviderName = SourceName,
                RequiresResolution = true,
                ResolverId = CNCLabsConstants.ResolverId,
                SourceUrl = map.DetailUrl,
                ResolverMetadata =
                {
                    [CNCLabsConstants.MapIdMetadataKey] = map.Id.ToString(),
                },
            });
            var list = results.ToList();
            return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = list,
                TotalItems = list.Count,
                HasMoreItems = false,
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
    /// <returns>A list of minimally populated map list items.</returns>
    private async Task<List<MapListItem>> SearchByTextAsync(
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

        return mapList;
    }

    /// <summary>
    /// Performs a structured search using server-rendered list pages (no headless browser).
    /// </summary>
    /// <param name="query">Structured query containing target game and content type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of map list items parsed from the list page.</returns>
    private async Task<List<MapListItem>> SearchByFiltersAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var url = CNCLabsHelper.BuildSearchUrl(query);
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

                mapList.Add(new MapListItem(id, name ?? string.Empty, description ?? string.Empty, author ?? CNCLabsConstants.DefaultAuthorName, detailsHref ?? string.Empty, query.TargetGame, query.ContentType));
            }
        }

        return mapList;
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
            throw new ArgumentException(CNCLabsConstants.UrlRequiredMessage, nameof(detailsPageUrl));

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

        return new MapListItem(id, name, description, string.IsNullOrEmpty(author) ? CNCLabsConstants.DefaultAuthorName : author, detailsPageUrl, gameType, contentType);
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
    private sealed record MapListItem(int Id, string Name, string Description, string Author, string DetailUrl, GameType? TargetGame, ContentType? ContentType);
}
