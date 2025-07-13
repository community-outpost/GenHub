namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Rich metadata for content discovery and presentation.
/// </summary>
public class ContentMetadata
{
    /// <summary>
    /// Gets or sets the content description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the content tags.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Gets or sets the icon URL.
    /// </summary>
    public string IconUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the screenshot URLs.
    /// </summary>
    public List<string> ScreenshotUrls { get; set; } = new();

    /// <summary>
    /// Gets or sets the release date.
    /// </summary>
    public DateTime ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets the changelog URL.
    /// </summary>
    public string ChangelogUrl { get; set; } = string.Empty;
}
