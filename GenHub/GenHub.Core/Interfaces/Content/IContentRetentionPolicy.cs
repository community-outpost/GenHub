using GenHub.Core.Models.Manifest;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Defines policy for retaining manifests during updates and cleanup,
/// protecting pinned versions (such as those needed by replays) and recent versions from automatic deletion.
/// </summary>
public interface IContentRetentionPolicy
{
    /// <summary>
    /// Filters candidate manifests proposed for deletion, returning only those safe to remove.
    /// Retained manifests (e.g., pinned or within the recent-version retention window) are excluded.
    /// </summary>
    /// <param name="candidates">Manifests proposed for deletion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Manifests that should actually be deleted.</returns>
    Task<IReadOnlyList<ContentManifest>> FilterDeletableManifestsAsync(
        IEnumerable<ContentManifest> candidates,
        CancellationToken cancellationToken = default);
}
