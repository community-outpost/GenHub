using GenHub.Core.Models.Enums;
using System;

namespace GenHub.Core.Models.Results.Content;

/// <summary>
/// Represents the result of a publisher profile reconciliation operation.
/// </summary>
public sealed class PublisherReconciliationResult : IEquatable<PublisherReconciliationResult>
{
    /// <summary>
    /// Gets a result indicating no reconciliation was needed or performed.
    /// </summary>
    public static PublisherReconciliationResult None { get; } = new() { Reconciled = false };

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

    /// <inheritdoc/>
    public bool Equals(PublisherReconciliationResult? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Reconciled == other.Reconciled &&
               Strategy == other.Strategy &&
               TargetProfileId == other.TargetProfileId &&
               ProfilesAffectedCount == other.ProfilesAffectedCount;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as PublisherReconciliationResult);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Reconciled, Strategy, TargetProfileId, ProfilesAffectedCount);
}
