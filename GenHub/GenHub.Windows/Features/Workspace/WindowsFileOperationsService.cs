using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Common;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Windows.Features.Workspace;

/// <summary>
/// Windows-specific implementation of <see cref="IFileOperationsService"/> for file operations.
/// </summary>
/// <param name="baseService">The base file operations service.</param>
/// <param name="logger">The logger instance.</param>
public class WindowsFileOperationsService(FileOperationsService baseService, ILogger<WindowsFileOperationsService> logger) : IFileOperationsService
{
    private readonly FileOperationsService _baseService = baseService ?? throw new ArgumentNullException(nameof(baseService));
    private readonly ILogger<WindowsFileOperationsService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
        => _baseService.CopyFileAsync(sourcePath, destinationPath, cancellationToken);

    /// <inheritdoc/>
    public Task CreateSymlinkAsync(string linkPath, string targetPath, bool allowFallback = true, CancellationToken cancellationToken = default)
        => _baseService.CreateSymlinkAsync(linkPath, targetPath, allowFallback, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> VerifyFileHashAsync(string filePath, string expectedHash, CancellationToken cancellationToken = default)
        => _baseService.VerifyFileHashAsync(filePath, expectedHash, cancellationToken);

    /// <inheritdoc/>
    public Task DownloadFileAsync(string url, string destinationPath, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        => _baseService.DownloadFileAsync(url, destinationPath, progress, cancellationToken);

    /// <inheritdoc/>
    public Task ApplyPatchAsync(string targetPath, string patchPath, CancellationToken cancellationToken = default)
        => _baseService.ApplyPatchAsync(targetPath, patchPath, cancellationToken);

    /// <inheritdoc/>
    public Task<string?> StoreInCasAsync(string sourcePath, string? expectedHash = null, CancellationToken cancellationToken = default)
        => _baseService.StoreInCasAsync(sourcePath, expectedHash, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> CopyFromCasAsync(string hash, string destinationPath, CancellationToken cancellationToken = default)
        => _baseService.CopyFromCasAsync(hash, destinationPath, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> LinkFromCasAsync(string hash, string destinationPath, bool useHardLink = false, CancellationToken cancellationToken = default)
        => _baseService.LinkFromCasAsync(hash, destinationPath, useHardLink, cancellationToken);

    /// <inheritdoc/>
    public Task<Stream?> OpenCasContentAsync(string hash, CancellationToken cancellationToken = default)
        => _baseService.OpenCasContentAsync(hash, cancellationToken);

    /// <inheritdoc/>
    public async Task CreateHardLinkAsync(
        string linkPath,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(linkPath);
            if (directory != null)
            {
                FileOperationsService.EnsureDirectoryExists(directory);
            }

            FileOperationsService.DeleteFileIfExists(linkPath);

            await Task.Run(
                () =>
                {
                    if (!CreateHardLinkW(linkPath, targetPath, IntPtr.Zero))
                    {
                        throw new IOException(
                            $"Failed to create hard link from {linkPath} to {targetPath}");
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
    /// P/Invoke for Windows hard link creation.
    /// </summary>
    /// <param name="lpFileName">The name of the new hard link.</param>
    /// <param name="lpExistingFileName">The name of the existing file.</param>
    /// <param name="lpSecurityAttributes">Reserved, must be IntPtr.Zero.</param>
    /// <returns>True if successful, otherwise false.</returns>
    [DllImport(
        "kernel32.dll",
        SetLastError = true,
        CharSet = CharSet.Unicode,
        EntryPoint = "CreateHardLinkW")]
    private static extern bool CreateHardLinkW(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);
}