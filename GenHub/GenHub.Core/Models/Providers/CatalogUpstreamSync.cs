using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Upstream synchronization configuration for autonomous releases.
/// </summary>
public class CatalogUpstreamSync
{
    /// <summary>
    /// Gets or sets the upstream provider identifier (e.g. "GitHubReleases", "GeneralsOnline", "CommunityOutpost").
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the upstream repository (e.g. "TheSuperHackers/GeneralsGameCode").
    /// </summary>
    [JsonPropertyName("repository")]
    public string? Repository { get; set; }

    /// <summary>
    /// Gets or sets the upstream release channel (e.g. "stable", "release", "beta").
    /// </summary>
    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    /// <summary>
    /// Gets or sets the variant axis (e.g. "game-type", "resolution").
    /// </summary>
    [JsonPropertyName("variantAxis")]
    public string? VariantAxis { get; set; }

    /// <summary>
    /// Gets or sets the asset rules for filtering and variant mapping.
    /// </summary>
    [JsonPropertyName("assetRules")]
    public List<CatalogUpstreamAssetRule> AssetRules { get; set; } = [];
}
