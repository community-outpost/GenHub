using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents a bundle item containing file mappings and build configuration.
/// </summary>
public class BundleItem
{
    /// <summary>
    /// Gets or sets the unique name of this bundle item.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the optional description of this bundle item.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the list of files to be processed in this bundle item.
    /// </summary>
    [JsonPropertyName("files")]
    public List<BundleFile> Files { get; set; } = new();

    /// <summary>
    /// Gets or sets the original source patterns as configured, before wildcard resolution
    /// replaces <see cref="Files"/> with resolved entries. Editors and serializers use these
    /// so saving never persists resolved absolute paths back into the configuration.
    /// </summary>
    [JsonPropertyName("sourcePatterns")]
    public List<string> SourcePatterns { get; set; } = new();

    /// <summary>
    /// Gets or sets the target directory template applied to resolved files.
    /// </summary>
    [JsonPropertyName("targetDir")]
    public string TargetDir { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base source directory for resolving relative patterns.
    /// </summary>
    [JsonPropertyName("baseDir")]
    public string BaseDir { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the prefix to add to the bundle item name.
    /// </summary>
    [JsonPropertyName("namePrefix")]
    public string NamePrefix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the suffix to add to the bundle item name.
    /// </summary>
    [JsonPropertyName("nameSuffix")]
    public string NameSuffix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this bundle should be packaged as a .big archive.
    /// </summary>
    [JsonPropertyName("isBig")]
    public bool IsBig { get; set; } = true;

    /// <summary>
    /// Gets or sets the suffix to add to the .big archive name.
    /// </summary>
    [JsonPropertyName("bigSuffix")]
    public string BigSuffix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional manifest file path for byte-for-byte reproducible BIG packing.
    /// When the item contains the same file set as a release pack, referencing the pack
    /// manifest makes the intermediate item archive byte-identical to the release archive.
    /// </summary>
    [JsonPropertyName("manifestFile")]
    public string? ManifestFile { get; set; }

    /// <summary>
    /// Gets or sets the game language to set on installation.
    /// </summary>
    [JsonPropertyName("setGameLanguageOnInstall")]
    public string SetGameLanguageOnInstall { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the event callbacks for this bundle item.
    /// </summary>
    [JsonPropertyName("events")]
    public Dictionary<BundleEventType, BundleEvent> Events { get; set; } = new();

    /// <summary>
    /// Gets the full name of this bundle item including prefix and suffix.
    /// </summary>
    /// <returns>The full name of the bundle item.</returns>
    public string GetFullName()
    {
        return $"{NamePrefix}{Name}{NameSuffix}";
    }
}
