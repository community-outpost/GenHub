using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Service for reconciling profiles when Community Outpost updates are detected.
/// </summary>
public interface ICommunityOutpostProfileReconciler
{
    /// <summary>
    /// Checks for updates and reconciles the profile if an update is found.
    /// </summary>
    /// <param name="triggeringProfileId">The ID of the profile triggering the check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success with reconciliation result if profile was updated/reconciled, false/none if no update needed.</returns>
    Task<OperationResult<PublisherReconciliationResult>> CheckAndReconcileIfNeededAsync(string triggeringProfileId, CancellationToken cancellationToken = default);
}
