using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Represents a complete content source that orchestrates discovery, resolution, and delivery
/// for a specific content provider (e.g., "GitHub", "ModDB", "Local Files").
/// This is a high-level interface that composes the specialized pipeline components.
/// </summary>
public interface IContentProvider : IContentSource
{
    /// <summary>
    /// Searches for content using this provider's complete pipeline.
    /// This orchestrates discovery -> resolution -> validation.
    /// </summary>
    /// <param name="query">The search criteria.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Fully resolved content search results ready for installation.</returns>
    Task<OperationResult<IEnumerable<ContentSearchResult>>> SearchAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a complete manifest for specific content, handling the full pipeline if needed.
    /// </summary>
    /// <param name="contentId">The unique identifier of the content.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A complete, validated game manifest ready for workspace preparation.</returns>
    Task<OperationResult<ContentManifest>> GetValidatedContentAsync(
        string contentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prepares content for installation by handling acquisition and delivery.
    /// This orchestrates the full content delivery pipeline.
    /// </summary>
    /// <param name="manifest">The manifest describing the content to prepare.</param>
    /// <param name="targetDirectory">The directory where content should be prepared.</param>
    /// <param name="progress">Optional progress reporting.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The final manifest with all content ready for workspace preparation.</returns>
    Task<OperationResult<ContentManifest>> PrepareContentAsync(
        ContentManifest manifest,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}