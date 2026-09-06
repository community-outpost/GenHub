using CommunityToolkit.Mvvm.Messaging.Messages;

namespace GenHub.Core.Messages;

/// <summary>
/// Message sent when a user wants to close the content details view.
/// </summary>
public class CloseContentDetailMessage() : ValueChangedMessage<bool>(true)
{
}
