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
    /// Gets all active publisher subscriptions.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <summary>
/// Retrieve all active publisher subscriptions.
/// </summary>
/// <returns>An OperationResult containing the sequence of active <see cref="PublisherSubscription"/> entries.</returns>
    Task<OperationResult<IEnumerable<PublisherSubscription>>> GetSubscriptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific subscription by publisher ID.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <summary>
/// Retrieve the subscription for the specified publisher.
/// </summary>
/// <param name="publisherId">The identifier of the publisher whose subscription is requested.</param>
/// <returns>The subscription for the specified publisher, or null if no subscription exists.</returns>
    Task<OperationResult<PublisherSubscription?>> GetSubscriptionAsync(string publisherId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new publisher subscription.
    /// </summary>
    /// <param name="subscription">The subscription to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <summary>
/// Add a new publisher subscription to the local store.
/// </summary>
/// <param name="subscription">The subscription to add.</param>
/// <returns>An OperationResult containing `true` if the subscription was added, `false` otherwise.</returns>
    Task<OperationResult<bool>> AddSubscriptionAsync(PublisherSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a publisher subscription.
    /// </summary>
    /// <param name="publisherId">The publisher identifier to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <summary>
/// Remove the subscription for the specified publisher from the local store.
/// </summary>
/// <param name="publisherId">The identifier of the publisher whose subscription to remove.</param>
/// <param name="cancellationToken">A token to cancel the operation.</param>
/// <returns>An OperationResult containing `true` if the subscription was removed, `false` otherwise.</returns>
    Task<OperationResult<bool>> RemoveSubscriptionAsync(string publisherId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing subscription.
    /// </summary>
    /// <param name="subscription">The updated subscription data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <summary>
/// Update an existing publisher subscription with the provided data.
/// </summary>
/// <param name="subscription">The subscription object containing updated values to persist.</param>
/// <returns>`true` if the subscription was updated, `false` otherwise.</returns>
    Task<OperationResult<bool>> UpdateSubscriptionAsync(PublisherSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a publisher subscription exists.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <summary>
/// Determines whether an active subscription exists for the specified publisher.
/// </summary>
/// <param name="publisherId">The identifier of the publisher to check.</param>
/// <returns>`true` if a subscription exists for the publisher, `false` otherwise.</returns>
    Task<OperationResult<bool>> IsSubscribedAsync(string publisherId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the trust level for a publisher.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="trustLevel">The new trust level.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <summary>
/// Update the trust level for an existing publisher subscription.
/// </summary>
/// <param name="publisherId">The identifier of the publisher whose trust level will be updated.</param>
/// <param name="trustLevel">The new trust level to apply to the subscription.</param>
/// <returns>An OperationResult containing `true` if the trust level was updated, `false` otherwise.</returns>
    Task<OperationResult<bool>> UpdateTrustLevelAsync(string publisherId, TrustLevel trustLevel, CancellationToken cancellationToken = default);
}