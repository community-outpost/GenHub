using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.GitHub;

namespace GenHub.Core.Interfaces.AppUpdate;

/// <summary>
/// Interface for downloading and installing application updates.
/// </summary>
public interface IUpdateInstaller
{
    /// <summary>
    /// Downloads and installs an update from the specified URL.
    /// </summary>
    /// <param name="downloadUrl">The URL to download the update from.</param>
    /// <param name="progress">Progress reporter for download and installation status.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>True if the update was downloaded and installed successfully; otherwise, false.</returns>
    Task<bool> DownloadAndInstallAsync(
        string downloadUrl,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the appropriate download URL from a collection of release assets based on the current platform.
    /// </summary>
    /// <param name="assets">The collection of release assets to choose from.</param>
    /// <returns>The download URL for the current platform, or null if no suitable asset is found.</returns>
    string? GetPlatformDownloadUrl(IEnumerable<GitHubReleaseAsset> assets);
}