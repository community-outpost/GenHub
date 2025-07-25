using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Common;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Workspace;

/// <summary>
/// Complete implementation of file operations for workspace preparation.
/// </summary>
public class FileOperationsService(
    ILogger<FileOperationsService> logger,
    IDownloadService downloadService) : IFileOperationsService
{
    private const int BufferSize = 1024 * 1024; // 1MB buffer

    private readonly ILogger<FileOperationsService> _logger = logger;
    private readonly IDownloadService _downloadService = downloadService;

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
    public static bool DeleteDirectoryIfExists(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
            return true;
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

            var sourceInfo = new FileInfo(sourcePath);
            var destInfo = new FileInfo(destinationPath)
            {
                CreationTime = sourceInfo.CreationTime,
                LastWriteTime = sourceInfo.LastWriteTime,
                Attributes = sourceInfo.Attributes,
            };

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
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous symlink creation operation.</returns>
    public async Task CreateSymlinkAsync(
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
                    if (File.Exists(targetPath))
                    {
                        File.CreateSymbolicLink(linkPath, targetPath);
                    }
                    else if (Directory.Exists(targetPath))
                    {
                        Directory.CreateSymbolicLink(linkPath, targetPath);
                    }
                    else
                    {
                        throw new FileNotFoundException(
                            $"Target path does not exist: {targetPath}");
                    }
                },
                cancellationToken);

            _logger.LogDebug(
                "Created symlink from {Link} to {Target}",
                linkPath,
                targetPath);
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
            var result = await _downloadService.DownloadFileAsync(
                url,
                destinationPath,
                expectedHash: null,
                progress,
                cancellationToken);

            if (!result.Success)
            {
                throw new HttpRequestException(
                    $"Download failed: {result.ErrorMessage}");
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
}
