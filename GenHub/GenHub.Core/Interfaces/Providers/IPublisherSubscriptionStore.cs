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
    /// <returns>List of active subscriptions.</returns>
    Task<OperationResult<IEnumerable<PublisherSubscription>>> GetSubscriptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific subscription by publisher ID.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The subscription if found, null otherwise.</returns>
    Task<OperationResult<PublisherSubscription?>> GetSubscriptionAsync(string publisherId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new publisher subscription.
    /// </summary>
    /// <param name="subscription">The subscription to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> AddSubscriptionAsync(PublisherSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a publisher subscription.
    /// </summary>
    /// <param name="publisherId">The publisher identifier to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> RemoveSubscriptionAsync(string publisherId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing subscription.
    /// </summary>
    /// <param name="subscription">The updated subscription data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> UpdateSubscriptionAsync(PublisherSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a publisher subscription exists.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if subscribed, false otherwise.</returns>
    Task<OperationResult<bool>> IsSubscribedAsync(string publisherId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the trust level for a publisher.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="trustLevel">The new trust level.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> UpdateTrustLevelAsync(string publisherId, TrustLevel trustLevel, CancellationToken cancellationToken = default);
}
