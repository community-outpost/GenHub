using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Content;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Provides audit logging for reconciliation operations.
/// </summary>
public interface IReconciliationAuditLog
{
    /// <summary>
    /// Logs an operation to the audit trail.
    /// </summary>
    /// <param name="entry">The audit entry to log.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task LogOperationAsync(ReconciliationAuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent audit history.
    /// </summary>
    /// <param name="count">Maximum number of entries to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of recent audit entries, ordered by timestamp descending.</returns>
    Task<IReadOnlyList<ReconciliationAuditEntry>> GetRecentHistoryAsync(
        int count = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets audit history for a specific profile.
    /// </summary>
    /// <param name="profileId">The profile ID to filter by.</param>
    /// <param name="count">Maximum number of entries to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of audit entries affecting the profile.</returns>
    Task<IReadOnlyList<ReconciliationAuditEntry>> GetProfileHistoryAsync(
        string profileId,
        int count = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets audit history for a specific manifest.
    /// </summary>
    /// <param name="manifestId">The manifest ID to filter by.</param>
    /// <param name="count">Maximum number of entries to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of audit entries affecting the manifest.</returns>
    Task<IReadOnlyList<ReconciliationAuditEntry>> GetManifestHistoryAsync(
        string manifestId,
        int count = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears old audit entries beyond retention period.
    /// </summary>
    /// <param name="retentionDays">Number of days to retain entries.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of entries removed.</returns>
    Task<int> PurgeOldEntriesAsync(
        int retentionDays = 30,
        CancellationToken cancellationToken = default);
}
