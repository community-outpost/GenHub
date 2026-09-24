using GenHub.Core.Constants;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Represents a catalog within a publisher definition (V2 schema).
/// Each publisher can have multiple catalogs for different content types.
/// </summary>
public class CatalogEntry
{
    private List<string> _mirrors = [];

    /// <summary>
    /// Gets or sets the unique ID for this catalog within the publisher (e.g., "zh-mods", "maps").
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable name for this catalog (e.g., "ZH Mods", "Maps").
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional description explaining what this catalog contains.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets an optional icon or avatar URL for this catalog.
    /// </summary>
    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; set; }

    /// <summary>
    /// Gets or sets the primary URL where this catalog JSON is hosted.
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the alternate URLs for redundancy.
    /// </summary>
    [JsonPropertyName("mirrors")]
    public List<string> Mirrors
    {
        get => _mirrors ??= [];
        set => _mirrors = value ?? [];
    }

    /// <summary>
    /// Gets the effective icon URL for this catalog, falling back to a deterministic placeholder.
    /// </summary>
    [JsonIgnore]
    public string EffectiveIconUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(IconUrl))
            {
                return IconUrl;
            }

            var seed = !string.IsNullOrWhiteSpace(Id) ? Id : Name;
            return ImageCacheConstants.GetPicsumUrl(seed, 128, 128);
        }
    }
}
