using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using System.Linq;

namespace GenHub.Core.Extensions;

/// <summary>
/// Provides extension methods for <see cref="WorkspaceConfiguration"/>.
/// </summary>
public static class WorkspaceConfigurationExtensions
{
    /// <summary>
    /// Gets all unique files from all manifests, deduplicated by relative path.
    /// When multiple manifests contain the same file path, the higher content-type priority wins,
    /// and on equal priority the later manifest in load order wins.
    /// Each manifest contributes the files it resolves to on this host.
    /// </summary>
    /// <param name="configuration">The workspace configuration to get files from.</param>
    /// <returns>An enumerable of unique manifest files.</returns>
    public static IEnumerable<ManifestFile> GetAllUniqueFiles(
        this WorkspaceConfiguration configuration)
    {
        return SelectCollisionWinners(configuration, workspaceOnly: false).Select(entry => entry.File);
    }

    /// <summary>
    /// Gets all unique files intended for the workspace from all manifests, deduplicated by relative path.
    /// Only includes files where <see cref="ManifestFile.InstallTarget"/> is <see cref="GenHub.Core.Models.Enums.ContentInstallTarget.Workspace"/>.
    /// </summary>
    /// <param name="configuration">The workspace configuration to get files from.</param>
    /// <returns>An enumerable of unique workspace-specific manifest files.</returns>
    public static IEnumerable<ManifestFile> GetWorkspaceUniqueFiles(
        this WorkspaceConfiguration configuration)
    {
        return configuration.GetWorkspaceUniqueFileEntries().Select(entry => entry.File);
    }

    /// <summary>
    /// Gets all unique files intended for the workspace, each paired with the manifest it came from.
    /// Deduplication and ordering match <see cref="GetWorkspaceUniqueFiles"/>: the higher content-type
    /// priority wins a shared path, and on equal priority the later manifest in load order wins.
    /// Every workspace strategy and the reconciler use this rule, so the strategy a profile uses
    /// never changes which manifest's file is materialized.
    /// </summary>
    /// <param name="configuration">The workspace configuration to get files from.</param>
    /// <returns>The unique workspace-specific files and their owning manifests.</returns>
    public static IReadOnlyList<(ManifestFile File, ContentManifest Manifest)> GetWorkspaceUniqueFileEntries(
        this WorkspaceConfiguration configuration)
    {
        return SelectCollisionWinners(configuration, workspaceOnly: true);
    }

    private static List<(ManifestFile File, ContentManifest Manifest)> SelectCollisionWinners(
        WorkspaceConfiguration configuration,
        bool workspaceOnly)
    {
        return configuration.Manifests
            .SelectMany((manifest, index) => ManifestVariantResolver.ResolveFiles(manifest)
                .Where(file => !workspaceOnly || file.InstallTarget == GenHub.Core.Models.Enums.ContentInstallTarget.Workspace)
                .Select(file => (File: file, Manifest: manifest, ManifestIndex: index)))
            .GroupBy(entry => entry.File.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(entry => ContentTypePriority.GetPriority(entry.Manifest.ContentType))
                .ThenByDescending(entry => entry.ManifestIndex)
                .First())
            .Select(entry => (entry.File, entry.Manifest))
            .ToList();
    }
}
