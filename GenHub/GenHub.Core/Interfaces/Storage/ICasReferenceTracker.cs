using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Storage;

/// <summary>
/// Tracks references to CAS objects for garbage collection purposes.
/// </summary>
public interface ICasReferenceTracker
{
    /// <summary>
    /// Tracks references from a game manifest.
    /// </summary>
    /// <param name="manifestId">The manifest ID.</param>
    /// <param name="manifest">The game manifest.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task<OperationResult> TrackManifestReferencesAsync(string manifestId, ContentManifest manifest, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracks references from a workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace ID.</param>
    /// <param name="referencedHashes">The set of CAS hashes referenced by the workspace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task<OperationResult> TrackWorkspaceReferencesAsync(string workspaceId, IEnumerable<string> referencedHashes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes tracking for a manifest.
    /// </summary>
    /// <param name="manifestId">The manifest ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task<OperationResult> UntrackManifestAsync(string manifestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes tracking for a workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task<OperationResult> UntrackWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all CAS hashes that are currently referenced.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Set of all referenced hashes.</returns>
    Task<HashSet<string>> GetAllReferencedHashesAsync(CancellationToken cancellationToken = default);
}
