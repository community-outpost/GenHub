using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Manifest;

/// <summary>
/// Persistent storage and management of acquired ContentManifests.
/// This is a temporary interface that will be replaced by a dedicated CAS system later.
/// </summary>
public interface IContentManifestPool
{
    /// <summary>
    /// Adds a ContentManifest to the pool after content acquisition.
    /// </summary>
    /// <param name="manifest">The game manifest to store.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task<OperationResult<bool>> AddManifestAsync(ContentManifest manifest, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a specific ContentManifest from the pool by ID.
    /// </summary>
    /// <param name="manifestId">The unique identifier of the manifest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The game manifest if found, null otherwise.</returns>
    Task<OperationResult<ContentManifest?>> GetManifestAsync(string manifestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all acquired ContentManifests from the pool.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A collection of all acquired game manifests.</returns>
    Task<OperationResult<IEnumerable<ContentManifest>>> GetAllManifestsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for ContentManifests in the pool based on query criteria.
    /// </summary>
    /// <param name="query">The search criteria.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A collection of matching game manifests.</returns>
    Task<OperationResult<IEnumerable<ContentManifest>>> SearchManifestsAsync(ContentSearchQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a ContentManifest from the pool.
    /// </summary>
    /// <param name="manifestId">The unique identifier of the manifest to remove.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task<OperationResult<bool>> RemoveManifestAsync(string manifestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a specific ContentManifest is already acquired and stored in the pool.
    /// </summary>
    /// <param name="manifestId">The unique identifier of the manifest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True if the manifest is acquired, false otherwise.</returns>
    Task<OperationResult<bool>> IsManifestAcquiredAsync(string manifestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the content directory path for a specific manifest.
    /// </summary>
    /// <param name="manifestId">The unique identifier of the manifest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The path to the content directory if it exists, null otherwise.</returns>
    Task<OperationResult<string?>> GetContentDirectoryAsync(string manifestId, CancellationToken cancellationToken = default);
}
