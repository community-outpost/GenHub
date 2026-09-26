using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Workspace.Strategies;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Workspace;

/// <summary>
/// Analyzes workspace state and determines delta operations for intelligent reconciliation.
/// </summary>
public class WorkspaceReconciler(ILogger<WorkspaceReconciler> logger, IFileOperationsService fileOperations)
{
    private static readonly long SmallFileThreshold = 5 * 1024 * 1024; // 5MB

    /// <summary>
    /// Analyzes workspace and determines what operations are needed to reconcile it with manifests.
    /// </summary>
    /// <param name="workspaceInfo">Existing workspace information (null if new workspace).</param>
    /// <param name="configuration">Target workspace configuration with manifests.</param>
    /// <param name="forceFullVerification">If true, forces full verification of all files including hashes.</param>
    /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
    /// <returns>List of delta operations needed to reconcile the workspace.</returns>
    public async Task<List<WorkspaceDelta>> AnalyzeWorkspaceDeltaAsync(
        WorkspaceInfo? workspaceInfo,
        WorkspaceConfiguration configuration,
        bool forceFullVerification = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deltas = new List<WorkspaceDelta>();
        var workspacePath = !string.IsNullOrEmpty(workspaceInfo?.WorkspacePath)
            ? workspaceInfo.WorkspacePath
            : Path.Combine(configuration.WorkspaceRootPath ?? string.Empty, configuration.Id);

        // Build a dictionary tracking ALL occurrences of each file for conflict resolution
        // Key: relative file path, Value: list of (file, contentType, manifestId) tuples
        var fileOccurrences = new Dictionary<string, List<(ManifestFile File, ContentType ContentType, string ManifestId)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var manifest in configuration.Manifests)
        {
            foreach (var file in (manifest.Files ?? Enumerable.Empty<ManifestFile>()).Where(f => f.InstallTarget == ContentInstallTarget.Workspace))
            {
                var relativePath = file.RelativePath.Replace('/', Path.DirectorySeparatorChar);

                if (!fileOccurrences.TryGetValue(relativePath, out var list))
                {
                    list = [];
                    fileOccurrences[relativePath] = list;
                }

                list.Add((file, manifest.ContentType, manifest.Id.ToString()));
            }
        }

        // Resolve conflicts using priority system and build final expectedFiles dictionary
        var expectedFiles = new Dictionary<string, ManifestFile>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<string>();

        foreach (var (relativePath, occurrences) in fileOccurrences)
        {
            if (occurrences.Count == 1)
            {
                // No conflict - single source
                expectedFiles[relativePath] = occurrences[0].File;
            }
            else
            {
                // Conflict - multiple sources for same file, resolve by priority
                var sorted = occurrences
                    .OrderByDescending(o => ContentTypePriority.GetPriority(o.ContentType))
                    .ToList();

                var winner = sorted[0];
                var losers = sorted.Skip(1).ToList();

                expectedFiles[relativePath] = winner.File;

                var loserInfo = string.Join(", ", losers.Select(l => $"{l.ContentType}({l.ManifestId})"));

                logger.LogWarning(
                    "File conflict for '{RelativePath}': using {WinnerType}({WinnerId}, priority {WinnerPriority}) over {Losers}",
                    relativePath,
                    winner.ContentType,
                    winner.ManifestId,
                    ContentTypePriority.GetPriority(winner.ContentType),
                    loserInfo);

                conflicts.Add(relativePath);
            }
        }

        // If workspace doesn't exist, all expected files need to be added
        if (workspaceInfo == null || !Directory.Exists(workspacePath))
        {
            logger.LogInformation("New workspace detected, {FileCount} files will be added after conflict resolution", expectedFiles.Count);
            foreach (var (relativePath, file) in expectedFiles)
            {
                deltas.Add(new WorkspaceDelta
                {
                    Operation = WorkspaceDeltaOperation.Add,
                    File = file,
                    WorkspacePath = Path.Combine(workspacePath, relativePath),
                    Reason = "New workspace",
                });
            }

            return deltas;
        }

