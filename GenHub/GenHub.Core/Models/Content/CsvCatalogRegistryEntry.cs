using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Represents a single CSV catalog entry in the registry.
/// </summary>
public class CsvCatalogRegistryEntry
{
    /// <summary>
    /// Gets or sets the unique identifier for this registry entry.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL to the CSV file.
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game type (e.g., "Generals", "ZeroHour").
    /// </summary>
    [JsonPropertyName("gameType")]
    public string GameType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game version (e.g., "1.08", "1.04").
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of supported languages in this CSV.
    /// </summary>
    [JsonPropertyName("languages")]
    public List<string> SupportedLanguages { get; set; } = [];

    /// <summary>
    /// Gets or sets the optional file count in the CSV.
    /// </summary>
    [JsonPropertyName("fileCount")]
    public int? FileCount { get; set; }

    /// <summary>
    /// Gets or sets the total size of the CSV file in bytes.
    /// </summary>
    [JsonPropertyName("totalSizeBytes")]
    public long TotalSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the checksum information for integrity verification.
    /// </summary>
    [JsonPropertyName("checksum")]
    public Checksum? Checksum { get; set; }

    /// <summary>
    /// Gets or sets when this registry entry was generated.
    /// </summary>
    [JsonPropertyName("generatedAt")]
    public DateTime? GeneratedAt { get; set; }

    /// <summary>
    /// Gets or sets the version of the generator that created this entry.
    /// </summary>
    [JsonPropertyName("generatorVersion")]
    public string GeneratorVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this registry entry is active.
    /// </summary>
    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }
}
