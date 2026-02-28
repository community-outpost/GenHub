using System.Collections.Generic;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Represents a single CSV catalog entry in the registry.
/// </summary>
public class CsvCatalogRegistryEntry
{
    /// <summary>
    /// Gets or sets the URL to the CSV file.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game type (e.g., "Generals", "ZeroHour").
    /// </summary>
    public string GameType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game version (e.g., "1.08", "1.04").
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of supported languages in this CSV.
    /// </summary>
    public List<string> SupportedLanguages { get; set; } = [];

    /// <summary>
    /// Gets or sets the optional file count in the CSV.
    /// </summary>
    public int? FileCount { get; set; }
}
