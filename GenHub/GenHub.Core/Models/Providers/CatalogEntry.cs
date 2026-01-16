using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Represents a catalog within a publisher definition (V2 schema).
/// Each publisher can have multiple catalogs for different content types.
/// </summary>
public class CatalogEntry
{
    /// <summary>
    /// Unique ID for this catalog within the publisher (e.g., "zh-mods", "maps").
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name for this catalog (e.g., "ZH Mods", "Maps").
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description explaining what this catalog contains.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Primary URL where this catalog JSON is hosted.
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Alternate URLs for redundancy.
    /// </summary>
    [JsonPropertyName("mirrors")]
    public List<string> Mirrors { get; set; } = [];
}
