using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Service for reconciling profiles when GeneralsOnline updates are detected.
/// When an update is found, this service updates all profiles using GeneralsOnline,
/// removes old manifests and CAS content, and prepares profiles for the new version.
/// </summary>
public interface IGeneralsOnlineProfileReconciler
{
    /// <summary>
    /// Checks for GeneralsOnline updates and reconciles all affected profiles if an update is found.
    /// This method should be called before launching a GeneralsOnline profile.
    /// </summary>
    /// <param name="triggeringProfileId">The ID of the profile that triggered the check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// - Success with Data=true: Update was found and applied successfully.
    /// - Success with Data=false: No update was needed.
    /// - Failure: Update check or reconciliation failed.
    /// </returns>
    Task<OperationResult<bool>> CheckAndReconcileIfNeededAsync(
        string triggeringProfileId,
        CancellationToken cancellationToken = default);
}
