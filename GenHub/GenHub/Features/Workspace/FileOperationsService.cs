using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Common;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Workspace;

/// <summary>
/// Complete implementation of file operations for workspace preparation.
/// </summary>
public class FileOperationsService(
    ILogger<FileOperationsService> logger,
    IDownloadService downloadService,
    ICasService casService) : IFileOperationsService
{
    private const int BufferSize = 1024 * 1024; // 1MB buffer

    private readonly ILogger<FileOperationsService> _logger = logger;
    private readonly IDownloadService _downloadService = downloadService;
    private readonly ICasService _casService = casService;

    /// <summary>
    /// Ensures that the directory for the specified file path exists, creating it if necessary.
    /// </summary>
    /// <param name="filePath">The file path whose directory should be ensured.</param>
    /// <returns>True if the filePath is valid; otherwise, false.</returns>
    public static bool EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Deletes the specified file if it exists.
    /// </summary>
    /// <param name="filePath">The path of the file to delete.</param>
    /// <returns>True if the file exists; otherwise, false.</returns>
    public static bool DeleteFileIfExists(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Deletes the specified directory and all its contents if it exists.
    /// </summary>
    /// <param name="directoryPath">The path of the directory to delete.</param>
    /// <returns>True if the directory was deleted; otherwise, false.</returns>
    /// <exception cref="IOException">Thrown when files are locked by another process.</exception>
    public static bool DeleteDirectoryIfExists(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
        {
            try
            {
                Directory.Delete(directoryPath, recursive: true);
                return true;
            }
            catch (IOException ex) when (ex.Message.Contains("being used by another process"))
            {
                throw new IOException(
                    $"Cannot delete directory '{directoryPath}' because files are being used by another process. " +
                    "Please ensure all applications using files in this directory are closed before deleting.",
                    ex);
            }
        }

        return false;
    }

    /// <summary>
    /// Copies a file from the source path to the destination path asynchronously.
    /// </summary>
    /// <param name="sourcePath">The source file path.</param>
    /// <param name="destinationPath">The destination file path.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous copy operation.</returns>
    public async Task CopyFileAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureDirectoryExists(destinationPath);

            // If destination exists and is a symlink/reparse point, delete it first
            // This prevents issues when switching from Symlink strategy to FullCopy strategy
            if (File.Exists(destinationPath))
            {
                var destInfo = new FileInfo(destinationPath);
                if (destInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    _logger.LogDebug("Removing existing symlink at {Destination} before copying", destinationPath);
                    destInfo.Delete();
                }
            }

            await using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                useAsync: true);
            await using var destination = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                useAsync: true);
            await source.CopyToAsync(destination, cancellationToken);

            // Safely try to copy timestamps/attributes but do not fail copy if this part fails
            try
            {
                var sourceInfo = new FileInfo(sourcePath);
                var destInfo = new FileInfo(destinationPath);

                // Timestamps
                destInfo.CreationTime = sourceInfo.CreationTime;
                destInfo.LastWriteTime = sourceInfo.LastWriteTime;

                // Attributes - avoid reparse/read-only/system flags
                var sanitizedAttributes = sourceInfo.Attributes
                    & ~FileAttributes.ReparsePoint
                    & ~FileAttributes.ReadOnly
                    & ~FileAttributes.System;
                destInfo.Attributes = sanitizedAttributes;
            }
            catch (Exception attrEx)
            {
                _logger.LogDebug(attrEx, "Non-fatal: failed to copy timestamps/attributes from {Source} to {Destination}", sourcePath, destinationPath);
            }

            _logger.LogDebug(
                "Copied file from {Source} to {Destination}",
                sourcePath,
                destinationPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to copy file from {Source} to {Destination}",
                sourcePath,
                destinationPath);
            throw;
        }
    }

    /// <summary>
    /// Creates a symbolic link asynchronously.
    /// </summary>
    /// <param name="linkPath">The path of the symbolic link.</param>
    /// <param name="targetPath">The target path the link points to.</param>
    /// <param name="allowFallback">Whether to fall back to copying if symlink creation fails.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous symlink creation operation.</returns>
    public virtual async Task CreateSymlinkAsync(
        string linkPath,
        string targetPath,
        bool allowFallback = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureDirectoryExists(linkPath);
            DeleteFileIfExists(linkPath);

            var absoluteTargetPath = Path.GetFullPath(targetPath);
            if (!File.Exists(absoluteTargetPath) && !Directory.Exists(absoluteTargetPath))
            {
                throw new FileNotFoundException(
                    $"Target path does not exist: {absoluteTargetPath}");
            }

            await Task.Run(
                () =>
                {
                    try
                    {
                        if (File.Exists(absoluteTargetPath))
                        {
                            File.CreateSymbolicLink(linkPath, absoluteTargetPath);
                        }
                        else if (Directory.Exists(absoluteTargetPath))
                        {
                            Directory.CreateSymbolicLink(linkPath, absoluteTargetPath);
                        }
                        else
                        {
                            throw new FileNotFoundException(
                                $"Target path does not exist: {absoluteTargetPath}");
                        }
                    }
                    catch (UnauthorizedAccessException uaex) when (OperatingSystem.IsWindows())
                    {
                        if (allowFallback)
                        {
                            // Fall back to copy if symlink creation requires elevation or Developer Mode
                            _logger.LogWarning(uaex, "Symlink creation not permitted on Windows. Falling back to file copy for {LinkPath}", linkPath);
                            FallbackToCopyIfPossible(absoluteTargetPath, linkPath);
                        }
                        else
                        {
                            throw;
                        }
                    }
                    catch (IOException ioex) when (OperatingSystem.IsWindows())
                    {
                        if (allowFallback)
                        {
                            // Fall back to copy if symlink creation fails due to privilege or filesystem issues
                            _logger.LogWarning(ioex, "Symlink creation failed on Windows. Falling back to file copy for {LinkPath}", linkPath);
                            FallbackToCopyIfPossible(absoluteTargetPath, linkPath);
                        }
                        else
                        {
                            throw;
                        }
                    }
                    catch (PlatformNotSupportedException pnsex)
                    {
                        if (allowFallback)
                        {
                            // Fall back if platform lacks symlink support
                            _logger.LogWarning(pnsex, "Symlink creation not supported on this platform. Falling back to file copy for {LinkPath}", linkPath);

                            if (File.Exists(absoluteTargetPath))
                            {
                                File.Copy(absoluteTargetPath, linkPath, overwrite: true);
                            }
                            else
                            {
                                throw;
                            }
                        }
                        else
                        {
                            throw;
                        }
                    }
                },
                cancellationToken);

            _logger.LogDebug(
                "Created symlink or copied file from {Link} to {Target}",
                linkPath,
                absoluteTargetPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create symlink from {Link} to {Target}",
                linkPath,
                targetPath);
            throw;
        }
    }

    /// <summary>
    /// Creates a hard link asynchronously.
    /// </summary>
    /// <param name="linkPath">The path of the hard link.</param>
    /// <param name="targetPath">The target path the link points to.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous hard link creation operation.</returns>
    public async Task CreateHardLinkAsync(
        string linkPath,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureDirectoryExists(linkPath);
            DeleteFileIfExists(linkPath);

            await Task.Run(
                () =>
                {
                    if (OperatingSystem.IsWindows())
                    {
                        // Use platform-specific implementation
                        throw new NotImplementedException("Hard link creation should be handled by platform-specific service");
                    }
                    else
                    {
                        File.Copy(targetPath, linkPath, true);
                        _logger.LogWarning(
                            "Hard links not supported on this platform, fell back to copy for {Link}",
                            linkPath);
                    }
                },
                cancellationToken);

            _logger.LogDebug(
                "Created hard link from {Link} to {Target}",
                linkPath,
                targetPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create hard link from {Link} to {Target}",
                linkPath,
                targetPath);
            throw;
        }
    }

    /// <summary>
    /// Verifies the hash of a file asynchronously.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="expectedHash">The expected hash value.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if the hash matches; otherwise, false.</returns>
    public async Task<bool> VerifyFileHashAsync(
        string filePath,
        string expectedHash,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            var actualHash = await _downloadService.ComputeFileHashAsync(
                filePath,
                cancellationToken);
            var result = string.Equals(
                actualHash,
                expectedHash,
                StringComparison.OrdinalIgnoreCase);

            _logger.LogDebug(
                "Hash verification for {File}: {Result}",
                filePath,
                result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify hash for {File}", filePath);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task ApplyPatchAsync(string targetPath, string patchPath, CancellationToken cancellationToken = default)
    {
        // TODO: This is a placeholder for a real patch implementation.
        // A real implementation would read the patch file and apply transformations
        // to the target file. For example, using a library for diff/patch or JSON Patch.
        _logger.LogInformation("Applying patch {PatchPath} to {TargetPath}", patchPath, targetPath);

        if (!File.Exists(targetPath))
        {
            throw new FileNotFoundException("Target file for patching does not exist.", targetPath);
        }

        if (!File.Exists(patchPath))
        {
            throw new FileNotFoundException("Patch file does not exist.", patchPath);
        }

        // Example: Simple text append for demonstration.
        // A real implementation would be much more complex.
        var patchContent = await File.ReadAllTextAsync(patchPath, cancellationToken);
        await File.AppendAllTextAsync(targetPath, patchContent, cancellationToken);

        _logger.LogDebug("Successfully applied patch to {TargetPath}", targetPath);
    }

    /// <summary>
    /// Downloads a file asynchronously using the download service.
    /// </summary>
    /// <param name="url">The URL of the file to download.</param>
    /// <param name="destinationPath">The destination file path.</param>
    /// <param name="progress">Progress reporter for download progress.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous download operation.</returns>
    public async Task DownloadFileAsync(
        string url,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureDirectoryExists(destinationPath);

            var result = await _downloadService.DownloadFileAsync(
                new DownloadConfiguration { Url = url, DestinationPath = destinationPath },
                progress,
                cancellationToken);
            if (!result.Success)
            {
                throw new HttpRequestException(
                    $"Download failed: {result.FirstError}");
            }

            _logger.LogInformation(
                "Downloaded {Bytes} bytes from {Url} to {Destination}",
                result.BytesDownloaded,
                url,
                destinationPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to download file from {Url} to {Destination}",
                url,
                destinationPath);
            throw;
        }
    }

    /// <summary>
    /// Stores a file in CAS and returns its hash.
    /// </summary>
    /// <param name="sourcePath">The path to the source file.</param>
    /// <param name="expectedHash">Optional expected hash for verification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The content hash if successful.</returns>
    public async Task<string?> StoreInCasAsync(
        string sourcePath,
        string? expectedHash = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _casService.StoreContentAsync(sourcePath, expectedHash, cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                _logger.LogDebug("Stored file {SourcePath} in CAS with hash {Hash}", sourcePath, result.Data);
                return result.Data;
            }

            _logger.LogError("Failed to store file {SourcePath} in CAS: {Error}", sourcePath, result.FirstError);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception storing file {SourcePath} in CAS", sourcePath);
            return null;
        }
    }

    /// <summary>
    /// Copies a file from CAS to the specified destination path using its hash.
    /// The destination path determines the final filename and location.
    /// </summary>
    /// <param name="hash">The content hash in CAS.</param>
    /// <param name="destinationPath">The destination file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the operation succeeded.</returns>
    public async Task<bool> CopyFromCasAsync(
        string hash,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pathResult = await _casService.GetContentPathAsync(hash, cancellationToken).ConfigureAwait(false);
            if (!pathResult.Success || pathResult.Data == null)
            {
                _logger.LogError("CAS content not found for hash {Hash}", hash);
                return false;
            }

            EnsureDirectoryExists(destinationPath);

            await CopyFileAsync(pathResult.Data, destinationPath, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("Copied from CAS hash {Hash} to {DestinationPath}", hash, destinationPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy from CAS hash {Hash} to {DestinationPath}", hash, destinationPath);
            return false;
        }
    }

    /// <summary>
    /// Creates a link (hard or symbolic) from CAS to the specified destination path.
    /// The destination path determines the final filename and location.
    /// </summary>
    /// <param name="hash">The content hash in CAS.</param>
    /// <param name="destinationPath">The destination file path.</param>
    /// <param name="useHardLink">Whether to use a hard link instead of symbolic link.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the operation succeeded.</returns>
    public async Task<bool> LinkFromCasAsync(
        string hash,
        string destinationPath,
        bool useHardLink = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pathResult = await _casService.GetContentPathAsync(hash, cancellationToken).ConfigureAwait(false);
            if (!pathResult.Success || pathResult.Data == null)
            {
                _logger.LogError("CAS content not found for hash {Hash}: {Error}", hash, pathResult.FirstError);
                return false;
            }

            // Verify the CAS file actually exists before trying to link
            if (!File.Exists(pathResult.Data))
            {
                _logger.LogError("CAS file does not exist at path {Path} for hash {Hash}", pathResult.Data, hash);
                return false;
            }

            EnsureDirectoryExists(destinationPath);

            if (useHardLink)
            {
                await CreateHardLinkAsync(destinationPath, pathResult.Data, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await CreateSymlinkAsync(destinationPath, pathResult.Data, useHardLink ? false : true, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogDebug("Created {LinkType} from CAS hash {Hash} to {DestinationPath}", useHardLink ? "hard link" : "symlink", hash, destinationPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create {LinkType} from CAS hash {Hash} to {DestinationPath}", useHardLink ? "hard link" : "symlink", hash, destinationPath);
            return false;
        }
    }

    /// <summary>
    /// Opens a stream to CAS content by hash.
    /// </summary>
    /// <param name="hash">The content hash in CAS.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A stream to read the content, or null if not found.</returns>
    public async Task<Stream?> OpenCasContentAsync(
        string hash,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var streamResult = await _casService.OpenContentStreamAsync(hash, cancellationToken).ConfigureAwait(false);
            if (streamResult.Success)
            {
                return streamResult.Data;
            }

            _logger.LogError("Failed to open CAS content stream for hash {Hash}: {Error}", hash, streamResult.FirstError);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception opening CAS content stream for hash {Hash}", hash);
            return null;
        }
    }

    /// <summary>
    /// Helper method to fallback to file copy if source file exists.
    /// </summary>
    /// <param name="sourcePath">The source file path.</param>
    /// <param name="destinationPath">The destination file path.</param>
    private static void FallbackToCopyIfPossible(string sourcePath, string destinationPath)
    {
        if (File.Exists(sourcePath))
        {
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
        else
        {
            throw new FileNotFoundException($"Source file not found for fallback copy: {sourcePath}");
        }
    }
}
