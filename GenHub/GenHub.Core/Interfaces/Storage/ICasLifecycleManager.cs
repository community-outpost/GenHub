using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;

namespace GenHub.Core.Interfaces.Storage;

/// <summary>
/// Manages the lifecycle of CAS references with proper ordering guarantees.
/// Ensures garbage collection only runs after references are properly untracked.
/// </summary>
public interface ICasLifecycleManager
{
    /// <summary>
    /// Atomically replaces manifest references (tracks new, then untracks old).
    /// </summary>
    /// <param name="oldManifestId">The old manifest ID to untrack.</param>
    /// <param name="newManifest">The new manifest to track.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success or failure.</returns>
    Task<OperationResult> ReplaceManifestReferencesAsync(
        string oldManifestId,
        ContentManifest newManifest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Untracks references for the specified manifest IDs.
    /// </summary>
    /// <param name="manifestIds">The manifest IDs to untrack.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result with bulk untrack stats.</returns>
    Task<OperationResult<BulkUntrackResult>> UntrackManifestsAsync(
        IEnumerable<string> manifestIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs garbage collection immediately.
    /// Should only be called AFTER all untrack operations are complete.
    /// </summary>
    /// <param name="force">Whether to force collection regardless of grace period.</param>
    /// <param name="lockTimeout">Optional timeout to wait for the GC lock. Defaults to 5 seconds if not specified.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with GC statistics.</returns>
    Task<OperationResult<GarbageCollectionStats>> RunGarbageCollectionAsync(
        bool force = false,
        TimeSpan? lockTimeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an audit of current CAS references for diagnostics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Audit result with reference statistics.</returns>
    Task<OperationResult<CasReferenceAudit>> GetReferenceAuditAsync(
        CancellationToken cancellationToken = default);
}
