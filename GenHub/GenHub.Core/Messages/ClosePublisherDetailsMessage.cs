using CommunityToolkit.Mvvm.Messaging.Messages;

namespace GenHub.Core.Messages;

/// <summary>
/// Message sent when a user wants to close the publisher details view and return to the dashboard.
/// </summary>
public class ClosePublisherDetailsMessage() : ValueChangedMessage<bool>(true)
{
}
