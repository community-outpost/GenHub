using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Orchestrates the high-level download flow, including acquisition, state updates, and notifications.
/// </summary>
public interface IContentDownloadCoordinator
{
    /// <summary>
    /// Gets a value indicating whether any download is currently in flight.
    /// </summary>
    bool HasActiveDownloads { get; }

    /// <summary>
    /// Downloads content, updates state, and shows notifications.
    /// </summary>
    /// <param name="searchResult">The content search result to download.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The acquired manifest if successful.</returns>
    Task<OperationResult<ContentManifest>> DownloadContentAsync(
        ContentSearchResult searchResult,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether content is currently being downloaded.
    /// </summary>
    /// <param name="searchResult">The content search result to check.</param>
    /// <returns>True if a download is in-flight; otherwise false.</returns>
    bool IsDownloading(ContentSearchResult searchResult);

    /// <summary>
    /// Attempts to retrieve current progress for an in-flight download.
    /// </summary>
    /// <param name="searchResult">The content search result.</param>
    /// <param name="progressPercentage">The reported progress percentage (0-100).</param>
    /// <param name="statusMessage">The reported status message.</param>
    /// <returns>True if an in-flight download exists; otherwise false.</returns>
    bool TryGetDownloadProgress(ContentSearchResult searchResult, out double progressPercentage, out string statusMessage);
}
