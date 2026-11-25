namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Orders offered by the Content page sort menu.
/// </summary>
public enum ProfileContentSortOrder
{
    /// <summary>Alphabetical by display name.</summary>
    Name,

    /// <summary>Newest release first; content without a release date last.</summary>
    ReleaseDate,

    /// <summary>Content in the profile first.</summary>
    EnabledFirst,

    /// <summary>Grouped by content type; offered only while every type is shown.</summary>
    Type,
}
