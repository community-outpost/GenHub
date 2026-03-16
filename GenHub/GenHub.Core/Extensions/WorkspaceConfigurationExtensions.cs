using System.Linq;
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
    /// When multiple manifests contain the same file path, returns the first occurrence.
    /// </summary>
    /// <param name="configuration">The workspace configuration to get files from.</param>
    /// <returns>An enumerable of unique manifest files.</returns>
    public static IEnumerable<ManifestFile> GetAllUniqueFiles(
        this WorkspaceConfiguration configuration)
    {
        return configuration.Manifests
            .SelectMany(m => (m.Files ?? []).Select(f => new { File = f, Manifest = m }))
            .GroupBy(x => x.File.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => ContentTypePriority.GetPriority(x.Manifest.ContentType))
                          .First().File);
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
        return configuration.Manifests
            .SelectMany(m => (m.Files ?? []).Select(f => new { File = f, Manifest = m }))
            .Where(x => x.File.InstallTarget == GenHub.Core.Models.Enums.ContentInstallTarget.Workspace)
            .GroupBy(x => x.File.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => ContentTypePriority.GetPriority(x.Manifest.ContentType))
                          .First().File);
    }
}