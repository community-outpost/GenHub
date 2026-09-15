using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Unified service for reconciling game profiles and manifest metadata.
/// Coordinates between profile metadata updates and content addressable storage tracking.
/// </summary>
public interface IContentReconciliationService : IDisposable
{
    /// <summary>
    /// Reconciles all profiles by removing references to a deleted manifest ID.
    /// Also handles CAS reference tracking cleanup.
    /// </summary>
    /// <param name="manifestId">The manifest ID to remove.</param>
    /// <param name="skipUntrack">If true, skips untracking CAS references for this manifest.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An OperationResult containing the reconciliation counts.</returns>
    Task<OperationResult<ReconciliationResult>> ReconcileManifestRemovalAsync(
        ManifestId manifestId,
        bool skipUntrack = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles the replacement of one manifest with another across all profiles.
    /// This is used when a manifest is updated and its ID changes (e.g. version bump).
    /// </summary>
    /// <param name="oldId">The old manifest ID to replace.</param>
    /// <param name="newManifest">The new manifest to use as replacement.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An OperationResult containing the reconciliation counts.</returns>
    Task<OperationResult<ReconciliationResult>> ReconcileManifestReplacementAsync(
        ManifestId oldId,
        ContentManifest newManifest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles bulk manifest replacements.
    /// </summary>
    /// <param name="replacements">Dictionary of old ID to new manifest.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An OperationResult containing the reconciliation counts.</returns>
    Task<OperationResult<ReconciliationResult>> ReconcileBulkManifestReplacementAsync(
        IReadOnlyDictionary<string, ContentManifest> replacements,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Orchestrates a local content update, including manifest pooling and profile reconciliation.
    /// </summary>
    /// <param name="oldId">The old manifest ID (if any).</param>
    /// <param name="newManifest">The new manifest representing the updated content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An OperationResult containing the update result.</returns>
    Task<OperationResult<ContentUpdateResult>> OrchestrateLocalUpdateAsync(
        string? oldId,
        ContentManifest newManifest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a safe bulk update of manifests across all profiles.
    /// Ensures CAS references are untracked and manifests are removed from the pool in the correct order.
    /// </summary>
    /// <param name="replacements">Dictionary of old manifest ID to new manifest ID.</param>
    /// <param name="removeOld">Whether to remove the old manifests after reconciliation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An OperationResult containing the reconciliation counts.</returns>
    Task<OperationResult<ReconciliationResult>> OrchestrateBulkUpdateAsync(
        IReadOnlyDictionary<string, string> replacements,
        bool removeOld = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a safe bulk removal of manifests across all profiles.
    /// Ensures CAS references are untracked and manifests are removed from the pool in the correct order.
    /// </summary>
    /// <param name="manifestIds">The manifest IDs to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An OperationResult containing the reconciliation counts.</returns>
    Task<OperationResult<ReconciliationResult>> OrchestrateBulkRemovalAsync(
        IEnumerable<ManifestId> manifestIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules garbage collection to run. Should be called AFTER all untrack operations are complete.
    /// </summary>
    /// <param name="force">Whether to force garbage collection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success.</returns>
    Task<OperationResult> ScheduleGarbageCollectionAsync(
        bool force = false,
        CancellationToken cancellationToken = default);
}
