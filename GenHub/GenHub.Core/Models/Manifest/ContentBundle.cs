using System;
using System.Collections.Generic;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Represents a bundle of content packages, such as mods, patches, or add-ons, grouped for distribution or installation.
/// </summary>
public class ContentBundle
{
    /// <summary>
    /// Gets or sets the unique bundle identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the bundle display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the bundle version.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the bundle publisher information.
    /// </summary>
    public PublisherInfo Publisher { get; set; } = new();

    /// <summary>
    /// Gets or sets the bundle contents.
    /// </summary>
    public List<BundleItem> Items { get; set; } = new();

    /// <summary>
    /// Gets or sets the bundle metadata.
    /// </summary>
    public ContentMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Gets or sets the creation date and time of the bundle.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
