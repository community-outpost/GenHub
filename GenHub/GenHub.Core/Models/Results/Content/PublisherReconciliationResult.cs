using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Results.Content;

/// <summary>
/// Represents the result of a publisher profile reconciliation operation.
/// </summary>
public sealed class PublisherReconciliationResult
{
    /// <summary>
    /// Gets a result indicating no reconciliation was needed or performed.
    /// </summary>
    public static PublisherReconciliationResult None => new() { Reconciled = false };

    /// <summary>
    /// Creates a result indicating reconciliation was performed.
    /// </summary>
    /// <param name="strategy">The strategy applied.</param>
    /// <param name="targetProfileId">The ID of the target profile to launch.</param>
    /// <param name="profilesAffectedCount">The number of profiles affected.</param>
    /// <returns>A new <see cref="PublisherReconciliationResult"/>.</returns>
    public static PublisherReconciliationResult Success(UpdateStrategy strategy, string? targetProfileId, int profilesAffectedCount = 1) =>
        new()
        {
            Reconciled = true,
            Strategy = strategy,
            TargetProfileId = targetProfileId,
            ProfilesAffectedCount = profilesAffectedCount,
        };

    /// <summary>
    /// Implicitly converts a <see cref="PublisherReconciliationResult"/> to a boolean indicating whether reconciliation occurred.
    /// </summary>
    /// <param name="result">The reconciliation result.</param>
    public static implicit operator bool(PublisherReconciliationResult? result) => result?.Reconciled ?? false;

    /// <summary>
    /// Gets a value indicating whether reconciliation was needed and performed.
    /// </summary>
    public bool Reconciled { get; init; }

    /// <summary>
    /// Gets the update strategy applied during reconciliation, if applicable.
    /// </summary>
    public UpdateStrategy? Strategy { get; init; }

    /// <summary>
    /// Gets the ID of the target profile to launch or use following reconciliation.
    /// When <see cref="UpdateStrategy.CreateNewProfile"/> is selected, this contains the ID
    /// of the newly created profile corresponding to the triggering profile.
    /// </summary>
    public string? TargetProfileId { get; init; }

    /// <summary>
    /// Gets the count of profiles created or updated during reconciliation.
    /// </summary>
    public int ProfilesAffectedCount { get; init; }
}
