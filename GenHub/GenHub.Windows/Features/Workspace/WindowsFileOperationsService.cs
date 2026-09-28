using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Features.Workspace;
using GenHub.Windows.Constants;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Windows.Features.Workspace;

/// <summary>
/// Windows-specific implementation of <see cref="IFileOperationsService"/> for file operations.
/// </summary>
public partial class WindowsFileOperationsService(
    FileOperationsService baseService,
    ICasService casService,
    ILogger<WindowsFileOperationsService> logger)
    : DelegatingFileOperationsService(baseService, casService, logger)
{
    /// <inheritdoc/>
    public override async Task<bool> CopyFromCasAsync(string hash, string destinationPath, ContentType? contentType = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var pathResult = contentType.HasValue
                ? await CasService.GetContentPathAsync(hash, contentType.Value, cancellationToken).ConfigureAwait(false)
                : await CasService.GetContentPathAsync(hash, cancellationToken).ConfigureAwait(false);

            if (!pathResult.Success || pathResult.Data == null)
            {
                Logger.LogError("CAS content not found for hash {Hash} for copy: {Error}", hash, pathResult.FirstError);
                return false;
            }

            await CopyFileAsync(pathResult.Data, destinationPath, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Failed to copy from CAS for hash {Hash} to {TargetPath}", hash, destinationPath);
            return false;
        }
    }

    /// <inheritdoc/>
    public override async Task<bool> LinkFromCasAsync(
        string hash,
        string destinationPath,
        bool useHardLink = false,
        ContentType? contentType = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pathResult = contentType.HasValue
                ? await CasService.GetContentPathAsync(hash, contentType.Value, cancellationToken).ConfigureAwait(false)
                : await CasService.GetContentPathAsync(hash, cancellationToken).ConfigureAwait(false);

            if (!pathResult.Success || pathResult.Data == null)
            {
                Logger.LogError("CAS content not found for hash {Hash}: {Error}", hash, pathResult.FirstError);
                return false;
            }

            var casSourcePath = pathResult.Data;

            // For hard links, check if source and destination are on the same volume
            if (useHardLink)
            {
                var resolvedSource = await ResolveHardLinkSourceAsync(hash, casSourcePath, destinationPath, contentType, cancellationToken).ConfigureAwait(false);
                if (resolvedSource == null)
                {
                    return false;
                }

                casSourcePath = resolvedSource;
            }

            FileOperationsService.EnsureDirectoryExists(destinationPath);

            if (useHardLink)
            {
                // Attempt hard link directly - NO COPY FALLBACK allowed
                await CreateHardLinkAsync(destinationPath, casSourcePath, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await CreateSymlinkAsync(destinationPath, casSourcePath, allowFallback: false, cancellationToken).ConfigureAwait(false);
            }

            Logger.LogDebug("Created {LinkType} from CAS hash {Hash} to {DestinationPath}", useHardLink ? "hard link" : "symlink", hash, destinationPath);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Failed to create {LinkType} from CAS hash {Hash} to {DestinationPath}", useHardLink ? "hard link" : "symlink", hash, destinationPath);
            return false;
        }
    }

    /// <inheritdoc/>
    public override async Task CreateHardLinkAsync(
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

            Logger.LogDebug(
                "Created hard link from {Link} to {Target}",
                linkPath,
                targetPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(
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

    private async Task<string?> ResolveHardLinkSourceAsync(
        string hash,
        string casSourcePath,
        string destinationPath,
        ContentType? contentType,
        CancellationToken cancellationToken)
    {
        if (FileOperationsService.AreSameVolume(casSourcePath, destinationPath))
        {
            return casSourcePath;
        }

        if (!contentType.HasValue)
        {
            Logger.LogWarning(
                "Cannot create hard link across different volumes/drives without content type (Source={SourceRoot}, Destination={DestRoot}) for hash {Hash}",
                Path.GetPathRoot(casSourcePath),
                Path.GetPathRoot(destinationPath),
                hash);
            return null;
        }

        return await MigrateContentToLocalPoolAsync(hash, casSourcePath, destinationPath, contentType.Value, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> MigrateContentToLocalPoolAsync(
        string hash,
        string casSourcePath,
        string destinationPath,
        ContentType contentType,
        CancellationToken cancellationToken)
    {
        // Content is in wrong CAS pool (different volume), need to migrate it
        Logger.LogWarning(
            "Content {Hash} found on volume {SourceVolume} but workspace is on {DestVolume}. Migrating content to correct CAS pool for hard link support.",
            hash,
            Path.GetPathRoot(casSourcePath),
            Path.GetPathRoot(destinationPath));

        // Store the content in the correct pool (determined by contentType)
        var migrateResult = await CasService.StoreContentAsync(casSourcePath, contentType, hash, cancellationToken).ConfigureAwait(false);
        if (!migrateResult.Success)
        {
            Logger.LogWarning(
                "Failed to migrate content {Hash} to correct CAS pool: {Error}",
                hash,
                migrateResult.FirstError);
            return null;
        }

        var newPathResult = await CasService.GetContentPathAsync(hash, contentType, cancellationToken).ConfigureAwait(false);
        if (!newPathResult.Success || newPathResult.Data == null)
        {
            Logger.LogWarning(
                "Migrated content {Hash} to CAS pool but failed to retrieve new path: {Error}",
                hash,
                newPathResult.FirstError ?? "Retrieved CAS content path was null after successful migration.");
            return null;
        }

        var migratedPath = newPathResult.Data;
        if (!FileOperationsService.AreSameVolume(migratedPath, destinationPath))
        {
            Logger.LogWarning(
                "Cannot create hard link across different volumes/drives (Source={SourceRoot}, Destination={DestRoot}) for hash {Hash}",
                Path.GetPathRoot(migratedPath),
                Path.GetPathRoot(destinationPath),
                hash);
            return null;
        }

        Logger.LogInformation("Successfully migrated content {Hash} to correct CAS pool at {NewPath}", hash, migratedPath);
        return migratedPath;
    }
}
