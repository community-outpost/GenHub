using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Represents a reference to another content item (for dependencies, referrals, etc).
/// </summary>
public class ContentReference
{
    /// <summary>
    /// Gets or sets the referenced content ID.
    /// </summary>
    public ManifestId ContentId { get; set; } = ManifestId.Create(ManifestConstants.DefaultContentDependencyId);  // Non-nullable; assigned in constructor

    /// <summary>
    /// Gets or sets the publisher ID (if cross-publisher).
    /// </summary>
    public string? PublisherId { get; set; }

    /// <summary>
    /// Gets or sets the minimum compatible version.
    /// </summary>
    public string? MinVersion { get; set; }

    /// <summary>
    /// Gets or sets the maximum compatible version.
    /// </summary>
    public string? MaxVersion { get; set; }

    /// <summary>
    /// Gets or sets the content type of the reference.
    /// </summary>
    public ContentType ContentType { get; set; }
}
