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