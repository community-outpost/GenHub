using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Discovers content from external sources and returns searchable results.
/// </summary>
/// <remarks>
/// <para>
/// Discoverers are responsible for fetching raw catalog data from external URLs and
/// delegating parsing to <see cref="ICatalogParser"/> implementations. They handle
/// network concerns like timeouts, retries, and error handling.
/// </para>
/// <para>
/// Discoverers should not parse raw data themselves (that's the parser's job), create
/// manifests (resolver), or download content files (deliverer).
/// </para>
/// <para>
/// Pipeline: Discoverer → Parser → Resolver → Deliverer → Factory.
/// </para>
/// </remarks>
public interface IContentDiscoverer : IContentSource
{
    /// <summary>
    /// Discovers content items from this source. Typically fetches catalog data and
    /// delegates to a parser, then applies search filters to the results.
    /// </summary>
    /// <param name="query">Search criteria to filter results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Discovered content items matching the query.</returns>
    Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers content using configuration from a provider definition.
    /// </summary>
    /// <param name="provider">
    /// Provider definition with endpoints and configuration. Falls back to constants if null.
    /// </param>
    /// <param name="query">Search criteria to filter results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Discovered content items matching the query.</returns>
    Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(
        ProviderDefinition? provider,
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        return DiscoverAsync(query, cancellationToken);
    }
}