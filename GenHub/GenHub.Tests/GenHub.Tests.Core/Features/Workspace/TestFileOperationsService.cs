using System.Runtime.InteropServices;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Common;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// A test-specific implementation of FileOperationsService that supports HardLinks on Windows.
/// This mimics the behavior of WindowsFileOperationsService but avoids referencing the WinExe project.
/// </summary>
public class TestFileOperationsService : IFileOperationsService
{
    private readonly FileOperationsService _innerService;
    private readonly ILogger<FileOperationsService> _logger;
    private readonly ICasService _casService;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestFileOperationsService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="downloadService">The download service.</param>
    /// <param name="casService">The CAS service.</param>
    public TestFileOperationsService(
        ILogger<FileOperationsService> logger,
        IDownloadService downloadService,
        ICasService casService)
    {
        _logger = logger;
        _casService = casService;
        _innerService = new FileOperationsService(logger, downloadService, casService);
    }

    /// <inheritdoc/>
    public async Task CreateHardLinkAsync(
        string linkPath,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            FileOperationsService.EnsureDirectoryExists(linkPath);
            FileOperationsService.DeleteFileIfExists(linkPath);

            await Task.Run(
                () =>
                {
                    if (OperatingSystem.IsWindows())
                    {
                        if (!CreateHardLink(linkPath, targetPath, IntPtr.Zero))
                        {
                            var error = Marshal.GetLastWin32Error();
                            throw new IOException($"Failed to create hard link. Win32 Error: {error}");
                        }
                    }
                    else
                    {
                        File.Copy(targetPath, linkPath, true);
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

    /// <inheritdoc/>
    public Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
        => _innerService.CopyFileAsync(sourcePath, destinationPath, cancellationToken);

    /// <inheritdoc/>
    public Task CreateSymlinkAsync(string linkPath, string targetPath, bool allowFallback = true, CancellationToken cancellationToken = default)
        => _innerService.CreateSymlinkAsync(linkPath, targetPath, allowFallback, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> VerifyFileHashAsync(string filePath, string expectedHash, CancellationToken cancellationToken = default)
        => _innerService.VerifyFileHashAsync(filePath, expectedHash, cancellationToken);

    /// <inheritdoc/>
    public Task ApplyPatchAsync(string targetPath, string patchPath, CancellationToken cancellationToken = default)
        => _innerService.ApplyPatchAsync(targetPath, patchPath, cancellationToken);

    /// <inheritdoc/>
    public Task DownloadFileAsync(Uri url, string destinationPath, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        => _innerService.DownloadFileAsync(url, destinationPath, progress, cancellationToken);

    /// <inheritdoc/>
    public Task<string?> StoreInCasAsync(string sourcePath, string? expectedHash = null, CancellationToken cancellationToken = default)
        => _innerService.StoreInCasAsync(sourcePath, expectedHash, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> CopyFromCasAsync(string hash, string destinationPath, CancellationToken cancellationToken = default)
        => _innerService.CopyFromCasAsync(hash, destinationPath, cancellationToken);

    /// <inheritdoc/>
    public async Task<bool> LinkFromCasAsync(string hash, string destinationPath, bool useHardLink = false, CancellationToken cancellationToken = default)
    {
        if (useHardLink)
        {
            try
            {
                var pathResult = await _casService.GetContentPathAsync(hash, cancellationToken).ConfigureAwait(false);
                if (!pathResult.Success || pathResult.Data == null)
                {
                    _logger.LogError("CAS content not found for hash {Hash}: {Error}", hash, pathResult.FirstError);
                    return false;
                }

                if (!File.Exists(pathResult.Data))
                {
                    _logger.LogError("CAS file does not exist at path {Path} for hash {Hash}", pathResult.Data, hash);
                    return false;
                }

                FileOperationsService.EnsureDirectoryExists(destinationPath);

                await CreateHardLinkAsync(destinationPath, pathResult.Data, cancellationToken).ConfigureAwait(false);

                _logger.LogDebug("Created hard link from CAS hash {Hash} to {DestinationPath}", hash, destinationPath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create hard link from CAS hash {Hash} to {DestinationPath}", hash, destinationPath);
                return false;
            }
        }

        return await _innerService.LinkFromCasAsync(hash, destinationPath, useHardLink, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Stream?> OpenCasContentAsync(string hash, CancellationToken cancellationToken = default)
        => _innerService.OpenCasContentAsync(hash, cancellationToken);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
}
