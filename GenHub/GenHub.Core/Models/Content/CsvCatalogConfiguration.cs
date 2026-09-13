using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Configuration for CSV catalog discovery.
/// Binds to the "GenHub" configuration section.
/// </summary>
public class CsvCatalogConfiguration
{
    /// <summary>
    /// Gets or sets the configured local path or remote URL for the catalog index.json file.
    /// </summary>
    public string IndexFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the fallback validation catalogs defined in configuration.
    /// </summary>
    public List<CsvCatalogRegistryEntry> CsvValidationCatalogs { get; set; } = [];

    /// <summary>
    /// Creates a deep copy of the current <see cref="CsvCatalogConfiguration"/> instance.
    /// </summary>
    /// <returns>A new <see cref="CsvCatalogConfiguration"/> instance with identical values.</returns>
    public CsvCatalogConfiguration Clone() => new()
    {
        IndexFilePath = IndexFilePath,
        CsvValidationCatalogs = CsvValidationCatalogs != null
            ? [.. CsvValidationCatalogs.Select(c => c.Clone())]
            : [],
    };
}
