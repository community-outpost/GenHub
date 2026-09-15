namespace GenHub.Core.Models.Content;

/// <summary>
/// Represents the result of a content reconciliation operation.
/// </summary>
/// <param name="ProfilesUpdated">The number of profiles whose content IDs were updated.</param>
/// <param name="WorkspacesInvalidated">The number of workspaces that were invalidated/deleted due to content changes.</param>
/// <param name="FailedProfilesCount">The number of profiles that failed to reconcile.</param>
public record ReconciliationResult(int ProfilesUpdated, int WorkspacesInvalidated, int FailedProfilesCount = 0)
{
    /// <summary>
    /// Gets an empty reconciliation result. Cached to avoid unnecessary allocations.
    /// </summary>
    public static ReconciliationResult Empty { get; } = new(0, 0, 0);

    /// <summary>
    /// Combines two reconciliation results.
    /// </summary>
    /// <param name="left">The first reconciliation result to combine. Cannot be null.</param>
    /// <param name="right">The second reconciliation result to combine. Cannot be null.</param>
    /// <returns>A new reconciliation result with combined counts.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either <paramref name="left"/> or <paramref name="right"/> is null.</exception>
    public static ReconciliationResult operator +(ReconciliationResult left, ReconciliationResult right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new ReconciliationResult(
            left.ProfilesUpdated + right.ProfilesUpdated,
            left.WorkspacesInvalidated + right.WorkspacesInvalidated,
            left.FailedProfilesCount + right.FailedProfilesCount);
    }
}
