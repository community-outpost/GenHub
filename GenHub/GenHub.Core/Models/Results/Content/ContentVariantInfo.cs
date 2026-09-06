namespace GenHub.Core.Models.Results.Content;

/// <summary>
/// Describes one selectable variant of a content release as surfaced to the downloads browser.
/// Carries the stable identity needed to group sibling cards and to resolve install state per
/// variant, independent of the on-disk manifest model.
/// </summary>
public class ContentVariantInfo
{
    /// <summary>
    /// Gets or sets the variant identifier within its family (e.g. "1080p", "zerohour", "english").
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the variant (e.g. "1080p (Recommended)").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the variant discriminator type (e.g. "resolution", "language", "game-type").
    /// </summary>
    public string VariantType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the manifest id this variant resolves to once downloaded, when known.
    /// May be empty for variants that are only named at discovery time.
    /// </summary>
    public string ManifestId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this is the recommended/default variant.
    /// </summary>
    public bool IsDefault { get; set; }
}
