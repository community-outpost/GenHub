using GenHub.Core.Models.Providers;

namespace GenHub.Core.Models.Publishers;

/// <summary>
/// A named catalog within a multi-catalog publisher project.
/// </summary>
public class NamedCatalog
{
    /// <summary>
    /// Gets or sets the unique ID for this catalog (e.g., "zh-mods", "maps").
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable name for this catalog.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional description of what this catalog contains.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the catalog data containing content items and releases.
    /// </summary>
    public PublisherCatalog Catalog { get; set; } = new();

    /// <summary>
    /// Gets or sets the filename for this catalog when exported (e.g., "catalog-zh-mods.json").
    /// </summary>
    public string FileName { get; set; } = "catalog.json";
}
