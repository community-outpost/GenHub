using GenHub.Core.Models.Common;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Defines a service for downloading files with progress reporting and verification.
/// </summary>
public interface IDownloadService
{
    /// <summary>
    /// Downloads a file from the specified URL.
    /// </summary>
    /// <param name="configuration">The download configuration.</param>
    /// <param name="progress">Progress reporter for download updates.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task containing the download result.</returns>
    Task<DownloadResult> DownloadFileAsync(
        DownloadConfiguration configuration,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a file from the specified URL with simplified parameters.
    /// </summary>
    /// <param name="url">The download URL.</param>
    /// <param name="destinationPath">The destination file path.</param>
    /// <param name="expectedHash">Optional expected hash for verification.</param>
    /// <param name="progress">Progress reporter for download updates.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task containing the download result.</returns>
    Task<DownloadResult> DownloadFileAsync(
        Uri url,
        string destinationPath,
        string? expectedHash = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the SHA256 hash of a file.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task containing the SHA256 hash string.</returns>
    Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default);
}
