using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Manifest;

/// <summary>
/// Persistent storage and management of acquired ContentManifests.
/// Provides high-level manifest operations and metadata management,
/// working alongside ICasService and IContentStorageService for complete content management.
/// </summary>
public interface IContentManifestPool
{
    /// <summary>
    /// Adds a ContentManifest to the pool after content acquisition.
    /// </summary>
    /// <param name="manifest">The content manifest to store.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation that returns an <see cref="OperationResult{T}"/> indicating success.</returns>
    Task<OperationResult<bool>> AddManifestAsync(ContentManifest manifest, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a ContentManifest to the pool and stores its content files from a source directory.
    /// </summary>
    /// <param name="manifest">The content manifest to store.</param>
    /// <param name="sourceDirectory">The directory containing the content files.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation that returns an <see cref="OperationResult{T}"/> indicating success.</returns>
    Task<OperationResult<bool>> AddManifestAsync(ContentManifest manifest, string sourceDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a specific ContentManifest from the pool by ID.
    /// </summary>
    /// <param name="manifestId">The unique identifier of the manifest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation that returns an <see cref="OperationResult{T}"/> containing the <see cref="ContentManifest"/> if found, or null otherwise.</returns>
    Task<OperationResult<ContentManifest?>> GetManifestAsync(ManifestId manifestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all acquired ContentManifests from the pool.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation that returns an <see cref="OperationResult{T}"/> containing a collection of all acquired <see cref="ContentManifest"/>s.</returns>
    Task<OperationResult<IEnumerable<ContentManifest>>> GetAllManifestsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for ContentManifests in the pool based on query criteria.
    /// </summary>
    /// <param name="query">The search criteria.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation that returns an <see cref="OperationResult{T}"/> containing a collection of matching <see cref="ContentManifest"/>s.</returns>
    Task<OperationResult<IEnumerable<ContentManifest>>> SearchManifestsAsync(ContentSearchQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a ContentManifest from the pool.
    /// </summary>
    /// <param name="manifestId">The unique identifier of the manifest to remove.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation that returns an <see cref="OperationResult{T}"/> indicating success.</returns>
    Task<OperationResult<bool>> RemoveManifestAsync(ManifestId manifestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a specific ContentManifest is already acquired and stored in the pool.
    /// </summary>
    /// <param name="manifestId">The unique identifier of the manifest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation that returns an <see cref="OperationResult{T}"/> indicating whether the manifest is acquired.</returns>
    Task<OperationResult<bool>> IsManifestAcquiredAsync(ManifestId manifestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the content directory path for a specific manifest.
    /// </summary>
    /// <param name="manifestId">The unique identifier of the manifest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation that returns an <see cref="OperationResult{T}"/> containing the path to the content directory if it exists, or null otherwise.</returns>
    Task<OperationResult<string?>> GetContentDirectoryAsync(ManifestId manifestId, CancellationToken cancellationToken = default);
}