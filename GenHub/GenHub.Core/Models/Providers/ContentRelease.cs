using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Represents a specific version/release of a content item.
/// </summary>
public class ContentRelease
{
    /// <summary>
    /// Gets or sets the semantic version string (e.g., "1.0.0", "2.1.0-beta").
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the release date.
    /// </summary>
    [JsonPropertyName("releaseDate")]
    public DateTime ReleaseDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets a value indicating whether this is a prerelease version.
    /// Prereleases are hidden by default unless user opts in.
    /// </summary>
    [JsonPropertyName("isPrerelease")]
    public bool IsPrerelease { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is the latest stable release.
    /// Used for "Latest Only" version filtering.
    /// </summary>
    [JsonPropertyName("isLatest")]
    public bool IsLatest { get; set; }

    /// <summary>
    /// Gets or sets the changelog/release notes.
    /// Supports markdown formatting.
    /// </summary>
    [JsonPropertyName("changelog")]
    public string? Changelog { get; set; }

    /// <summary>
    /// Gets or sets the downloadable artifacts for this release.
    /// </summary>
    [JsonPropertyName("artifacts")]
    public List<ReleaseArtifact> Artifacts { get; set; } = [];

    /// <summary>
    /// Gets or sets dependencies required by this release.
    /// </summary>
    [JsonPropertyName("dependencies")]
    public List<CatalogDependency> Dependencies { get; set; } = [];
}
