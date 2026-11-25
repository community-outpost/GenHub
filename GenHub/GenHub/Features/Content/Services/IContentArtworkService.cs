using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Persists remote content artwork (icons, covers) to local per-manifest storage so the
/// offline library renders without a connection, and purges it when content is deleted.
/// </summary>
public interface IContentArtworkService
{
    /// <summary>
    /// Returns the local file path for a manifest's artwork slot when persisted.
    /// </summary>
    /// <param name="manifestId">The manifest ID.</param>
    /// <param name="kind">The artwork slot.</param>
    /// <returns>The local path, or null when nothing is stored.</returns>
    string? GetLocalArtworkPath(string manifestId, ContentArtworkKind kind);

    /// <summary>
    /// Downloads a manifest's remote artwork into local storage unless already present.
    /// Best-effort: failures are reported, never thrown except on cancellation.
    /// </summary>
    /// <param name="manifest">The manifest whose artwork should be persisted.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Whether all artwork was persisted or already present.</returns>
    Task<OperationResult<bool>> PrefetchArtworkAsync(ContentManifest manifest, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a manifest's local artwork directory when its content is removed.
    /// </summary>
    /// <param name="manifestId">The manifest ID.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Whether the artwork directory is gone.</returns>
    Task<OperationResult<bool>> PurgeArtworkAsync(string manifestId, CancellationToken cancellationToken = default);
}
