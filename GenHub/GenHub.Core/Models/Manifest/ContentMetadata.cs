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
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the icon URL.
    /// </summary>
    public string? IconUrl { get; set; }

    /// <summary>
    /// Gets or sets the cover image URL.
    /// </summary>
    public string? CoverUrl { get; set; }

    /// <summary>
    /// Gets or sets the screenshot URLs.
    /// </summary>
    public List<string> ScreenshotUrls { get; set; } = [];

    /// <summary>
    /// Gets or sets the release date.
    /// </summary>
    public DateTime ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets the changelog URL.
    /// </summary>
    public string? ChangelogUrl { get; set; }

    /// <summary>
    /// Gets or sets the theme color.
    /// </summary>
    public string? ThemeColor { get; set; }

    /// <summary>
    /// Gets or sets the original source path where this content was installed or located.
    /// Used for GameInstallation manifests to persist installation paths across sessions.
    /// </summary>
    public string? SourcePath { get; set; }

    /// <summary>
    /// Gets or sets the available variants for this content.
    /// Variants allow users to select specific configurations (e.g., resolution, language).
    /// </summary>
    public List<ContentVariant>? Variants { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this content requires variant selection.
    /// If true, user must select a variant before installation.
    /// </summary>
    public bool RequiresVariantSelection { get; set; }

    /// <summary>
    /// Gets or sets the currently selected variant ID.
    /// Used when creating profile-specific manifests from variant content.
    /// </summary>
    public string? SelectedVariantId { get; set; }

    /// <summary>
    /// Gets or sets the stable group key shared by every manifest that is a variant of the same
    /// release (e.g. all five Control Bar Pro resolutions). The downloads browser groups sibling
    /// cards by this id so they render as a single card with a variant picker instead of N
    /// unrelated cards. Null/empty for single-variant content.
    /// </summary>
    public string? VariantGroupId { get; set; }

    /// <summary>
    /// Gets or sets the display name of the variant family (e.g. "Control Bar Pro (Xezon)"),
    /// shown as the card title when multiple variants share a <see cref="VariantGroupId"/>.
    /// </summary>
    public string? VariantFamilyName { get; set; }
}
