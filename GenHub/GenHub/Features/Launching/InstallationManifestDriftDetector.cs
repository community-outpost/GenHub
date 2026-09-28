using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Manifest;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;

namespace GenHub.Features.Launching;

/// <summary>
/// Compares a stored game installation manifest against the installation folder on disk so
/// launches can pick up loose files players add, remove, or modify between detections
/// (for example mod BIG archives dropped into the game root). The comparison mirrors the
/// generation rules in <see cref="GameInstallationScanRules"/>: same skip list, same
/// extension filter, and added-file detection only for scan-based manifests since
/// authoritative catalog generation ignores loose extras. Comparison is by file presence
/// and size only: same-size in-place edits are intentionally not detected, since hashing
/// the whole installation on every launch would cost more than the check saves.
/// </summary>
internal static class InstallationManifestDriftDetector
{
    private static readonly EnumerationOptions ScanOptions = new()
    {
        IgnoreInaccessible = false,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <summary>
    /// Detects differences between a stored installation manifest and the installation folder.
    /// Fails open: unreadable folders or scan errors report no drift so launches proceed
    /// with stored manifests exactly as before.
    /// </summary>
    /// <param name="installationPath">The game installation directory to scan.</param>
    /// <param name="targetGame">The game the manifest describes.</param>
    /// <param name="manifestVersion">The manifest version string.</param>
    /// <param name="manifestFiles">The files recorded in the stored manifest.</param>
    /// <param name="ignoredPaths">Optional set of relative paths to ignore for added-file detection.</param>
    /// <param name="cancellationToken">A token to cancel the scan.</param>
    /// <returns>The detected drift, if any.</returns>
    internal static InstallationManifestDrift DetectDrift(
        string installationPath,
        GameType targetGame,
        string? manifestVersion,
        IReadOnlyList<ManifestFile> manifestFiles,
        IReadOnlySet<string>? ignoredPaths = null,
        CancellationToken cancellationToken = default)
    {
        var empty = new InstallationManifestDrift(false, [], [], []);
        if (string.IsNullOrWhiteSpace(installationPath) || !Directory.Exists(installationPath))
        {
            return empty;
        }

        var manifestMap = BuildManifestMap(manifestFiles);
        Dictionary<string, long> diskFiles;
        try
        {
            diskFiles = ScanDiskFiles(installationPath, manifestMap, targetGame, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return empty;
        }

        var removed = new List<string>();
        var changed = new List<string>();
        foreach (var (relativePath, size) in manifestMap)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!diskFiles.TryGetValue(relativePath, out var diskSize))
            {
                removed.Add(relativePath);
            }
            else if (size > 0 && diskSize != size)
            {
                changed.Add(relativePath);
            }
        }

        var added = CollectAddedFiles(diskFiles, manifestMap, targetGame, manifestVersion, ignoredPaths, cancellationToken);
        var hasDrift = added.Count > 0 || removed.Count > 0 || changed.Count > 0;
        return new InstallationManifestDrift(hasDrift, added, removed, changed);
    }

    private static List<string> CollectAddedFiles(
        Dictionary<string, long> diskFiles,
        Dictionary<string, long> manifestMap,
        GameType targetGame,
        string? manifestVersion,
        IReadOnlySet<string>? ignoredPaths,
        CancellationToken cancellationToken)
    {
        var added = new List<string>();
        if (GameInstallationScanRules.UsesAuthoritativeCatalog(targetGame, manifestVersion))
        {
            return added;
        }

        foreach (var relativePath in diskFiles.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ignoredPaths != null && ignoredPaths.Contains(relativePath))
            {
                continue;
            }

            if (!manifestMap.ContainsKey(relativePath))
            {
                added.Add(relativePath);
            }
        }

        return added;
    }

    private static Dictionary<string, long> ScanDiskFiles(
        string installationPath,
        Dictionary<string, long> manifestMap,
        GameType targetGame,
        CancellationToken cancellationToken)
    {
        var primaryExecutable = targetGame == GameType.Generals
            ? GameClientConstants.GeneralsExecutable
            : GameClientConstants.ZeroHourExecutable;

        var diskFiles = new Dictionary<string, long>(PathHelper.PathComparer);
        foreach (var file in Directory.EnumerateFiles(installationPath, "*", ScanOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = TryGetIncludedRelativePath(installationPath, file, manifestMap, primaryExecutable);
            if (relativePath == null)
            {
                continue;
            }

            var sourcePath = GameInstallationScanRules.ResolveSourcePathWithBackup(file);
            diskFiles.TryAdd(relativePath, new FileInfo(sourcePath).Length);
        }

        return diskFiles;
    }

    private static string? TryGetIncludedRelativePath(
        string installationPath,
        string file,
        Dictionary<string, long> manifestMap,
        string primaryExecutable)
    {
        string relativePath;
        try
        {
            relativePath = Path.GetRelativePath(installationPath, file).Replace('\\', '/');
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }

        if (GameInstallationScanRules.ShouldSkipFile(relativePath))
        {
            return null;
        }

        var isPrimary = relativePath.Equals(primaryExecutable, StringComparison.OrdinalIgnoreCase) ||
                        manifestMap.ContainsKey(relativePath) ||
                        GameClientConstants.ValidGameExecutableNames.Contains(Path.GetFileName(relativePath), StringComparer.OrdinalIgnoreCase);

        if (!isPrimary && !GameInstallationScanRules.FallbackFileExtensions.Contains(Path.GetExtension(file)))
        {
            return null;
        }

        return relativePath;
    }

    private static Dictionary<string, long> BuildManifestMap(IReadOnlyList<ManifestFile> manifestFiles)
    {
        var manifestMap = new Dictionary<string, long>(PathHelper.PathComparer);
        foreach (var file in manifestFiles)
        {
            if (string.IsNullOrWhiteSpace(file.RelativePath))
            {
                continue;
            }

            manifestMap.TryAdd(file.RelativePath.Replace('\\', '/'), file.Size);
        }

        return manifestMap;
    }
}
