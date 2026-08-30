using System;
using System.Collections.Generic;
using System.Linq;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;

namespace GenHub.Core.Extensions;

/// <summary>
/// Provides extension methods for <see cref="WorkspaceConfiguration"/>.
/// </summary>
public static class WorkspaceConfigurationExtensions
{
    /// <summary>
    /// Gets all unique files from all manifests, deduplicated by relative path.
    /// When multiple manifests contain the same file path, returns the file from the highest priority manifest.
    /// </summary>
    /// <param name="configuration">The workspace configuration to get files from.</param>
    /// <returns>An enumerable of unique manifest files.</returns>
    public static IEnumerable<ManifestFile> GetAllUniqueFiles(
        this WorkspaceConfiguration configuration)
    {
        if (configuration?.Manifests is null || configuration.Manifests.Count == 0)
        {
            return [];
        }

        return configuration.Manifests
            .SelectMany((m, index) => (m.Files ?? []).Select(f => new { File = f, Manifest = m, ManifestIndex = index }))
            .GroupBy(x => x.File.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => ContentTypePriority.GetPriority(x.Manifest.ContentType))
                          .ThenByDescending(x => x.ManifestIndex)
                          .First().File);
    }

    /// <summary>
    /// Gets all unique files intended for the workspace from all manifests, deduplicated by relative path.
    /// Only includes files where <see cref="ManifestFile.InstallTarget"/> is <see cref="ContentInstallTarget.Workspace"/>.
    /// Higher-priority manifests (such as mods and patches) take precedence over lower-priority manifests (such as base game installations).
    /// </summary>
    /// <param name="configuration">The workspace configuration to get files from.</param>
    /// <returns>An enumerable of unique workspace-specific manifest files.</returns>
    public static IEnumerable<ManifestFile> GetWorkspaceUniqueFiles(
        this WorkspaceConfiguration configuration)
    {
        if (configuration?.Manifests is null || configuration.Manifests.Count == 0)
        {
            return [];
        }

        return configuration.Manifests
            .SelectMany((m, index) => (m.Files ?? []).Select(f => new { File = f, Manifest = m, ManifestIndex = index }))
            .Where(x => x.File.InstallTarget == ContentInstallTarget.Workspace)
            .GroupBy(x => x.File.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => ContentTypePriority.GetPriority(x.Manifest.ContentType))
                          .ThenByDescending(x => x.ManifestIndex)
                          .First().File);
    }

    /// <summary>
    /// Gets all unique workspace files paired with their owning manifest, deduplicated by relative path
    /// and resolved by content type priority (higher-priority content types override lower-priority ones).
    /// </summary>
    /// <param name="configuration">The workspace configuration to get prioritized files from.</param>
    /// <returns>A read-only list of prioritized file and manifest pairs.</returns>
    public static IReadOnlyList<(ManifestFile File, ContentManifest Manifest)> GetPrioritizedWorkspaceFiles(
        this WorkspaceConfiguration configuration)
    {
        if (configuration?.Manifests is null || configuration.Manifests.Count == 0)
        {
            return [];
        }

        return configuration.Manifests
            .SelectMany((manifest, index) => (manifest.Files ?? Enumerable.Empty<ManifestFile>())
                .Where(f => f.InstallTarget == ContentInstallTarget.Workspace)
                .Select(file => new { File = file, Manifest = manifest, ManifestIndex = index }))
            .GroupBy(x => x.File.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(x => ContentTypePriority.GetPriority(x.Manifest.ContentType))
                .ThenByDescending(x => x.ManifestIndex)
                .First())
            .Select(x => (x.File, x.Manifest))
            .ToList();
    }
}