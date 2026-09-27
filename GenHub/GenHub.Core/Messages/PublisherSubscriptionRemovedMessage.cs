namespace GenHub.Core.Messages;

/// <summary>
/// Announces that a publisher catalog subscription was removed.
/// </summary>
/// <param name="PublisherId">The identifier of the removed publisher.</param>
public sealed record PublisherSubscriptionRemovedMessage(string PublisherId);
