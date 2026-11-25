namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// A choice in the Content page filter.
/// </summary>
/// <param name="Group">The content group to show, or null to show all content.</param>
public sealed record ProfileContentFilterOption(ProfileContentGroup? Group)
{
    /// <summary>
    /// Gets the option that shows all content.
    /// </summary>
    public static ProfileContentFilterOption All { get; } = new((ProfileContentGroup?)null);

    /// <summary>
    /// Gets a value indicating whether this option shows all content.
    /// </summary>
    public bool IsAll => Group == null;
}
