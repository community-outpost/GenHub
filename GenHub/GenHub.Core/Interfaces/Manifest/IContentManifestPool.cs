using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Interfaces.Manifest;

/// <summary>
/// Persistent storage and management of acquired ContentManifests.
/// Provides high-level manifest operations and metadata management,
/// working alongside ICasService and IContentStorageService for complete content management.
/// </summary>
public interface IContentManifestPool
{
    /// <summary>
    /// Stores a ContentManifest and its content files in persistent storage,
    /// and registers it in the pool.
    /// </summary>
    /// <param name="manifest">The content manifest to store.</param>
    /// <param name="sourceDirectory">The directory containing the content files.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AddManifestAsync(ContentManifest manifest, string sourceDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a specific ContentManifest from the pool by ID.
    /// </summary>
    /// <param name="manifestId">The unique identifier of the manifest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The game manifest if found, null otherwise.</returns>
    Task<ContentManifest?> GetManifestAsync(string manifestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all acquired ContentManifests from the pool.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A collection of all acquired game manifests.</returns>
    Task<IEnumerable<ContentManifest>> GetAllManifestsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for ContentManifests in the pool based on query criteria.
    /// </summary>
    /// <param name="query">The search criteria.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A collection of matching game manifests.</returns>
    Task<IEnumerable<ContentManifest>> SearchManifestsAsync(ContentSearchQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a ContentManifest from the pool.
    /// </summary>
    /// <param name="manifestId">The unique identifier of the manifest to remove.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RemoveManifestAsync(string manifestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a specific ContentManifest is already acquired and stored in the pool.
    /// </summary>
    /// <param name="manifestId">The unique identifier of the manifest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True if the manifest is acquired, false otherwise.</returns>
    Task<bool> IsManifestAcquiredAsync(string manifestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the content directory path for a specific manifest.
    /// </summary>
    /// <param name="manifestId">The unique identifier of the manifest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The path to the content directory if it exists, null otherwise.</returns>
    Task<string?> GetContentDirectoryAsync(string manifestId, CancellationToken cancellationToken = default);
}
