using System;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Core service for persistent content storage and retrieval in the GenHub ecosystem.
/// Provides centralized content-addressable storage with deduplication capabilities.
/// </summary>
public interface IContentStorageService
{
    /// <summary>
    /// Gets the root directory for all content storage.
    /// </summary>
    /// <returns>The absolute path to the content storage root.</returns>
    string GetContentStorageRoot();

    /// <summary>
    /// Gets the storage path for a specific manifest file.
    /// </summary>
    /// <param name="manifestId">The unique identifier of the manifest.</param>
    /// <returns>The absolute path where the manifest should be stored.</returns>
    string GetManifestStoragePath(ManifestId manifestId);

    /// <summary>
    /// Gets the content directory path for a specific manifest.
    /// </summary>
    /// <param name="manifestId">The unique identifier of the manifest.</param>
    /// <returns>The absolute path where the manifest's content files are stored.</returns>
    /// <remarks>
    /// This method is obsolete - content is now stored in CAS (Content Addressable Storage),
    /// not in Content/Data directories. Retained for backward compatibility only.
    /// </remarks>
    [Obsolete("Content is now stored in CAS, not in Content/Data directories. This method is retained for backward compatibility only.")]
    string GetContentDirectoryPath(ManifestId manifestId);

    /// <summary>
    /// Stores content from a source directory into permanent storage.
    /// </summary>
    /// <param name="manifest">The game manifest describing the content.</param>
    /// <param name="sourceDirectory">The temporary directory containing the content files.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result indicating success or failure with the updated manifest.</returns>
    Task<OperationResult<ContentManifest>> StoreContentAsync(
        ContentManifest manifest,
        string sourceDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves stored content to a target directory.
    /// </summary>
    /// <param name="manifestId">The unique identifier of the manifest.</param>
    /// <param name="targetDirectory">The directory where content should be extracted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<OperationResult<string>> RetrieveContentAsync(
        ManifestId manifestId,
        string targetDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if content for a specific manifest is stored.
    /// </summary>
    /// <param name="manifestId">The unique identifier of the manifest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True if the content is stored, false otherwise.</returns>
    Task<OperationResult<bool>> IsContentStoredAsync(ManifestId manifestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes stored content for a specific manifest.
    /// </summary>
    /// <param name="manifestId">The unique identifier of the manifest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<OperationResult<bool>> RemoveContentAsync(ManifestId manifestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets storage statistics and usage information.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="StorageStats"/> object describing usage under the content storage root.
    /// Fields include manifest count (logical manifests), total file count (all files under the storage root),
    /// total size in bytes, deduplication savings and available free disk space.
    /// </returns>
    Task<StorageStats> GetStorageStatsAsync(CancellationToken cancellationToken = default);
}