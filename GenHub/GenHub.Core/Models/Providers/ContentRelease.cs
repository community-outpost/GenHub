using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// A specific version release of a content item.
/// Contains downloadable artifacts and version-specific metadata.
/// </summary>
public class ContentRelease
{
    /// <summary>
    /// Gets or sets an optional display title or name for this release or addon (e.g., "HD Texture Pack", "Maps Expansion").
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets an optional category or addon type (e.g., "Map", "Patch", "UI", "AI", "Music", "Skin", "Addon").
    /// </summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>
    /// Gets or sets the version string (e.g., "1.0.0", "2.1-beta").
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the release date.
    /// </summary>
    [JsonPropertyName("releaseDate")]
    public DateTime? ReleaseDate { get; set; }

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
    /// Gets or sets a value indicating whether this release should be featured/highlighted in the UI.
    /// </summary>
    [JsonPropertyName("isFeatured")]
    public bool IsFeatured { get; set; }

    /// <summary>
    /// Gets or sets the changelog/release notes.
    /// Supports markdown formatting.
    /// </summary>
    [JsonPropertyName("changelog")]
    public string? Changelog { get; set; }

    /// <summary>
    /// Gets or sets the optional entry point path for this release.
    /// </summary>
    [JsonPropertyName("entryPoint")]
    public string? EntryPoint { get; set; }

    /// <summary>
    /// Gets or sets the downloadable artifacts for this release.
    /// </summary>
    [JsonPropertyName("artifacts")]
    public List<ReleaseArtifact> Artifacts { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether all downloadable artifacts in this
    /// release are installed together as one package. When true, catalog discovery
    /// does not split multi-file releases into per-file variant siblings; every
    /// artifact becomes a manifest file of the same installed content.
    /// </summary>
    [JsonPropertyName("bundleArtifacts")]
    public bool BundleArtifacts { get; set; }

    /// <summary>
    /// Gets or sets media image URLs (screenshots, promotional artwork) specific to this release.
    /// </summary>
    [JsonPropertyName("imageUrls")]
    public List<string> ImageUrls { get; set; } = [];

    /// <summary>
    /// Gets or sets video URLs (trailers, preview videos) specific to this release.
    /// </summary>
    [JsonPropertyName("videoUrls")]
    public List<string> VideoUrls { get; set; } = [];

    /// <summary>
    /// Gets or sets dependencies required by this release.
    /// </summary>
    [JsonPropertyName("dependencies")]
    public List<CatalogDependency> Dependencies { get; set; } = [];
}
