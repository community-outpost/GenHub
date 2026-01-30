using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

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
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the index was last updated.
    /// </summary>
    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the list of available catalog entries.
    /// </summary>
    [JsonPropertyName("registries")]
    public List<CsvCatalogRegistryEntry> Entries { get; set; } = [];
}