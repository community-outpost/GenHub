using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Providers;

/// <summary>
/// Manages user subscriptions to publisher catalogs.
/// Subscriptions are stored locally and enable discovery of creator content.
/// </summary>
public interface IPublisherSubscriptionStore
{
    /// <summary>
    /// Retrieve all active publisher subscriptions.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An OperationResult containing the sequence of active <see cref="PublisherSubscription"/> entries.</returns>
    Task<OperationResult<IEnumerable<PublisherSubscription>>> GetSubscriptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve the subscription for the specified publisher.
    /// </summary>
    /// <param name="publisherId">The identifier of the publisher whose subscription is requested.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The subscription for the specified publisher, or null if no subscription exists.</returns>
    Task<OperationResult<PublisherSubscription?>> GetSubscriptionAsync(string publisherId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new publisher subscription to the local store.
    /// </summary>
    /// <param name="subscription">The subscription to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An OperationResult containing `true` if the subscription was added, `false` otherwise.</returns>
    Task<OperationResult<bool>> AddSubscriptionAsync(PublisherSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove the subscription for the specified publisher from the local store.
    /// </summary>
    /// <param name="publisherId">The identifier of the publisher whose subscription to remove.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An OperationResult containing `true` if the subscription was removed, `false` otherwise.</returns>
    Task<OperationResult<bool>> RemoveSubscriptionAsync(string publisherId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing publisher subscription with the provided data.
    /// </summary>
    /// <param name="subscription">The subscription object containing updated values to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>`true` if the subscription was updated, `false` otherwise.</returns>
    Task<OperationResult<bool>> UpdateSubscriptionAsync(PublisherSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether an active subscription exists for the specified publisher.
    /// </summary>
    /// <param name="publisherId">The identifier of the publisher to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>`true` if a subscription exists for the publisher, `false` otherwise.</returns>
    Task<OperationResult<bool>> IsSubscribedAsync(string publisherId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the trust level for an existing publisher subscription.
    /// </summary>
    /// <param name="publisherId">The identifier of the publisher whose trust level will be updated.</param>
    /// <param name="trustLevel">The new trust level to apply to the subscription.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An OperationResult containing `true` if the trust level was updated, `false` otherwise.</returns>
    Task<OperationResult<bool>> UpdateTrustLevelAsync(string publisherId, TrustLevel trustLevel, CancellationToken cancellationToken = default);
}