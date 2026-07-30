using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Launching;

/// <summary>
/// Records a receipt of what each launch consisted of and cheaply revalidates it before
/// subsequent launches so drift is detected without a full re-scan.
/// </summary>
public interface ILaunchReceiptService
{
    /// <summary>
    /// Records a receipt for a launch into the workspace directory, replacing any previous one.
    /// </summary>
    /// <param name="context">What the launch consisted of.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The recorded receipt, or a failure that must not block the launch.</returns>
    Task<OperationResult<LaunchReceipt>> RecordLaunchAsync(LaunchReceiptContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cheaply compares the receipt in a workspace, if one exists, against the current
    /// on-disk state. Only existence, counts, sizes and timestamps are recomputed; nothing
    /// is hashed.
    /// </summary>
    /// <param name="workspacePath">The workspace directory the receipt would live in.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A drift report; an absent receipt yields an empty report, not a failure.</returns>
    Task<OperationResult<LaunchReceiptDriftReport>> RevalidateAsync(string workspacePath, CancellationToken cancellationToken = default);
}
