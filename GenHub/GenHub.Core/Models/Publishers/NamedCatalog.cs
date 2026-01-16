using GenHub.Core.Models.Providers;

namespace GenHub.Core.Models.Publishers;

/// <summary>
/// A named catalog within a multi-catalog publisher project.
/// </summary>
public class NamedCatalog
{
    /// <summary>
    /// Unique ID for this catalog (e.g., "zh-mods", "maps").
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name for this catalog.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of what this catalog contains.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The catalog data containing content items and releases.
    /// </summary>
    public PublisherCatalog Catalog { get; set; } = new();

    /// <summary>
    /// The filename for this catalog when exported (e.g., "catalog-zh-mods.json").
    /// </summary>
    public string FileName { get; set; } = "catalog.json";
}
