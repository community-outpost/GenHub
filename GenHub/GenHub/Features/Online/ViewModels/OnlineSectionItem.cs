namespace GenHub.Features.Online.ViewModels;

/// <summary>
/// Represents a section entry in the Online sidebar navigation.
/// </summary>
/// <param name="Id">The unique identifier of the online section.</param>
/// <param name="Title">The display title of the online section.</param>
/// <param name="Description">The short description shown under the title.</param>
/// <param name="IconData">The SVG path data representing the section icon.</param>
/// <param name="IconImageSource">Optional logo image source shown instead of the path icon.</param>
public sealed record OnlineSectionItem(
    string Id,
    string Title,
    string Description,
    string IconData,
    string? IconImageSource = null)
{
    /// <summary>
    /// Gets a value indicating whether the section shows a logo image instead of a path icon.
    /// </summary>
    public bool HasIconImage => IconImageSource is not null;
}
