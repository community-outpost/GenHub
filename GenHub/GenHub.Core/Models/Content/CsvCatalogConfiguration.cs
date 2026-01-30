using System.Collections.Generic;

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

/// <summary>
/// Represents a CSV catalog defined in appsettings.json.
/// Matches the structure of CsvCatalogRegistryEntry but optimized for config binding.
/// </summary>
public class CsvValidationCatalog
{
    /// <summary>
    /// Gets or sets the URL to the CSV file.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game type.
    /// </summary>
    public string GameType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version string.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the supported languages.
    /// </summary>
    public List<string> SupportedLanguages { get; set; } = [];

    /// <summary>
    /// Gets or sets the file count.
    /// </summary>
    public int FileCount { get; set; }
}
