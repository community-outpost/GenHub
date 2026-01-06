using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Defines a contract for a service that orchestrates content discovery, resolution, and delivery using content providers.
/// </summary>
public interface IContentOrchestrator
{
    /// <summary>
    /// Searches for content across all enabled and applicable content discoverers.
    /// </summary>
    /// <param name="query">The search criteria.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="OperationResult{T}"/> containing an aggregated list of <see cref="ContentSearchResult"/>.</returns>
    Task<OperationResult<IEnumerable<ContentSearchResult>>> SearchAsync(ContentSearchQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a <see cref="ContentSearchResult"/> item into a full <see cref="ContentManifest"/>.
    /// </summary>
    /// <param name="providerName">The name of the provider.</param>
    /// <param name="contentId">The unique identifier of the content.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result object containing the game manifest.</returns>
    Task<OperationResult<ContentManifest>> GetContentManifestAsync(
        string providerName,
        string contentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires content and stores ContentManifest in pool for later profile usage.
    /// This separates content acquisition from workspace preparation, which is handled by GameProfile/GameLauncher.
    /// </summary>
    /// <param name="searchResult">The content search result to acquire.</param>
    /// <param name="progress">Optional progress reporter for acquisition status.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result object containing the acquired game manifest.</returns>
    Task<OperationResult<ContentManifest>> AcquireContentAsync(
        ContentSearchResult searchResult,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all acquired content manifests from the pool.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result object containing all acquired game manifests.</returns>
    Task<OperationResult<IEnumerable<ContentManifest>>> GetAcquiredContentAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes acquired content from the pool.
    /// </summary>
    /// <param name="manifestId">The unique identifier of the manifest to remove.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result indicating success or failure of the removal operation.</returns>
    Task<OperationResult<bool>> RemoveAcquiredContentAsync(
        string manifestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of featured or popular content.
    /// </summary>
    /// <param name="contentType">Optional filter by content type.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result object containing information about the featured content.</returns>
    Task<OperationResult<IEnumerable<ContentSearchResult>>> GetFeaturedContentAsync(
        ContentType? contentType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of all currently available content providers.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result object containing information about the available content providers.</returns>
    Task<OperationResult<IEnumerable<IContentProvider>>> GetAvailableProvidersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a new content provider with the service.
    /// </summary>
    /// <param name="provider">The content provider to register.</param>
    void RegisterProvider(IContentProvider provider);

    /// <summary>
    /// Unregisters a content provider from the service.
    /// </summary>
    /// <param name="providerName">The name of the provider to unregister.</param>
    void UnregisterProvider(string providerName);
}