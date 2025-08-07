using System;
using System.Collections.Generic;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameVersions;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Comprehensive manifest for content distribution in GenHub ecosystem.
/// This is the central contract between content publishers and the GenHub launcher.
/// </summary>
public class ContentManifest
{
    /// <summary>
    /// Gets or sets the manifest format version for backward compatibility.
    /// </summary>
    public string ManifestVersion { get; set; } = "1.0";

    /// <summary>
    /// Gets or sets the unique identifier for this content package.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable name for the content.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version of this content package.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of content this manifest describes.
    /// </summary>
    public ContentType ContentType { get; set; }

    /// <summary>
    /// Gets or sets the target game for this content.
    /// </summary>
    public GameType TargetGame { get; set; }

    /// <summary>
    /// Gets or sets the publisher information.
    /// </summary>
    public PublisherInfo Publisher { get; set; } = new();

    /// <summary>
    /// Gets or sets the content metadata and descriptions.
    /// </summary>
    public ContentMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Gets or sets the dependencies required for this content to function.
    /// </summary>
    public List<ContentDependency> Dependencies { get; set; } = [];

    /// <summary>
    /// Gets or sets content references for cross-publisher linking.
    /// </summary>
    public List<ContentReference> ContentReferences { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of known addons for this game (manifest-driven, not hardcoded).
    /// </summary>
    public List<string> KnownAddons { get; set; } = new();

    /// <summary>
    /// Gets or sets all files included in this content package.
    /// </summary>
    public List<ManifestFile> Files { get; set; } = [];

    /// <summary>
    /// Gets or sets the required directory structure.
    /// </summary>
    public List<string> RequiredDirectories { get; set; } = [];

    /// <summary>
    /// Gets or sets the installation instructions and hooks.
    /// </summary>
    public InstallationInstructions Installation { get; set; } = new();
}
