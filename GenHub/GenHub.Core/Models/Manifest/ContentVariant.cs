namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Represents a variant of content (e.g., different resolutions for GenTool).
/// Variants allow users to select specific configurations or options when installing content.
/// </summary>
public class ContentVariant
{
    /// <summary>
    /// Gets or sets the unique identifier for this variant.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name for this variant.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description of this variant.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the variant type (e.g., "resolution", "language", "quality").
    /// </summary>
    public string VariantType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the variant value (e.g., "1920x1080", "4K", "en-US").
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this is the default variant.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Gets or sets file path patterns to include for this variant.
    /// Supports wildcards (e.g., "*1920x1080*", "Resolution_1080p/*").
    /// </summary>
    public List<string> IncludePatterns { get; set; } = [];

    /// <summary>
    /// Gets or sets file path patterns to exclude for this variant.
    /// </summary>
    public List<string> ExcludePatterns { get; set; } = [];

    /// <summary>
    /// Gets or sets tags associated with this variant for filtering/discovery.
    /// </summary>
    public List<string> Tags { get; set; } = [];
}
