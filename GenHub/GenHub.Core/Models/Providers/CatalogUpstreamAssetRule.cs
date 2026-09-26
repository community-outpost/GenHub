using GenHub.Core.Models.Enums;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Defines an asset pattern rule for mapping upstream release artifacts to variant axes.
/// </summary>
public class CatalogUpstreamAssetRule
{
    /// <summary>
    /// Gets or sets the regex or glob pattern matching the asset filename.
    /// </summary>
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the variant label assigned to matching assets.
    /// </summary>
    [JsonPropertyName("variant")]
    public string Variant { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this variant is the default variant.
    /// </summary>
    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; }

    /// <summary>
    /// Gets or sets the target game for matching assets.
    /// </summary>
    [JsonPropertyName("targetGame")]
    public GameType TargetGame { get; set; } = GameType.ZeroHour;
}
