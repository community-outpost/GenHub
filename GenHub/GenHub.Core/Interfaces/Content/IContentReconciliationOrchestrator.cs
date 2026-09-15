using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Single entry point for all content reconciliation operations.
/// Enforces correct operation ordering: Acquire -> Track -> Update Profiles -> Untrack -> Remove -> GC.
/// </summary>
public interface IContentReconciliationOrchestrator
{
    /// <summary>
    /// Executes a complete content replacement workflow.
    /// Guarantees: Update Profiles -> Untrack Old -> Remove Old -> GC.
    /// </summary>
    /// <param name="request">The replacement request containing old/new manifest mappings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing details of the operation.</returns>
    Task<OperationResult<ContentReplacementResult>> ExecuteContentReplacementAsync(
        ContentReplacementRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a complete content removal workflow.
    /// Guarantees: Update Profiles -> Untrack -> Remove -> GC.
    /// </summary>
    /// <param name="manifestIds">The manifest IDs to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing details of the operation.</returns>
    Task<OperationResult<ContentRemovalResult>> ExecuteContentRemovalAsync(
        IEnumerable<string> manifestIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a local content update workflow.
    /// Guarantees: Track New -> Update Profiles -> Untrack Old -> Remove Old -> GC.
    /// </summary>
    /// <param name="oldManifestId">The existing manifest ID.</param>
    /// <param name="newManifest">The new manifest (may have same or different ID).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success.</returns>
    Task<OperationResult<ContentUpdateResult>> ExecuteContentUpdateAsync(
        string oldManifestId,
        ContentManifest newManifest,
        CancellationToken cancellationToken = default);
}