        // Build a set of all files that currently exist in workspace
        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(workspacePath))
        {
            foreach (var filePath in Directory.GetFiles(workspacePath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(workspacePath, filePath);
                existingFiles.Add(relativePath);
            }
        }

        // Determine operations for expected files
        foreach (var (relativePath, manifestFile) in expectedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(workspacePath, relativePath);

            if (!existingFiles.Contains(relativePath))
            {
                if (IsOptionalOrSkippedFile(relativePath))
                {
                    // Optional media skipped or removed by launcher settings is not a missing file defect
                    deltas.Add(new WorkspaceDelta
                    {
                        Operation = WorkspaceDeltaOperation.Skip,
                        File = manifestFile,
                        WorkspacePath = fullPath,
                        Reason = WorkspaceConstants.OptionalFileSkippedReason,
                    });
                    continue;
                }

                // File missing from workspace - needs to be added
                deltas.Add(new WorkspaceDelta
                {
                    Operation = WorkspaceDeltaOperation.Add,
                    File = manifestFile,
                    WorkspacePath = fullPath,
                    Reason = "File missing from workspace",
                });
            }
            else
            {
                // File exists - check if it needs updating
                var needsUpdate = await FileNeedsUpdateAsync(fullPath, manifestFile, forceFullVerification, cancellationToken);
                if (needsUpdate)
                {
                    deltas.Add(new WorkspaceDelta
                    {
                        Operation = WorkspaceDeltaOperation.Update,
                        File = manifestFile,
                        WorkspacePath = fullPath,
                        Reason = "File needs update (hash mismatch or broken symlink)",
                    });
                }
                else
                {
                    // File is up to date - skip it
                    deltas.Add(new WorkspaceDelta
                    {
                        Operation = WorkspaceDeltaOperation.Skip,
                        File = manifestFile,
                        WorkspacePath = fullPath,
                        Reason = "File is current",
                    });
                }
            }
        }

        // Supplemental archives are workspace content that no manifest names: without this exclusion
        // every launch would report them as orphans and force a full workspace recreation.
        if (!WorkspaceCompatibilityHelper.TryGetSupplementalArchives(
            configuration.SupplementalArchiveRoot,
            out var supplementalArchives,
            logger))
        {
            logger.LogWarning(
                "Supplemental archive root could not be read: {Root}. Already-linked supplemental archives will be treated as orphans for this run, forcing one workspace recreation.",
                configuration.SupplementalArchiveRoot);
        }

        // Determine files to remove (exist in workspace but not in manifests)
        foreach (var relativePath in existingFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!expectedFiles.ContainsKey(relativePath))
            {
                if (IsRuntimeOrIgnoredWorkspaceFile(relativePath))
                {
                    continue;
                }

                // If this is generals.exe and generals.exe is not explicitly in manifests, check if it was created
                // as a compatibility alias for another custom executable/entrypoint, so it is not treated as an orphan.
                if (string.Equals(relativePath, GameClientConstants.GeneralsExecutable, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (await IsSupplementalArchiveFileAsync(workspacePath, relativePath, configuration.SupplementalArchiveRoot, supplementalArchives, forceFullVerification, cancellationToken))
                {
                    continue;
                }

                var fullPath = Path.Combine(workspacePath, relativePath);
                deltas.Add(new WorkspaceDelta
                {
                    Operation = WorkspaceDeltaOperation.Remove,
                    File = new ManifestFile { RelativePath = relativePath },
                    WorkspacePath = fullPath,
                    Reason = "File no longer in manifests",
                });
            }
        }

        var stats = deltas.GroupBy(d => d.Operation)
            .ToDictionary(g => g.Key, g => g.Count());

        logger.LogInformation(
            "Workspace delta analysis: Add={Add}, Update={Update}, Remove={Remove}, Skip={Skip}",
            stats.GetValueOrDefault(WorkspaceDeltaOperation.Add, 0),
            stats.GetValueOrDefault(WorkspaceDeltaOperation.Update, 0),
            stats.GetValueOrDefault(WorkspaceDeltaOperation.Remove, 0),
            stats.GetValueOrDefault(WorkspaceDeltaOperation.Skip, 0));

        return deltas;
    }

    /// <summary>
    /// Compares two streams chunk by chunk. Each chunk is filled fully before comparing:
    /// stream reads may legally short-read, and comparing two independently short-read
    /// buffers would both misalign every later chunk and report identical files as different.
    /// </summary>
    /// <param name="first">The first stream to compare.</param>
    /// <param name="second">The second stream to compare.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><c>true</c> when both streams have identical contents; otherwise, <c>false</c>.</returns>
    internal static async Task<bool> StreamsHaveIdenticalContentAsync(
        Stream first,
        Stream second,
        CancellationToken cancellationToken)
    {
        var firstBuffer = new byte[IoConstants.FileHashBufferSize];
        var secondBuffer = new byte[IoConstants.FileHashBufferSize];

        while (true)
        {
            var firstRead = await first.ReadAtLeastAsync(firstBuffer, firstBuffer.Length, throwOnEndOfStream: false, cancellationToken);
            var secondRead = await second.ReadAtLeastAsync(secondBuffer, secondBuffer.Length, throwOnEndOfStream: false, cancellationToken);
            if (firstRead != secondRead)
            {
                return false;
            }

            if (firstRead == 0)
            {
                return true;
            }

            if (!firstBuffer.AsSpan(0, firstRead).SequenceEqual(secondBuffer.AsSpan(0, secondRead)))
            {
                return false;
            }
        }
    }

    private static bool IsOptionalOrSkippedFile(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath.Replace('\\', '/'));
        return string.Equals(fileName, WorkspaceConstants.EaLogoBik, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, WorkspaceConstants.EaLogo640Bik, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRuntimeOrIgnoredWorkspaceFile(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath.Replace('\\', '/'));
        return fileName.StartsWith(WorkspaceConstants.RuntimeArtifactPrefix, StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(WorkspaceConstants.LogFileExtension, StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(WorkspaceConstants.TmpFileExtension, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, WorkspaceConstants.LaunchReceiptFile, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, WorkspaceConstants.ReleaseCrashInfoFile, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compares two files chunk by chunk so arbitrarily large archives are never loaded
    /// fully into memory, mirroring the manifest path's streaming hash verification.
    /// </summary>
    /// <param name="firstPath">The first file to compare.</param>
    /// <param name="secondPath">The second file to compare.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><c>true</c> when both files have identical contents; otherwise, <c>false</c>.</returns>
    private static async Task<bool> FilesHaveIdenticalContentAsync(
        string firstPath,
        string secondPath,
        CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = IoConstants.FileHashBufferSize,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        };

        await using var first = new FileStream(firstPath, options);
        await using var second = new FileStream(secondPath, options);
        return await StreamsHaveIdenticalContentAsync(first, second, cancellationToken);
    }

    private async Task<bool> IsSupplementalArchiveFileAsync(
        string workspacePath,
        string relativePath,
        string? supplementalRoot,
        IReadOnlyDictionary<string, string> supplementalArchives,
        bool forceFullVerification,
        CancellationToken cancellationToken)
    {
        if (supplementalArchives.Count == 0 || string.IsNullOrWhiteSpace(supplementalRoot))
        {
            return false;
        }

        if (relativePath.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            return false;
        }

        if (!supplementalArchives.TryGetValue(relativePath, out var sourcePath))
        {
            return false;
        }

        var workspaceFile = Path.Combine(workspacePath, relativePath);
        var linkTarget = new FileInfo(workspaceFile).LinkTarget;
        if (linkTarget is not null)
        {
            // A link pointing outside the current root is either foreign content or a leftover
            // from a previous root: it must not be mistaken for this root's archive, so it stays
            // a removal candidate and a recreation cleans it up.
            return WorkspaceCompatibilityHelper.IsLinkTargetUnderRoot(linkTarget, supplementalRoot);
        }

        // A regular file or hardlink carrying a supplemental name is only expected when it still
        // matches its source: anything else is a stale orphan (e.g. a disabled mod's override)
        // that must be removed rather than shadow the base archive indefinitely.
        return await SupplementalCopyMatchesAsync(workspaceFile, sourcePath, forceFullVerification, cancellationToken);
    }

    private async Task<bool> SupplementalCopyMatchesAsync(
        string workspaceFile,
        string sourcePath,
        bool forceFullVerification,
        CancellationToken cancellationToken)
    {
        try
        {
            var workspaceInfo = new FileInfo(workspaceFile);
            var sourceInfo = new FileInfo(sourcePath);
            if (!sourceInfo.Exists || workspaceInfo.Length != sourceInfo.Length)
            {
                return false;
            }

            // Mirror FileNeedsUpdateAsync: hashing every reconciliation is too expensive for
            // multi-hundred-megabyte archives, so size-matching large files are trusted while
            // small ones are compared byte for byte. A forced full verification compares
            // everything instead, since there is no manifest hash to check against.
            if (workspaceInfo.Length >= SmallFileThreshold && !forceFullVerification)
            {
                return true;
            }

            return await FilesHaveIdenticalContentAsync(workspaceFile, sourcePath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to compare supplemental file {WorkspaceFile} with {Source}; it will be treated as stale for this run", workspaceFile, sourcePath);
            return false;
        }
    }

    /// <summary>
    /// Determines if a file needs to be updated based on hash or symlink validity.
    /// </summary>
    private async Task<bool> FileNeedsUpdateAsync(
        string filePath,
        ManifestFile manifestFile,
        bool forceFullVerification = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(filePath))
                return true;

            var fileInfo = new FileInfo(filePath);

            // Check if it's a symlink
            if (fileInfo.LinkTarget != null)
            {
                // Symlink - verify target exists and is valid
                var targetPath = fileInfo.LinkTarget;
                if (!Path.IsPathRooted(targetPath))
                {
                    targetPath = Path.Combine(Path.GetDirectoryName(filePath) ?? Path.GetPathRoot(filePath) ?? string.Empty, targetPath);
                }

                // Broken symlink needs update
                if (!File.Exists(targetPath))
                {
                    logger.LogDebug("Broken symlink detected: {FilePath} -> {Target}", filePath, targetPath);
                    return true;
                }

                // For symlinks, trust that the target is correct if it exists and size matches
                // Computing hashes on every reconciliation is too expensive for 600+ files
                var targetFileInfo = new FileInfo(targetPath);
                if (manifestFile.Size > 0 && targetFileInfo.Length != manifestFile.Size)
                {
                    logger.LogDebug(
                        "Symlink target size mismatch for {FilePath}: expected {Expected}, got {Actual}",
                        filePath,
                        manifestFile.Size,
                        targetFileInfo.Length);
                    return true;
                }

                if (forceFullVerification && !string.IsNullOrEmpty(manifestFile.Hash))
                {
                    var hashMatches = await fileOperations.VerifyFileHashAsync(targetPath, manifestFile.Hash, cancellationToken);
                    if (!hashMatches)
                    {
                        logger.LogDebug("Symlink target hash mismatch for {FilePath}: expected {Expected}", filePath, manifestFile.Hash);
                        return true;
                    }
                }

                return false; // Valid symlink with size-matching target (and passing hash if forceFullVerification)
            }

            // Regular file - use size-based comparison for performance
            // File size mismatch check (fast and reliable for detecting changes)
            if (manifestFile.Size > 0 && fileInfo.Length != manifestFile.Size)
            {
                logger.LogDebug(
                    "Size mismatch for {FilePath}: expected {Expected}, got {Actual}",
                    filePath,
                    manifestFile.Size,
                    fileInfo.Length);
                return true;
            }

            if (!string.IsNullOrEmpty(manifestFile.Hash) && (forceFullVerification || fileInfo.Length < SmallFileThreshold))
            {
                var hashMatches = await fileOperations.VerifyFileHashAsync(filePath, manifestFile.Hash, cancellationToken);

                if (!hashMatches)
                {
                    logger.LogDebug(
                        "Hash mismatch for {FilePath}: expected {Expected}",
                        filePath,
                        manifestFile.Hash);
                    return true;
                }
            }

            return false; // File appears to be current (size matches and hash check passed/skipped)
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error checking if file needs update: {FilePath}", filePath);
            return true; // Assume needs update if we can't verify
        }
    }
}
