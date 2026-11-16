using GenHub.Core.Models.Content;
using GenHub.Core.Models.Results;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Steam;

/// <summary>
/// Downloads Workshop items directly from Steam via HTTP or SteamKit2 CDN client.
/// </summary>
public interface ISteamWorkshopClientDownloader
{
    /// <summary>
    /// Downloads the files for a workshop item directly into the target staging directory.
    /// </summary>
    /// <param name="appId">The Steam application ID.</param>
    /// <param name="publishedFileId">The workshop item published file ID.</param>
    /// <param name="targetDirectory">The staging directory to write the downloaded files to.</param>
    /// <param name="progress">Progress reporter for bytes downloaded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success result if downloaded, failure result otherwise.</returns>
    Task<OperationResult<bool>> DownloadWorkshopItemAsync(
        int appId,
        string publishedFileId,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
