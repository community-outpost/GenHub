using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Index of available CSV catalog registries.
/// Represents the structure of the docs/GameInstallationFilesRegistry/index.json file.
/// </summary>
public class CsvCatalogRegistryIndex
{
    /// <summary>
    /// Gets or sets the schema version.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Gets or sets when the index was last updated.
    /// </summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the list of available catalog entries.
    /// </summary>
    public List<CsvCatalogRegistryEntry> Entries { get; set; } = [];
}

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
