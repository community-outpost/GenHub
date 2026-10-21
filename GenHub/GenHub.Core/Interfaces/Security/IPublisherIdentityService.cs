using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Security;

namespace GenHub.Core.Interfaces.Security;

/// <summary>
/// Detects publisher ID collisions between a subscription candidate and an existing subscription,
/// and verifies that a moved publisher still holds the key trusted on first use.
/// </summary>
public interface IPublisherIdentityService
{
    /// <summary>
    /// Compares a candidate with the existing subscription for the same publisher ID.
    /// </summary>
    /// <param name="existing">The existing subscription, or null when there is none.</param>
    /// <param name="candidate">The source the user is about to subscribe to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The assessment. An unreadable key store yields <see cref="Models.Enums.PublisherSourceChange.IdentityMismatch"/> for a new source.</returns>
    Task<PublisherSourceAssessment> AssessAsync(
        PublisherSubscription? existing,
        PublisherSourceCandidate candidate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the trusted key after a subscription was saved.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="assessment">The assessment the subscription was saved under.</param>
    /// <param name="replaceTrust">True for a first subscription or a replaced publisher: the proven key replaces any trusted key, and without one the trusted key is removed. False keeps a trusted key and only trusts the proven key when none is stored.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    Task<OperationResult> RecordTrustAsync(
        string publisherId,
        PublisherSourceAssessment assessment,
        bool replaceTrust,
        CancellationToken cancellationToken = default);
}
