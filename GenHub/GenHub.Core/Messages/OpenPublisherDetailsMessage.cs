using CommunityToolkit.Mvvm.Messaging.Messages;

namespace GenHub.Core.Messages;

/// <summary>
/// Message sent when a user wants to view details for a specific publisher.
/// </summary>
/// <param name="publisherId">The ID of the publisher to view.</param>
public class OpenPublisherDetailsMessage(string publisherId) : ValueChangedMessage<string>(publisherId)
{
}
