using System.Collections.Generic;

namespace GenHub.Core.Models.Content;

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
    public int? FileCount { get; set; }
}
