using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Service for creating ContentManifests from local directories.
/// </summary>
public interface ILocalContentService
{
    /// <summary>
    /// Creates a ContentManifest from a local directory.
    /// </summary>
    /// <param name="directoryPath">The path to the local directory.</param>
    /// <param name="name">The display name for the content.</param>
    /// <param name="contentType">The type of content.</param>
    /// <param name="targetGame">The target game for this content.</param>
    /// <param name="sourcePath">Optional original source path of the content.</param>
    /// <param name="progress">Optional progress reporter for tracking manifest creation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the created manifest or errors.</returns>
    Task<OperationResult<ContentManifest>> CreateLocalContentManifestAsync(
        string directoryPath,
        string name,
        ContentType contentType,
        GameType targetGame,
        string? sourcePath = null,
        IProgress<ContentStorageProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds local content by creating and storing a manifest.
    /// Wrapper for CreateLocalContentManifestAsync with simplified parameter order.
    /// </summary>
    /// <param name="name">The display name for the content.</param>
    /// <param name="directoryPath">The path to the local directory.</param>
    /// <param name="contentType">The type of content.</param>
    /// <param name="targetGame">The target game for this content.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result containing the created manifest or errors.</returns>
    Task<OperationResult<ContentManifest>> AddLocalContentAsync(
        string name,
        string directoryPath,
        ContentType contentType,
        GameType targetGame,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes local content by removing its manifest and potentially deleting files.
    /// </summary>
    /// <param name="manifestId">The manifest ID of the content to delete.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<OperationResult> DeleteLocalContentAsync(string manifestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing local content item.
    /// </summary>
    /// <param name="existingManifestId">The ID of the manifest to update.</param>
    /// <param name="name">The new display name.</param>
    /// <param name="directoryPath">The path to the content directory.</param>
    /// <param name="contentType">The content type.</param>
    /// <param name="targetGame">The target game.</param>
    /// <param name="sourcePath">Optional original source path of the content.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the updated manifest.</returns>
    Task<OperationResult<ContentManifest>> UpdateLocalContentManifestAsync(
        string existingManifestId,
        string name,
        string directoryPath,
        ContentType contentType,
        GameType targetGame,
        string? sourcePath = null,
        IProgress<ContentStorageProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the allowed content types for local content creation.
    /// </summary>
    IReadOnlyList<ContentType> AllowedContentTypes { get; }
}
