using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Defines the contract for reconciling publisher profiles.
/// </summary>
public interface IPublisherReconciler
{
    /// <summary>
    /// Gets the publisher type this reconciler handles (e.g., "generalsonline").
    /// </summary>
    string PublisherType { get; }

    /// <summary>
    /// Checks for updates and reconciles profiles if needed.
    /// </summary>
    /// <param name="triggeringProfileId">The ID of the profile that triggered the check.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation, returning an operation result indicating if reconciliation was needed and performed.</returns>
    Task<OperationResult<bool>> CheckAndReconcileIfNeededAsync(string triggeringProfileId, CancellationToken cancellationToken = default);
}
