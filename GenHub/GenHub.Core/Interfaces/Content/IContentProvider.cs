using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Defines a contract for a content provider that can discover, resolve, and prepare content.
/// </summary>
public interface IContentProvider : IContentSource
{
    /// <summary>
    /// Searches for content using the provider's pipeline.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of discovered and resolved content items.</returns>
    Task<ContentOperationResult<IEnumerable<ContentSearchResult>>> SearchAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a fully resolved manifest for a specific content ID.
    /// </summary>
    /// <param name="contentId">The unique identifier of the content.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A complete, validated game manifest ready for workspace preparation.</returns>
    Task<ContentOperationResult<ContentManifest>> GetContentAsync(
        string contentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prepares content for use by downloading, extracting, and validating it.
    /// </summary>
    /// <param name="manifest">The manifest describing the content to prepare.</param>
    /// <param name="targetDirectory">The directory to prepare the content in.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The final manifest with all content ready for workspace preparation.</returns>
    Task<ContentOperationResult<ContentManifest>> PrepareContentAsync(
        ContentManifest manifest,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
