namespace GenHub.Features.Content.Services.CommunityOutpost.Models;

/// <summary>
/// Represents a download mirror for a GenPatcher content item.
/// </summary>
public class GenPatcherMirror
{
    /// <summary>
    /// Gets or sets the mirror name (e.g., "gentool.net", "legi.cc", "drive.google.com").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the download URL.
    /// </summary>
    public string Url { get; set; } = string.Empty;
}
