using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
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
    /// <returns>List of delta operations needed to reconcile the workspace.</returns>
    public async Task<List<WorkspaceDelta>> AnalyzeWorkspaceDeltaAsync(
        WorkspaceInfo? workspaceInfo,
        WorkspaceConfiguration configuration,
        bool forceFullVerification = false)
    {
        var deltas = new List<WorkspaceDelta>();
        var workspacePath = Path.Combine(configuration.WorkspaceRootPath, configuration.Id);

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
            var fullPath = Path.Combine(workspacePath, relativePath);

            if (!existingFiles.Contains(relativePath))
            {
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
                var needsUpdate = await FileNeedsUpdateAsync(fullPath, manifestFile, forceFullVerification);
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

        // Determine files to remove (exist in workspace but not in manifests)
        foreach (var relativePath in existingFiles)
        {
            if (!expectedFiles.ContainsKey(relativePath))
            {
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
    /// Determines if a file needs to be updated based on hash or symlink validity.
    /// </summary>
    private async Task<bool> FileNeedsUpdateAsync(
        string filePath,
        ManifestFile manifestFile,
        bool forceFullVerification = false)
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
                    var hashMatches = await fileOperations.VerifyFileHashAsync(targetPath, manifestFile.Hash, CancellationToken.None);
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
                var hashMatches = await fileOperations.VerifyFileHashAsync(filePath, manifestFile.Hash, CancellationToken.None);

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
