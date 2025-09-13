
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

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
    public string SourceName => "CNC Labs Maps";

    /// <summary>
    /// Gets the description for this discoverer.
    /// </summary>
    public string Description => "Discovers maps from CNC Labs website";

    /// <summary>
    /// Gets a value indicating whether this discoverer is enabled.
    /// </summary>
    public bool IsEnabled => true;

    /// <summary>
    /// Gets the capabilities of this discoverer.
    /// </summary>
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresDiscovery;

    /// <summary>
    /// Discovers maps from CNC Labs based on the search query.
    /// </summary>
    /// <param name="query">The search criteria.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="T:GenHub.Core.Models.Results.OperationResult"/> containing discovered maps.</returns>
    public async Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(ContentSearchQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            if (query == null)
            {
                return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure("Query cannot be null");
            }

            if (string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(Enumerable.Empty<ContentSearchResult>());
            }


            var searchUrl = $"http://search.cnclabs.com/?cse=labs&q={Uri.EscapeDataString(query.SearchTerm ?? string.Empty)}";
            var discoveredMaps = await CNCLabSearchAsync(searchUrl);
            var results = discoveredMaps.Select(map => new ContentSearchResult
            {
                Id = $"cnclabs.map.{map.id}",
                Name = map.name,
                Description = "Map from CNC Labs - full details available after resolution",
                AuthorName = map.author,
                ContentType = ContentType.MapPack,
                TargetGame = GameType.ZeroHour,
                ProviderName = SourceName,
                RequiresResolution = true,
                ResolverId = "CNCLabsMap",
                SourceUrl = map.detailUrl,
                ResolverMetadata = { ["mapId"] = map.id.ToString(), },
            });
            return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover maps from CNC Labs");
            return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure($"Discovery failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Crawls the C&amp;C Labs search results page and extracts map entries linking to a details page.
    /// </summary>
    /// <param name="url">
    /// Absolute URL of the C&amp;C Labs search results page to parse (e.g., a Google CSE results page on cnc-labs).
    /// </param>
    /// <param name="cancellationToken">A token to observe while awaiting the task.</param>
    /// <returns>
    /// A list of <see cref="MapListItem"/> entries discovered on the page. Only items that resolve to a
    /// details URL with a valid <c>id</c> query parameter are included.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="url"/> is null or empty.</exception>
    /// <exception cref="UriFormatException">Thrown if <paramref name="url"/> is not a valid absolute URI.</exception>
    /// <remarks>
    /// This method launches a headless Chromium instance via Playwright, navigates to the specified URL,
    /// and queries DOM nodes under <c>#search-results div.gsc-webResult.gsc-result</c>. It looks for anchors
    /// matching <c>div.gs-webResult.gs-result div.gsc-thumbnail-inside div.gs-title a.gs-title</c> and attempts
    /// to extract the canonical destination from the <c>data-ctorig</c> attribute. If the destination resembles
    /// <c>details.aspx?id=123</c>, the numeric <c>id</c> is parsed and used to construct a <see cref="MapListItem"/>.
    /// </remarks>          
    private async Task<List<MapListItem>> CNCLabSearchAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentNullException(nameof(url));

        if (!Uri.TryCreate(url, UriKind.Absolute, out var _))
            throw new UriFormatException("The provided URL is not a valid absolute URI.");

        var mapList = new List<MapListItem>();

        // Selectors kept as constants for maintainability.
        const string ResultSelector = "#search-results div.gsc-webResult.gsc-result";
        const string LinkSelector = "div.gs-webResult.gs-result div.gsc-thumbnail-inside div.gs-title a.gs-title";
        const string CanonicalHrefAttr = "data-ctorig";
        const string DetailsPathMarker = "details.aspx";
        const string GeneralsPathMarker = "maps/generals";

        // Playwright setup and navigation.
        using var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        }).ConfigureAwait(false);

        var context = await browser.NewContextAsync().ConfigureAwait(false);
        var page = await context.NewPageAsync().ConfigureAwait(false);

        // A sensible navigation timeout; adjust if the site is slow.
        page.SetDefaultNavigationTimeout(30_000);

        await page.GotoAsync(url).ConfigureAwait(false);

        // Grab all result containers.
        var results = await page.QuerySelectorAllAsync(ResultSelector).ConfigureAwait(false);

        foreach (var result in results)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Find the anchor that holds the title and canonical link.
            var linkHandle = await result.QuerySelectorAsync(LinkSelector).ConfigureAwait(false);
            if (linkHandle is null) continue;

            // Prefer the canonical target (data-ctorig) if present.
            var detailUrl = await linkHandle.GetAttributeAsync(CanonicalHrefAttr).ConfigureAwait(false)
                           ?? await linkHandle.GetAttributeAsync("href").ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(detailUrl))
                continue;

            var name = (await linkHandle.InnerTextAsync().ConfigureAwait(false))?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            // C&C Labs is the known author for these search results.
            const string author = "C&C Labs";

            // Try to extract numeric id from URLs like .../details.aspx?id=123
            if (TryExtractId(detailUrl, DetailsPathMarker, out var id))
            {
                mapList.Add(new MapListItem(id, name, author, detailUrl));
            }
            else
            {
                // For non-details results, you can branch based on path for future handling.
                var lower = detailUrl.ToLowerInvariant();
                if (lower.Contains(GeneralsPathMarker))
                {
                    // NOTE: This URL redirects to the Generals maps page (list view). If needed,
                    // call a dedicated extractor (e.g., ExtractMapsPageResultAsync) to harvest items.
                }
            }
        }

        return mapList;

        // ------- local helpers --------
        static bool TryExtractId(string targetUrl, string detailsMarker, out int id)
        {
            id = default;

            if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri))
                return false;

            // Quick path check to reduce false positives.
            if (!uri.AbsolutePath.ToLower(CultureInfo.InvariantCulture).Contains(detailsMarker))
                return false;

            var query = HttpUtility.ParseQueryString(uri.Query);
            var idValue = query.Get("id");
            return int.TryParse(idValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
        }
    }

    private record MapListItem(int id, string name, string author, string detailUrl);
}
