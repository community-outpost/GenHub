using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Resolves dependencies that may come from different publishers.
/// Handles cross-publisher dependency resolution and catalog fetching.
/// </summary>
public interface ICrossPublisherDependencyResolver
{
    /// <summary>
    /// Checks which dependencies are missing and need installation.
    /// </summary>
    /// <param name="manifest">The manifest to check dependencies for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing list of missing dependencies.</returns>
    Task<OperationResult<IEnumerable<MissingDependency>>> CheckMissingDependenciesAsync(
        ContentManifest manifest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a catalog from an external publisher to resolve a dependency.
    /// </summary>
    /// <param name="catalogUrl">The URL of the catalog to fetch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing the fetched catalog.</returns>
    Task<OperationResult<PublisherCatalog>> FetchExternalCatalogAsync(
        string catalogUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds content that satisfies a dependency requirement.
    /// </summary>
    /// <param name="dependency">The dependency to find content for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing the matching content search result, or null if not found.</returns>
    Task<OperationResult<ContentSearchResult?>> FindDependencyContentAsync(
        ContentDependency dependency,
        CancellationToken cancellationToken = default);
}
