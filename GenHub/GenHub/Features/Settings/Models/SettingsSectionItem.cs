namespace GenHub.Features.Settings.Models;

/// <summary>
/// Represents a section entry in the settings sidebar navigation.
/// </summary>
/// <param name="Id">The unique identifier of the settings section.</param>
/// <param name="Title">The display title of the settings section.</param>
/// <param name="IconData">The SVG path data representing the section icon.</param>
public sealed record SettingsSectionItem(
    string Id,
    string Title,
    string IconData);
