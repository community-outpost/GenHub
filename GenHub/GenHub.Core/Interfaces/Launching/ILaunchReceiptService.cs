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

    /// <summary>
    /// Compares an upcoming launch's configuration against a previously recorded receipt:
    /// game client, game type, executable path, manifest set and versions, and the archive
    /// root paths about to be configured — the configuration itself, where
    /// <see cref="RevalidateAsync"/> checks what is on disk. Touches no filesystem state.
    /// </summary>
    /// <remarks>
    /// A separate step because the two halves are known at different times: the receipt must
    /// be read before workspace preparation rebuilds the workspace, while the upcoming
    /// configuration — the resolved executable path in particular — exists only afterwards.
    /// Profile identity is deliberately not compared: the receipt lives in the workspace and
    /// the workspace is per-profile, so a mismatch cannot occur without the receipt being a
    /// different file.
    /// </remarks>
    /// <param name="receipt">The receipt from the previous launch.</param>
    /// <param name="upcoming">The configuration of the launch about to happen.</param>
    /// <returns>A drift report naming each configuration field that changed.</returns>
    LaunchReceiptDriftReport CompareUpcomingLaunch(LaunchReceipt receipt, LaunchReceiptContext upcoming);
}
