using GenHub.Core.Models.Common;
using GenHub.Core.Models.Manifest;
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
    /// Downloads multiple files in parallel.
    /// </summary>
    /// <param name="files">A dictionary mapping URLs to destination paths. Destination paths must be unique; if multiple URIs map to the same path, the last download will overwrite previous ones.</param>
    /// <param name="progress">Progress reporter for overall download progress.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary mapping destination file paths to their download results.</returns>
    Task<IDictionary<string, DownloadResult>> DownloadFilesAsync(
        IDictionary<Uri, string> files,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads files defined in a manifest.
    /// </summary>
    /// <param name="files">The files to download.</param>
    /// <param name="destinationDirectory">The destination directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The operation result.</returns>
    Task<OperationResult> DownloadFilesAsync(
        IEnumerable<ManifestFile> files,
        string destinationDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the SHA256 hash of a file.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task containing the SHA256 hash string.</returns>
    Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default);
}
