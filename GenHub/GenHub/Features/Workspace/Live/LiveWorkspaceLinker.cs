using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Results;
using GenHub.Core.Utilities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace GenHub.Features.Workspace.Live;

/// <summary>
/// Creates and removes the symbolic links that mount live content into a workspace.
/// All operations fail loudly; there is no hardlink or copy fallback.
/// </summary>
public sealed class LiveWorkspaceLinker(
    ILogger<LiveWorkspaceLinker> logger,
    ILocalizationService? localizationService = null) : ILiveWorkspaceLinker
{
    /// <summary>
    /// Approximate on-disk cost of one symbolic link entry.
    /// </summary>
    public const long LinkOverheadBytes = 1024L;

    /// <summary>
    /// Ensures that a workspace entry is a symbolic link to the supplied source file.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <param name="relativePath">The workspace-relative path, using forward or back slashes.</param>
    /// <param name="sourcePath">The absolute source file path.</param>
    /// <returns>Success, or a failure describing why the link could not be created.</returns>
    public OperationResult EnsureLink(string workspaceRoot, string relativePath, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var targetPath = Path.Combine(workspaceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        try
        {
            var existing = new FileInfo(targetPath);
            if (existing.LinkTarget != null)
            {
                var resolved = existing.ResolveLinkTarget(returnFinalTarget: true);
                if (resolved != null && PathsEqual(resolved.FullName, sourcePath))
                {
                    return OperationResult.CreateSuccess();
                }

                File.Delete(targetPath);
            }
            else if (File.Exists(targetPath))
            {
                // The snapshot pass may have materialized the same path first. The live
                // plan only contains entries that won conflict resolution, so replacing
                // the entry is intended.
                File.SetAttributes(targetPath, FileAttributes.Normal);
                File.Delete(targetPath);
            }

            SymbolicLinkCreator.CreateFileLink(targetPath, sourcePath);
            return OperationResult.CreateSuccess();
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Symbolic link creation was denied for {TargetPath}", targetPath);
            return OperationResult.CreateFailure(
                GetLiveError(
                    "Workspace.Live.Error.SymlinkCreate",
                    "Cannot create a symbolic link at '{0}'. Enable Developer Mode or run GenHub as an administrator.",
                    targetPath));
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Symbolic link creation failed for {TargetPath}", targetPath);
            return OperationResult.CreateFailure(
                GetLiveError(
                    "Workspace.Live.Error.SymlinkCreateDetail",
                    "Cannot create a symbolic link at '{0}': {1}. Enable Developer Mode or run GenHub as an administrator.",
                    targetPath,
                    ex.Message));
        }
    }

    /// <summary>
    /// Ensures that a workspace entry is a private copy of the source file.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <param name="relativePath">The workspace-relative path.</param>
    /// <param name="sourcePath">The absolute source file path.</param>
    /// <returns>Success, or a failure describing why the copy could not be refreshed.</returns>
    public OperationResult EnsureCopy(string workspaceRoot, string relativePath, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var targetPath = Path.Combine(workspaceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        string? tempPath = null;
        try
        {
            var existing = new FileInfo(targetPath);
            if (existing.LinkTarget != null)
            {
                File.Delete(targetPath);
            }
            else if (File.Exists(targetPath))
            {
                // A newer workspace copy means the game wrote to it; keep that write.
                // A newer source file means the developer edited it; refresh the copy.
                var sourceTime = File.GetLastWriteTimeUtc(sourcePath);
                var targetTime = File.GetLastWriteTimeUtc(targetPath);
                if (sourceTime <= targetTime)
                {
                    return OperationResult.CreateSuccess();
                }

                File.SetAttributes(targetPath, FileAttributes.Normal);
            }

            tempPath = targetPath + ".gh-live-" + Guid.NewGuid().ToString("N") + ".tmp";
            File.Copy(sourcePath, tempPath, overwrite: true);
            File.SetLastWriteTimeUtc(tempPath, DateTime.UtcNow);
            File.Move(tempPath, targetPath, overwrite: true);
            return OperationResult.CreateSuccess();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "[Live] Failed to copy {SourcePath} to {TargetPath}", sourcePath, targetPath);
            return OperationResult.CreateFailure(
                GetLiveError(
                    "Workspace.Live.Error.Copy",
                    "Cannot copy '{0}' to '{1}': {2}",
                    sourcePath,
                    targetPath,
                    ex.Message));
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Best-effort cleanup of a uniquely named staging file.
                }
            }
        }
    }

    /// <summary>
    /// Deletes a workspace link when it points into one of the supplied live roots.
    /// </summary>
    /// <param name="linkPath">The workspace entry to inspect.</param>
    /// <param name="liveRoots">The live roots a link may belong to.</param>
    /// <returns><c>true</c> when the entry was a live link and was deleted.</returns>
    public bool TryDeleteLiveLink(string linkPath, IReadOnlyList<string> liveRoots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkPath);
        ArgumentNullException.ThrowIfNull(liveRoots);

        try
        {
            var info = new FileInfo(linkPath);
            if (info.LinkTarget == null)
            {
                return false;
            }

            var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
            if (resolved != null && !liveRoots.Any(root => IsUnder(resolved.FullName, root)))
            {
                return false;
            }

            File.Delete(linkPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to delete live workspace link {LinkPath}", linkPath);
            return false;
        }
    }

    /// <summary>
    /// Determines whether an existing workspace entry is a link to the supplied source.
    /// </summary>
    /// <param name="path">The workspace entry.</param>
    /// <param name="sourcePath">The expected source file.</param>
    /// <returns><c>true</c> when the entry is a link to the source.</returns>
    public bool IsLinkTo(string path, string sourcePath)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.LinkTarget == null)
            {
                return false;
            }

            var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
            return resolved != null && PathsEqual(resolved.FullName, sourcePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool PathsEqual(string first, string second) =>
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string root)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private string GetLiveError(string key, string fallback, params object?[] arguments)
    {
        if (localizationService == null)
        {
            return string.Format(CultureInfo.InvariantCulture, fallback, arguments);
        }

        var message = localizationService.GetString(key, arguments);
        return string.IsNullOrWhiteSpace(message) || string.Equals(message, key, StringComparison.Ordinal)
            ? string.Format(CultureInfo.InvariantCulture, fallback, arguments)
            : message;
    }
}
