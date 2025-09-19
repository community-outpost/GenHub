using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Manifest;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// CSV content discoverer that discovers content from CSV files.
/// Implements <see cref="IContentDiscoverer"/> for CSV-based content discovery.
/// </summary>
/// <remarks>
/// This class provides discovery capabilities for CSV-based content sources.
/// It creates <see cref="ContentSearchResult"/> objects with metadata pointing to CSV files
/// that can be resolved by the corresponding CSV resolver.
/// The discoverer supports different game types and versions, providing appropriate
/// CSV URLs and metadata for each supported content type.
/// </remarks>
public class CSVDiscoverer(
    ILogger<CSVDiscoverer> logger,
    ManifestDiscoveryService manifestDiscoveryService,
    IConfigurationProviderService configurationProvider) : IContentDiscoverer
{
    private readonly ILogger<CSVDiscoverer> _logger = logger;
    private readonly ManifestDiscoveryService _manifestDiscoveryService = manifestDiscoveryService;
    private readonly IConfigurationProviderService _configurationProvider = configurationProvider;

    /// <summary>
    /// Gets the resolver identifier for CSV content.
    /// </summary>
    /// <returns>A string identifier used to match this discoverer with appropriate resolvers.</returns>
    /// <remarks>
    /// This identifier is used by the content resolution system to associate discovered
    /// <see cref="ContentSearchResult"/> items with the appropriate resolver implementation.
    /// The value "CSVResolver" indicates that discovered items should be resolved using CSV-specific logic.
    /// </remarks>
    public static string ResolverId => CsvConstants.CsvResolverId;

    /// <inheritdoc />
    public string SourceName => CsvConstants.CsvSourceName;

    /// <inheritdoc />
    public string Description => "Discovers content from CSV files.";

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.SupportsManifestGeneration |
        ContentSourceCapabilities.DirectSearch |
        ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <summary>
    /// Discovers content from CSV files based on the search query.
    /// </summary>
    /// <param name="query">The search criteria containing game type and other filters.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="OperationResult{T}"/> containing discovered <see cref="ContentSearchResult"/> objects.</returns>
    /// <remarks>
    /// This method examines the <paramref name="query"/> to determine what type of content to discover.
    /// Currently supports discovery of:
    /// <list type="bullet">
    /// <item><see cref="GameType.Generals"/> version 1.08 content</item>
    /// <item><see cref="GameType.ZeroHour"/> version 1.04 content</item>
    /// </list>
    /// For each supported game type, it creates a <see cref="ContentSearchResult"/> with appropriate
    /// metadata including CSV URLs and resolver information. The results can then be resolved
    /// by the corresponding CSV resolver to obtain full <see cref="ContentManifest"/> objects.
    /// </remarks>
    public Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ContentSearchResult>();

        // Generals 1.08
        if (query.TargetGame == GameType.Generals)
        {
            var searchResult = new ContentSearchResult
            {
                Id = "generals-1.08",
                Name = "Command & Conquer Generals 1.08",
                ResolverId = ResolverId,
            };
            searchResult.ResolverMetadata.Add("game", "Generals");
            searchResult.ResolverMetadata.Add("version", "1.08");
            searchResult.ResolverMetadata.Add("csvUrl", CsvConstants.DefaultGeneralsCsvUrl);
            results.Add(searchResult);
        }

        // Zero Hour 1.04
        if (query.TargetGame == GameType.ZeroHour)
        {
            var searchResult = new ContentSearchResult
            {
                Id = "zerohour-1.04",
                Name = "Command & Conquer Generals: Zero Hour 1.04",
                ResolverId = ResolverId,
            };
            searchResult.ResolverMetadata.Add("game", "ZeroHour");
            searchResult.ResolverMetadata.Add("version", "1.04");
            searchResult.ResolverMetadata.Add("csvUrl", CsvConstants.DefaultZeroHourCsvUrl);
            results.Add(searchResult);
        }

        return Task.FromResult(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(results));
    }
}
