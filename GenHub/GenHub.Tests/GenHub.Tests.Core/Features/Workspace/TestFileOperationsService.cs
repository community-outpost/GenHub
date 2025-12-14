using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// A test-specific implementation of FileOperationsService that supports HardLinks on Windows.
/// This mimics the behavior of WindowsFileOperationsService but avoids referencing the WinExe project.
/// </summary>
public class TestFileOperationsService(
    ILogger<FileOperationsService> logger,
    IDownloadService downloadService,
    ICasService casService) : FileOperationsService(logger, downloadService, casService)
{
    private readonly ILogger<FileOperationsService> _logger = logger;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    /// <inheritdoc/>
    public override async Task CreateHardLinkAsync(
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
}
