namespace GenHub.Core.Messages;

/// <summary>
/// Message broadcast when publisher subscriptions are added, updated, or removed.
/// </summary>
/// <param name="PublisherId">The optional publisher ID that changed.</param>
public record PublisherSubscriptionsChangedMessage(string? PublisherId = null);
