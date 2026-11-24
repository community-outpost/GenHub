using GenHub.Core.Models.Enums;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// A choice in the Content page type filter.
/// </summary>
/// <param name="ContentType">The content type to show, or null to show every type.</param>
public sealed record ProfileContentFilterOption(ContentType? ContentType)
{
    /// <summary>
    /// Gets the option that shows every content type.
    /// </summary>
    public static ProfileContentFilterOption All { get; } = new((ContentType?)null);

    /// <summary>
    /// Gets a value indicating whether this option shows every content type.
    /// </summary>
    public bool IsAll => ContentType == null;
}
