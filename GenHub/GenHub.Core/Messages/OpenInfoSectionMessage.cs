using CommunityToolkit.Mvvm.Messaging.Messages;

namespace GenHub.Core.Messages;

/// <summary>
/// Message sent to request navigation to a specific info section.
/// </summary>
/// <param name="sectionId">The ID of the section to open.</param>
public class OpenInfoSectionMessage(string sectionId) : ValueChangedMessage<string>(sectionId);
