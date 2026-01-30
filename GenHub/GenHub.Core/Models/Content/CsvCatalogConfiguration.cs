namespace GenHub.Core.Models.Content;

/// <summary>
/// Configuration for CSV Catalog discovery.
/// Binds to "CsvValidationCatalogs" and "CsvCatalogIndexPath" in configuration.
/// </summary>
public class CsvCatalogConfiguration
{
    /// <summary>
    /// Gets or sets the list of fallback validation catalogs defined in configuration.
    /// Used when index.json cannot be reached.
    /// </summary>
    public List<CsvValidationCatalog> CsvValidationCatalogs { get; set; } = [];

    /// <summary>
    /// Gets or sets the path to the index.json file relative to the docs directory.
    /// </summary>
    public string IndexFilePath { get; set; } = "docs/GameInstallationFilesRegistry/index.json";
}