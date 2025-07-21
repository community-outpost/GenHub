using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Workspace;

/// <summary>
/// Complete implementation of file operations for workspace preparation.
/// </summary>
public class FileOperationsService(ILogger<FileOperationsService> logger, HttpClient httpClient) : IFileOperationsService
{
    private readonly ILogger<FileOperationsService> _logger = logger;
    private readonly HttpClient _httpClient = httpClient;

    /// <summary>
    /// Copies a file from the source path to the destination path asynchronously.
    /// </summary>
    /// <param name="sourcePath">The source file path.</param>
    /// <param name="destinationPath">The destination file path.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous copy operation.</returns>
    public async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Handle large files with buffered copying
            const int bufferSize = 1024 * 1024; // 1MB buffer
            await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
            await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);

            await source.CopyToAsync(destination, cancellationToken);

            // Preserve file attributes and timestamps
            var sourceInfo = new FileInfo(sourcePath);
            var destInfo = new FileInfo(destinationPath)
            {
                CreationTime = sourceInfo.CreationTime,
                LastWriteTime = sourceInfo.LastWriteTime,
                Attributes = sourceInfo.Attributes,
            };

            _logger.LogDebug("Copied file from {Source} to {Destination}", sourcePath, destinationPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy file from {Source} to {Destination}", sourcePath, destinationPath);
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
    public async Task CreateSymlinkAsync(string linkPath, string targetPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(linkPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Delete existing file/link if it exists
            if (File.Exists(linkPath) || Directory.Exists(linkPath))
            {
                File.Delete(linkPath);
            }

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
                        throw new FileNotFoundException($"Target path does not exist: {targetPath}");
                    }
                },
                cancellationToken);

            _logger.LogDebug("Created symlink from {Link} to {Target}", linkPath, targetPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create symlink from {Link} to {Target}", linkPath, targetPath);
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
    public async Task CreateHardLinkAsync(string linkPath, string targetPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(linkPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(linkPath))
            {
                File.Delete(linkPath);
            }

            await Task.Run(
                () =>
            {
                if (OperatingSystem.IsWindows())
                {
                    if (!CreateHardLinkW(linkPath, targetPath, IntPtr.Zero))
                    {
                        throw new IOException($"Failed to create hard link from {linkPath} to {targetPath}");
                    }
                }
                else
                {
                    // Use Unix link() system call or fallback to copy
                    File.Copy(targetPath, linkPath, true);
                    _logger.LogWarning("Hard links not supported on this platform, fell back to copy for {Link}", linkPath);
                }
            }, cancellationToken);

            _logger.LogDebug("Created hard link from {Link} to {Target}", linkPath, targetPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create hard link from {Link} to {Target}", linkPath, targetPath);
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
    public async Task<bool> VerifyFileHashAsync(string filePath, string expectedHash, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            using var sha256 = SHA256.Create();
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
            var hashString = Convert.ToHexString(hash);

            var result = string.Equals(hashString, expectedHash, StringComparison.OrdinalIgnoreCase);
            _logger.LogDebug("Hash verification for {File}: {Result}", filePath, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify hash for {File}", filePath);
            return false;
        }
    }

    /// <summary>
    /// Downloads a file asynchronously.
    /// </summary>
    /// <param name="url">The URL of the file to download.</param>
    /// <param name="destinationPath">The destination file path.</param>
    /// <param name="progress">Progress reporter for download progress.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous download operation.</returns>
    public async Task DownloadFileAsync(string url, string destinationPath, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long totalBytesRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalBytesRead += bytesRead;

                progress?.Report(new DownloadProgress
                {
                    BytesReceived = totalBytesRead,
                    TotalBytes = totalBytes,
                });
            }

            _logger.LogInformation("Downloaded {Bytes} bytes from {Url} to {Destination}", totalBytesRead, url, destinationPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file from {Url} to {Destination}", url, destinationPath);
            throw;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateHardLinkW")]
    private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
}
