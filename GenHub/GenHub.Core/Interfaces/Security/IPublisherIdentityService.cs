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
    /// Gets the key currently trusted for a publisher, so a replace can be undone.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The trusted key, null when none is stored, or a failure when the store cannot be read.</returns>
    Task<OperationResult<TrustedPublisherKey?>> GetTrustedKeyAsync(string publisherId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts back a trusted key captured with <see cref="GetTrustedKeyAsync"/>, or removes the trusted key when none was captured.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="previous">The captured key to save back. Null removes the trusted key, which restores a store that had no key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    Task<OperationResult> RestoreTrustAsync(string publisherId, TrustedPublisherKey? previous, CancellationToken cancellationToken = default);

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
