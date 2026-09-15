using System.Collections.Generic;

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
}
