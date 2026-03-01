using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Features.Workspace;
using GenHub.Windows.Constants;
using Microsoft.Extensions.Logging;

namespace GenHub.Windows.Features.Workspace;

/// <summary>
/// Windows-specific implementation of <see cref="IFileOperationsService"/> for file operations.
/// </summary>
public partial class WindowsFileOperationsService(
    FileOperationsService baseService,
    ICasService casService,
    ILogger<WindowsFileOperationsService> logger) : IFileOperationsService
{
    /// <inheritdoc/>
    public Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
        => baseService.CopyFileAsync(sourcePath, destinationPath, cancellationToken);

    /// <inheritdoc/>
    public Task CreateSymlinkAsync(string linkPath, string targetPath, bool allowFallback = true, CancellationToken cancellationToken = default)
        => baseService.CreateSymlinkAsync(linkPath, targetPath, allowFallback, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> VerifyFileHashAsync(string filePath, string expectedHash, CancellationToken cancellationToken = default)
        => baseService.VerifyFileHashAsync(filePath, expectedHash, cancellationToken);

    /// <inheritdoc/>
    public Task DownloadFileAsync(Uri url, string destinationPath, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        => baseService.DownloadFileAsync(url, destinationPath, progress, cancellationToken);

    /// <inheritdoc/>
    public Task ApplyPatchAsync(string targetPath, string patchPath, CancellationToken cancellationToken = default)
        => baseService.ApplyPatchAsync(targetPath, patchPath, cancellationToken);

    /// <inheritdoc/>
    public Task<string?> StoreInCasAsync(string sourcePath, string? expectedHash = null, CancellationToken cancellationToken = default)
        => baseService.StoreInCasAsync(sourcePath, expectedHash, cancellationToken);

    /// <inheritdoc/>
    public async Task<bool> CopyFromCasAsync(string hash, string destinationPath, ContentType? contentType = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var pathResult = contentType.HasValue
                ? await casService.GetContentPathAsync(hash, contentType.Value, cancellationToken).ConfigureAwait(false)
                : await casService.GetContentPathAsync(hash, cancellationToken).ConfigureAwait(false);

            if (!pathResult.Success || pathResult.Data == null)
            {
                logger.LogError("CAS content not found for hash {Hash} for copy: {Error}", hash, pathResult.FirstError);
                return false;
            }

            await CopyFileAsync(pathResult.Data, destinationPath, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to copy from CAS for hash {Hash} to {TargetPath}", hash, destinationPath);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> LinkFromCasAsync(
        string hash,
        string destinationPath,
        bool useHardLink = false,
        ContentType? contentType = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pathResult = contentType.HasValue
                ? await casService.GetContentPathAsync(hash, contentType.Value, cancellationToken).ConfigureAwait(false)
                : await casService.GetContentPathAsync(hash, cancellationToken).ConfigureAwait(false);

            if (!pathResult.Success || pathResult.Data == null)
            {
                logger.LogError("CAS content not found for hash {Hash}: {Error}", hash, pathResult.FirstError);
                return false;
            }

            var casSourcePath = pathResult.Data;

            // For hard links, check if source and destination are on the same volume
            if (useHardLink)
            {
                var sameVolume = FileOperationsService.AreSameVolume(casSourcePath, destinationPath);
                var sourceRoot = sameVolume ? null : Path.GetPathRoot(casSourcePath);
                var destRoot = sameVolume ? null : Path.GetPathRoot(destinationPath);

                if (!sameVolume && contentType.HasValue)
                {
                    // Content is in wrong CAS pool (different volume), need to migrate it
                    logger.LogWarning(
                        "Content {Hash} found on volume {SourceVolume} but workspace is on {DestVolume}. Migrating content to correct CAS pool for hard link support.",
                        hash,
                        sourceRoot,
                        destRoot);

                    // Store the content in the correct pool (determined by contentType)
                    var migrateResult = await casService.StoreContentAsync(casSourcePath, contentType.Value, hash, cancellationToken).ConfigureAwait(false);
                    if (!migrateResult.Success)
                    {
                        logger.LogError("Failed to migrate content {Hash} to correct CAS pool: {Error}", hash, migrateResult.FirstError);
                        return false;
                    }

                    // Get the new path from the correct pool
                    pathResult = await casService.GetContentPathAsync(hash, contentType.Value, cancellationToken).ConfigureAwait(false);
                    if (!pathResult.Success || pathResult.Data == null)
                    {
                        logger.LogError("Failed to get migrated content path for hash {Hash}: {Error}", hash, pathResult.FirstError);
                        return false;
                    }

                    casSourcePath = pathResult.Data;
                    logger.LogInformation("Successfully migrated content {Hash} to correct CAS pool at {NewPath}", hash, casSourcePath);
                }
                else if (!sameVolume)
                {
                    // No content type provided and volumes differ - hard link will fail
                    var errorMessage = $"Cannot create hard link across different volumes/drives: Source={casSourcePath} (volume {sourceRoot}), Destination={destinationPath} (volume {destRoot})";
                    // Exception will be caught and logged by the outer catch block
                    throw new IOException(errorMessage);
                }
            }

            FileOperationsService.EnsureDirectoryExists(destinationPath);

            if (useHardLink)
            {
                // Attempt hard link directly - NO COPY FALLBACK allowed
                await CreateHardLinkAsync(destinationPath, casSourcePath, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await CreateSymlinkAsync(destinationPath, casSourcePath, allowFallback: true, cancellationToken).ConfigureAwait(false);
            }

            logger.LogDebug("Created {LinkType} from CAS hash {Hash} to {DestinationPath}", useHardLink ? "hard link" : "symlink", hash, destinationPath);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create {LinkType} from CAS hash {Hash} to {DestinationPath}", useHardLink ? "hard link" : "symlink", hash, destinationPath);
            return false;
        }
    }

    /// <inheritdoc/>
    public Task<Stream?> OpenCasContentAsync(string hash, CancellationToken cancellationToken = default)
        => baseService.OpenCasContentAsync(hash, cancellationToken);

    /// <inheritdoc/>
    public async Task CreateHardLinkAsync(
        string linkPath,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Normalize paths to absolute paths for Windows API compatibility
            var absoluteLinkPath = Path.GetFullPath(linkPath);
            var absoluteTargetPath = Path.GetFullPath(targetPath);

            // Ensure destination directory exists
            // Note: We pass the full file path to EnsureDirectoryExists which extracts and creates the parent directory
            FileOperationsService.EnsureDirectoryExists(absoluteLinkPath);

            FileOperationsService.DeleteFileIfExists(absoluteLinkPath);

            await Task.Run(
                () =>
                {
                    if (!CreateHardLinkW(absoluteLinkPath, absoluteTargetPath, IntPtr.Zero))
                    {
                        var errorCode = Marshal.GetLastWin32Error();
                        var errorMessage = Win32ErrorCodes.GetErrorMessage(errorCode);
                        throw new IOException(
                            $"Failed to create hard link from {absoluteLinkPath} to {absoluteTargetPath}: {errorMessage}");
                    }
                },
                cancellationToken);

            logger.LogDebug(
                "Created hard link from {Link} to {Target}",
                linkPath,
                targetPath);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to create hard link from {Link} to {Target}",
                linkPath,
                targetPath);
            throw;
        }
    }

    /// <summary>
    /// P/Invoke for Windows hard link creation.
    /// </summary>
    /// <param name="lpFileName">The name of the new hard link.</param>
    /// <param name="lpExistingFileName">The name of the existing file.</param>
    /// <param name="lpSecurityAttributes">Reserved, must be IntPtr.Zero.</param>
    /// <returns>True if successful, otherwise false.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLinkW(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);
}
