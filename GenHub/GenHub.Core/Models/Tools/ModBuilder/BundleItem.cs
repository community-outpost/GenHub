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
    /// Gets or sets the list of files to be processed in this bundle item.
    /// </summary>
    [JsonPropertyName("files")]
    public List<BundleFile> Files { get; set; } = new();

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
