using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Common;

namespace GenHub.Core.Interfaces.Workspace;

/// <summary>
/// Defines file operations for workspace management.
/// </summary>
public interface IFileOperationsService
{
    /// <summary>
    /// Copies a file from the source path to the destination path asynchronously.
    /// </summary>
    /// <param name="sourcePath">The source file path.</param>
    /// <param name="destinationPath">The destination file path.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous copy operation.</returns>
    Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a symbolic link asynchronously.
    /// </summary>
    /// <param name="linkPath">The path of the symbolic link.</param>
    /// <param name="targetPath">The target path the link points to.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous symlink creation operation.</returns>
    Task CreateSymlinkAsync(
        string linkPath,
        string targetPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a hard link asynchronously.
    /// </summary>
    /// <param name="linkPath">The path of the hard link.</param>
    /// <param name="targetPath">The target path the link points to.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous hard link creation operation.</returns>
    Task CreateHardLinkAsync(
        string linkPath,
        string targetPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the hash of a file asynchronously.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="expectedHash">The expected hash value.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if the hash matches; otherwise, false.</returns>
    Task<bool> VerifyFileHashAsync(
        string filePath,
        string expectedHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a patch to a target file. The patch format is determined by the implementation.
    /// </summary>
    /// <param name="targetPath">The file to be patched.</param>
    /// <param name="patchPath">The path to the patch definition file.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous patch operation.</returns>
    Task ApplyPatchAsync(string targetPath, string patchPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a file asynchronously.
    /// </summary>
    /// <param name="url">The URL of the file to download.</param>
    /// <param name="destinationPath">The destination file path.</param>
    /// <param name="progress">Progress reporter for download progress.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous download operation.</returns>
    Task DownloadFileAsync(
        string url,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a file in CAS and returns its hash.
    /// </summary>
    /// <param name="sourcePath">The path to the source file.</param>
    /// <param name="expectedHash">Optional expected hash for verification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The content hash if successful.</returns>
    Task<string?> StoreInCasAsync(string sourcePath, string? expectedHash = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies a file from CAS to the specified destination path using its hash.
    /// The destination path determines the final filename and location.
    /// </summary>
    /// <param name="hash">The content hash in CAS.</param>
    /// <param name="destinationPath">The destination file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the operation succeeded; otherwise, false.</returns>
    Task<bool> CopyFromCasAsync(string hash, string destinationPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a link (hard or symbolic) from CAS to the specified destination path.
    /// The destination path determines the final filename and location.
    /// </summary>
    /// <param name="hash">The content hash in CAS.</param>
    /// <param name="destinationPath">The destination file path.</param>
    /// <param name="useHardLink">Whether to use a hard link instead of symbolic link.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the operation succeeded.</returns>
    Task<bool> LinkFromCasAsync(string hash, string destinationPath, bool useHardLink = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a stream to content stored in CAS.
    /// </summary>
    /// <param name="hash">The content hash in CAS.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A stream to read the content, or null if not found.</returns>
    Task<Stream?> OpenCasContentAsync(string hash, CancellationToken cancellationToken = default);
}
