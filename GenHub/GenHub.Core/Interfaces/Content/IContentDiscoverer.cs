using GenHub.Core.Models.Content;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Defines a contract for a service that discovers potential content from a specific source.
/// </summary>
public interface IContentDiscoverer : IContentSource
{
    /// <summary>
    /// Discovers potential content items that can be resolved into full ContentSearchResult objects.
    /// </summary>
    /// <param name="query">The search criteria to apply during discovery.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="T:GenHub.Core.Models.Results.OperationResult"/> containing discovered content search results.</returns>
    Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(ContentSearchQuery query, CancellationToken cancellationToken = default);
}