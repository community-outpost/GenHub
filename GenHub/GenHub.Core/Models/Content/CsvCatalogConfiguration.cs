namespace GenHub.Core.Models.Content;

/// <summary>
/// Configuration for CSV catalog discovery.
/// Binds to the "GenHub" configuration section.
/// </summary>
public class CsvCatalogConfiguration
{
    /// <summary>
    /// Gets or sets the path to the catalog index.json file.
    /// If a relative path is provided, it is resolved relative to the application's working directory;
    /// absolute paths are used as given. The expected file is index.json.
    /// </summary>
    public string IndexFilePath { get; set; } = "docs/GameInstallationFilesRegistry/index.json";
}
