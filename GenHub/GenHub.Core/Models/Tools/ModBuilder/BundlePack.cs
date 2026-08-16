using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents a grouping of bundle items for distribution and installation.
/// </summary>
public class BundlePack
{
    /// <summary>
    /// Gets or sets the unique name of this bundle pack.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the list of bundle item names included in this pack.
    /// </summary>
    [JsonPropertyName("itemNames")]
    public List<string> ItemNames { get; set; } = new();

    /// <summary>
    /// Alias property for itemNames to support "items" JSON key.
    /// </summary>
    [JsonIgnore]
    public List<string>? Items
    {
        get => ItemNames;
        set
        {
            if (value != null)
            {
                ItemNames = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the prefix to add to the bundle pack name.
    /// </summary>
    [JsonPropertyName("namePrefix")]
    public string NamePrefix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the suffix to add to the bundle pack name.
    /// </summary>
    [JsonPropertyName("nameSuffix")]
    public string NameSuffix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this pack should be built.
    /// </summary>
    [JsonPropertyName("allowBuild")]
    public bool AllowBuild { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether this pack can be installed.
    /// </summary>
    [JsonPropertyName("allowInstall")]
    public bool AllowInstall { get; set; } = false;

    /// <summary>
    /// Gets or sets the game language to set on installation.
    /// </summary>
    [JsonPropertyName("setGameLanguageOnInstall")]
    public string SetGameLanguageOnInstall { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the event callbacks for this bundle pack.
    /// </summary>
    [JsonPropertyName("events")]
    public Dictionary<BundleEventType, BundleEvent> Events { get; set; } = new();

    /// <summary>
    /// Gets the full name of this bundle pack including prefix and suffix.
    /// </summary>
    /// <returns>The full name of the bundle pack.</returns>
    public string GetFullName()
    {
        return $"{NamePrefix}{Name}{NameSuffix}";
    }
}
