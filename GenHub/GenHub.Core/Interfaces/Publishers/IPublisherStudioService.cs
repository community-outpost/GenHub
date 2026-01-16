using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Publishers;

/// <summary>
/// Service for managing Publisher Studio projects and catalogs.
/// </summary>
public interface IPublisherStudioService
{
    /// <summary>
    /// Creates a new publisher project.
    /// </summary>
    /// <param name="name">The project name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing the created project.</returns>
    Task<OperationResult<PublisherStudioProject>> CreateProjectAsync(
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads an existing project from disk.
    /// </summary>
    /// <param name="path">The project file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing the loaded project.</returns>
    Task<OperationResult<PublisherStudioProject>> LoadProjectAsync(
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a project to disk.
    /// </summary>
    /// <param name="project">The project to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> SaveProjectAsync(
        PublisherStudioProject project,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports the catalog to JSON.
    /// </summary>
    /// <param name="project">The project containing the catalog.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing the JSON string.</returns>
    Task<OperationResult<string>> ExportCatalogAsync(
        PublisherStudioProject project,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a catalog for correctness.
    /// </summary>
    /// <param name="catalog">The catalog to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating if the catalog is valid.</returns>
    Task<OperationResult<bool>> ValidateCatalogAsync(
        PublisherCatalog catalog,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a subscription URL for a catalog.
    /// </summary>
    /// <param name="catalogUrl">The URL where the catalog is hosted.</param>
    /// <returns>The genhub:// subscription URL.</returns>
    string GenerateSubscriptionUrl(string catalogUrl);

    /// <summary>
    /// Exports the provider definition to JSON.
    /// </summary>
    /// <param name="project">The project to export definition for.</param>
    /// <param name="primaryCatalogUrl">The primary URL where the catalog will be hosted.</param>
    /// <param name="catalogMirrorUrls">Optional list of mirror URLs for the catalog.</param>
    /// <param name="definitionUrl">The URL where this definition file will be hosted (for self-updates).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing the JSON string.</returns>
    Task<OperationResult<string>> ExportProviderDefinitionAsync(
        PublisherStudioProject project,
        string primaryCatalogUrl,
        IEnumerable<string>? catalogMirrorUrls,
        string definitionUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that all artifacts in the catalog have valid, reachable URLs.
    /// </summary>
    /// <param name="catalog">The catalog to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success or failure with details.</returns>
    Task<OperationResult<bool>> ValidateArtifactUrlsAsync(
        PublisherCatalog catalog,
        CancellationToken cancellationToken = default);
}
