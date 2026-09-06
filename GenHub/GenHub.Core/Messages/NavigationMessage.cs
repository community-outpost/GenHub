using GenHub.Core.Models.Enums;

namespace GenHub.Core.Messages;

/// <summary>
/// Message used to request navigation to a specific tab.
/// </summary>
/// <param name="Tab">The navigation tab to select.</param>
public record NavigationMessage(NavigationTab Tab);
