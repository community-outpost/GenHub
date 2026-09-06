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
}
