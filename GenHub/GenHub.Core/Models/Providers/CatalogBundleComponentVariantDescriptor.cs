using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// One installable option of a bundle component (a resolution, language, or the sole artifact).
/// </summary>
public sealed class CatalogBundleComponentVariantDescriptor
{
    /// <summary>Gets or sets the variant label shown in the dropdown (empty for non-variant content).</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets the variant axis (e.g. <c>resolution</c>).</summary>
    [JsonPropertyName("axis")]
    public string Axis { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether this option is the default selection.</summary>
    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; }

    /// <summary>Gets or sets the discoverer catalog ID for this variant.</summary>
    [JsonPropertyName("catalogId")]
    public string CatalogId { get; set; } = string.Empty;

    /// <summary>Gets or sets the serialized release JSON the resolver should use.</summary>
    [JsonPropertyName("releaseJson")]
    public string ReleaseJson { get; set; } = string.Empty;

    /// <summary>Gets or sets the download size in bytes.</summary>
    [JsonPropertyName("downloadSize")]
    public long DownloadSize { get; set; }
}
