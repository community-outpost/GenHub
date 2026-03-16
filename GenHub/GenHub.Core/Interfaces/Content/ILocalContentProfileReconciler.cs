using System.Threading.Tasks;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Service for reconciling game profiles when local content is modified (e.g. renamed).
/// Ensures that profiles referencing the old content ID are updated to reference the new ID.
/// </summary>
public interface ILocalContentProfileReconciler
{
    /// <summary>
    /// Reconciles all profiles by updating references from an old manifest ID to a new one.
    /// </summary>
    /// <param name="oldManifestId">The old manifest ID (before rename/update).</param>
    /// <param name="newManifestId">The new manifest ID (after rename/update).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing the number of updated profiles.</returns>
    Task<OperationResult<int>> ReconcileProfilesAsync(
        string oldManifestId,
        string newManifestId,
        CancellationToken cancellationToken = default);
}
