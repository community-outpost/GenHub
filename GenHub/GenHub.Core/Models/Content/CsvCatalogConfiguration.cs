using GenHub.Core.Constants;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Configuration for CSV catalog discovery.
/// Binds to the "GenHub" configuration section.
/// </summary>
public class CsvCatalogConfiguration
{
    /// <summary>
    /// Gets or sets the preferred local path or remote URL for the catalog index.json file.
    /// Relative file paths are resolved from the application's working directory.
    /// HTTP and HTTPS URLs are downloaded directly from the configured source.
    /// If loading fails, the discoverer falls back to <see cref="CsvConstants.DefaultIndexFileUrl"/>.
    /// </summary>
    public string IndexFilePath { get; set; } = CsvConstants.DefaultIndexFileUrl;
}
