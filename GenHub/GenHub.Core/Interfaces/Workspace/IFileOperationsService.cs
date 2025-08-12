using System;
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
}
