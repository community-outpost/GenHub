using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// A downloadable member of a <see cref="Enums.ContentType.ContentBundle"/>, serialized onto
/// the bundle search result so the card can render per-component identity and variant pickers
/// without depending on sibling cards being visible in the current grid.
/// </summary>
public sealed class CatalogBundleComponentDescriptor
{
    /// <summary>Gets or sets the publisher id of the component.</summary>
    [JsonPropertyName("publisherId")]
    public string PublisherId { get; set; } = string.Empty;

    /// <summary>Gets or sets the catalog content id of the component.</summary>
    [JsonPropertyName("contentId")]
    public string ContentId { get; set; } = string.Empty;

    /// <summary>Gets or sets the display name of the component.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the content type name (e.g. <c>GameClient</c>, <c>Addon</c>).</summary>
    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the component is optional.</summary>
    [JsonPropertyName("isOptional")]
    public bool IsOptional { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a base-game installation constraint
    /// rather than downloadable catalog content.
    /// </summary>
    [JsonPropertyName("isBaseGame")]
    public bool IsBaseGame { get; set; }

    /// <summary>Gets or sets the serialized catalog item JSON used to acquire this component.</summary>
    [JsonPropertyName("catalogItemJson")]
    public string CatalogItemJson { get; set; } = string.Empty;

    /// <summary>Gets or sets installable variants (one entry for non-variant content).</summary>
    [JsonPropertyName("variants")]
    public List<CatalogBundleComponentVariantDescriptor> Variants { get; set; } = [];
}
