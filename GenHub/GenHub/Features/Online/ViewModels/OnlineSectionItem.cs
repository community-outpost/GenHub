namespace GenHub.Features.Online.ViewModels;

/// <summary>
/// Represents a section entry in the Online sidebar navigation.
/// </summary>
/// <param name="Id">The unique identifier of the online section.</param>
/// <param name="Title">The display title of the online section.</param>
/// <param name="Description">The short description shown under the title.</param>
/// <param name="IconData">The SVG path data representing the section icon.</param>
public sealed record OnlineSectionItem(
    string Id,
    string Title,
    string Description,
    string IconData);
