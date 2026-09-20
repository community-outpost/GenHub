using GenHub.Core.Constants;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Defines a content manifest as a named grouping of bundle packs.
/// Each definition produces one <see cref="Manifest.ContentManifest"/> whose
/// content is the union of its linked packs' built files.
/// </summary>
public class BundleManifest
{
    /// <summary>
    /// Gets or sets the unique display name of this manifest.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the manifest version string (for example "1.0.0").
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = ModBuilderConstants.DefaultManifestVersion;

    /// <summary>
    /// Gets or sets the publisher identifier. Empty selects the default local publisher.
    /// </summary>
    [JsonPropertyName("publisher")]
    public string Publisher { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional manifest description.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the names of the bundle packs linked into this manifest.
    /// </summary>
    [JsonPropertyName("packs")]
    public List<string> PackNames { get; set; } = new();
}
