using System.Collections.Generic;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Represents the CSV registry index containing metadata about available CSV files.
/// </summary>
public class CsvRegistryIndex
{
    /// <summary>
    /// Gets or sets the version of the index format.
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Gets or sets the last updated timestamp in ISO 8601 format.
    /// </summary>
    public string LastUpdated { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description of the registry.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of available registries.
    /// </summary>
    public List<CsvRegistryEntry> Registries { get; set; } = new();
}

/// <summary>
/// Represents a single registry entry in the CSV index.
/// </summary>
public class CsvRegistryEntry
{
    /// <summary>
    /// Gets or sets the unique identifier for the registry.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game type (Generals or ZeroHour).
    /// </summary>
    public string GameType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game version.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL to the CSV file.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of files in the CSV.
    /// </summary>
    public int FileCount { get; set; }

    /// <summary>
    /// Gets or sets the total size of all files in bytes.
    /// </summary>
    public long TotalSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the list of supported languages.
    /// </summary>
    public List<string> Languages { get; set; } = new();

    /// <summary>
    /// Gets or sets the checksum information.
    /// </summary>
    public CsvChecksum Checksum { get; set; } = new();

    /// <summary>
    /// Gets or sets the generation timestamp in ISO 8601 format.
    /// </summary>
    public string GeneratedAt { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the generator version.
    /// </summary>
    public string GeneratorVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this registry is active.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Represents checksum information for a CSV file.
/// </summary>
public class CsvChecksum
{
    /// <summary>
    /// Gets or sets the MD5 checksum.
    /// </summary>
    public string Md5 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SHA256 checksum.
    /// </summary>
    public string Sha256 { get; set; } = string.Empty;
}
