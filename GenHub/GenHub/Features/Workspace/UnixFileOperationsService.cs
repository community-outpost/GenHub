using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Workspace;

/// <summary>
/// Hard-link support for Linux and macOS, decorating <see cref="FileOperationsService"/>.
/// <para>
/// Without this, <c>CreateHardLinkAsync</c> on any Unix platform fell through to
/// <c>File.Copy</c> and logged a warning. The default workspace strategy is
/// <c>HardLink</c>, and the two symlink strategies are downgraded to it when the
/// process is not elevated, so in practice every workspace on Linux full-copied the
/// game — roughly 1.5 GB per profile — while appearing to work.
/// </para>
/// <para>
/// Registered by both the Linux and macOS hosts. It lives in the shared project rather
/// than being duplicated per host because <c>link(2)</c> is identical on both; see
/// <see cref="UnixNativeMethods"/> for why the interop stops there.
/// </para>
/// </summary>
/// <param name="baseService">The shared implementation everything else delegates to.</param>
/// <param name="casService">Content-addressable store, used to resolve hashes to paths.</param>
/// <param name="logger">Logger.</param>
public class UnixFileOperationsService(
    FileOperationsService baseService,
    ICasService casService,
    ILogger<UnixFileOperationsService> logger)
    : DelegatingFileOperationsService(baseService, casService, logger)
{
    /// <inheritdoc/>
    public override async Task<bool> CopyFromCasAsync(string hash, string destinationPath, ContentType? contentType = null, CancellationToken cancellationToken = default)
    {
        var casPath = await ResolveCasPathAsync(hash, contentType, cancellationToken).ConfigureAwait(false);
        if (casPath is null)
        {
            return false;
        }

        await BaseService.CopyFileAsync(casPath, destinationPath, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// No volume preflight. The Windows implementation checks <c>AreSameVolume</c> first,
    /// but that helper compares <c>Path.GetPathRoot</c>, which is <c>/</c> for every path
    /// on Unix and so always reports "same volume". Attempting the link and handling
    /// <c>EXDEV</c> is both correct and free of the race a preflight introduces.
    /// </remarks>
    public override async Task<bool> LinkFromCasAsync(
        string hash,
        string destinationPath,
        bool useHardLink = false,
        ContentType? contentType = null,
        CancellationToken cancellationToken = default)
    {
        var casPath = await ResolveCasPathAsync(hash, contentType, cancellationToken).ConfigureAwait(false);
        if (casPath is null)
        {
            return false;
        }

        if (!useHardLink)
        {
            try
            {
                await BaseService.CreateSymlinkAsync(destinationPath, casPath, allowFallback: false, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "Failed to create symlink from CAS hash {Hash} to {DestinationPath}", hash, destinationPath);
                return false;
            }
        }

        try
        {
            await CreateHardLinkAsync(destinationPath, casPath, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Failed to create hard link from CAS hash {Hash} to {DestinationPath}", hash, destinationPath);
            return false;
        }
    }

    /// <inheritdoc/>
    public override async Task CreateHardLinkAsync(
        string linkPath,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        var absoluteLinkPath = Path.GetFullPath(linkPath);
        var absoluteTargetPath = Path.GetFullPath(targetPath);

        FileOperationsService.EnsureDirectoryExists(absoluteLinkPath);
        FileOperationsService.DeleteFileIfExists(absoluteLinkPath);

        await Task.Run(
            () =>
            {
                if (UnixNativeMethods.Link(absoluteTargetPath, absoluteLinkPath) == 0)
                {
                    return;
                }

                // Interpret errno rather than preflighting. A preflight volume check is
                // racy, and would need a shared struct stat layout that Linux and macOS
                // do not agree on. Attempting the call is both simpler and accurate.
                var errno = Marshal.GetLastPInvokeError();

                throw errno switch
                {
                    UnixNativeMethods.EXDEV => new IOException(
                        $"Cannot hard link '{absoluteLinkPath}' to '{absoluteTargetPath}': they are on different "
                        + "filesystems. Move the content store onto the same volume as the workspace, or choose "
                        + "the FullCopy workspace strategy."),
                    UnixNativeMethods.EPERM => new IOException(
                        $"Cannot hard link '{absoluteLinkPath}': the filesystem does not support hard links."),
                    UnixNativeMethods.ENOENT => new FileNotFoundException(
                        $"Cannot hard link '{absoluteLinkPath}': the target '{absoluteTargetPath}' does not exist.",
                        absoluteTargetPath),
                    UnixNativeMethods.EACCES => new UnauthorizedAccessException(
                        $"Cannot hard link '{absoluteLinkPath}' to '{absoluteTargetPath}': permission denied."),
                    _ => new IOException(
                        $"Failed to hard link '{absoluteLinkPath}' to '{absoluteTargetPath}' (errno {errno})."),
                };
            },
            cancellationToken).ConfigureAwait(false);

        Logger.LogDebug("Created hard link from {Link} to {Target}", absoluteLinkPath, absoluteTargetPath);
    }

    private async Task<string?> ResolveCasPathAsync(string hash, ContentType? contentType, CancellationToken cancellationToken)
    {
        var pathResult = contentType.HasValue
            ? await CasService.GetContentPathAsync(hash, contentType.Value, cancellationToken).ConfigureAwait(false)
            : await CasService.GetContentPathAsync(hash, cancellationToken).ConfigureAwait(false);

        if (!pathResult.Success || pathResult.Data is null)
        {
            Logger.LogError("CAS content not found for hash {Hash}: {Error}", hash, pathResult.FirstError);
            return null;
        }

        return pathResult.Data;
    }
}
