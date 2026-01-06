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
    /// <param name="progress">Optional progress reporter for tracking manifest creation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the created manifest or errors.</returns>
    Task<OperationResult<ContentManifest>> CreateLocalContentManifestAsync(
        string directoryPath,
        string name,
        ContentType contentType,
        GameType targetGame,
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
    /// <returns>A result containing the created manifest or errors.</returns>
    Task<OperationResult<ContentManifest>> AddLocalContentAsync(
        string name,
        string directoryPath,
        ContentType contentType,
        GameType targetGame);

    /// <summary>
    /// Gets the allowed content types for local content creation.
    /// </summary>
    IReadOnlyList<ContentType> AllowedContentTypes { get; }
}
