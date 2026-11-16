using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Represents an item within a content bundle, referencing a specific content package and its metadata.
/// </summary>
public class BundleItem
{
    /// <summary>
    /// Gets or sets the content ID to include in the bundle.
    /// </summary>
    public string ContentId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the publisher ID for cross-publisher bundles.
    /// </summary>
    public string PublisherId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the required version of the content.
    /// </summary>
    public string? RequiredVersion { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this item is optional in the bundle.
    /// </summary>
    public bool IsOptional { get; set; } = false;

    /// <summary>
    /// Gets or sets the type of content for this bundle item.
    /// </summary>
    public ContentType ContentType { get; set; }

    /// <summary>
    /// Gets or sets the display order of this item within the bundle.
    /// </summary>
    public int DisplayOrder { get; set; } = 0;
}
