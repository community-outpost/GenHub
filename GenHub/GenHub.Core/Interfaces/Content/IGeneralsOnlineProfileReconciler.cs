using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Service for reconciling profiles when GeneralsOnline updates are detected.
/// When an update is found, this service updates all profiles using GeneralsOnline,
/// removes old manifests and CAS content, and prepares profiles for the new version.
/// </summary>
public interface IGeneralsOnlineProfileReconciler
{
    /// <summary>
    /// Checks for GeneralsOnline updates and reconciles profiles if an update is available.
    /// Prompts user for strategy (replace vs new profile) if not configured.
    /// </summary>
    /// <param name="triggeringProfileId">The profile that triggered the launch (if any).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating whether reconciliation was performed, including the target profile ID to launch.</returns>
    Task<OperationResult<PublisherReconciliationResult>> CheckAndReconcileIfNeededAsync(
        string triggeringProfileId,
        CancellationToken cancellationToken = default);
}
