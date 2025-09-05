using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

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

            var searchUrl = $"https://search.cnclabs.com/?cse=labs&q={Uri.EscapeDataString(query.SearchTerm ?? string.Empty)}";
            var response = await _httpClient.GetStringAsync(searchUrl, cancellationToken);
            var discoveredMaps = ParseMapListPage(response);
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
    /// Parses the HTML response from CNC Labs to extract map list items.
    /// </summary>
    /// <param name="html">The HTML content to parse.</param>
    /// <returns>A list of map list items.</returns>
    private List<MapListItem> ParseMapListPage(string html)
    {
        // TODO: Implement actual HTML parsing logic
        return new List<MapListItem>();
    }

    private record MapListItem(int id, string name, string author, string detailUrl);
}
