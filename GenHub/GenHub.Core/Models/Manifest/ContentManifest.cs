using System.Collections.Generic;
using System.Text.Json.Serialization;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Comprehensive manifest for content distribution in GenHub ecosystem.
/// This is the central contract between content publishers and the GenHub launcher.
/// </summary>
public class ContentManifest
{
    private List<ArtifactVariant> _variants = [];

    /// <summary>Gets or sets the manifest format/schema version.</summary>
    [JsonPropertyName("ManifestVersion")]
    public string SchemaVersion { get; set; } = ManifestConstants.DefaultManifestVersion;

    /// <summary>Gets or sets the unique identifier for this content package.</summary>
    public ManifestId Id { get; set; }

    /// <summary>Gets or sets the human-readable name for the content.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the version of this content package.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Gets or sets the type of content this manifest describes.</summary>
    public ContentType ContentType { get; set; }

    /// <summary>Gets or sets the target game for this content.</summary>
    public GameType TargetGame { get; set; }

    /// <summary>Gets or sets the publisher information.</summary>
    public PublisherInfo Publisher { get; set; } = new();

    /// <summary>Gets or sets the content metadata and descriptions.</summary>
    public ContentMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Gets or sets the name of the provider that originally supplied this manifest.
    /// Used for cache invalidation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OriginalProviderName { get; set; }

    /// <summary>
    /// Gets or sets the ID of the content from the original provider.
    /// Used for cache invalidation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OriginalContentId { get; set; }

    /// <summary>Gets or sets the original source path for local content.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourcePath { get; set; }

    /// <summary>Gets or sets the dependencies required for this content to function.</summary>
    public List<ContentDependency> Dependencies { get; set; } = [];

    /// <summary>Gets or sets content references for cross-publisher linking.</summary>
    public List<ContentReference> ContentReferences { get; set; } = [];

    /// <summary>Gets or sets the list of known addons for this game (manifest-driven, not hardcoded).</summary>
    public List<string> KnownAddons { get; set; } = [];

    /// <summary>
    /// Gets or sets all files included in this content package.
    /// <para>
    /// This describes the single, unconstrained build. When <see cref="Variants"/> is
    /// non-empty this list is ignored in favour of the matching variant. Consumers
    /// should resolve through <c>ManifestVariantResolver</c> rather than reading this
    /// directly, so that multi-platform manifests behave correctly.
    /// </para>
    /// </summary>
    public List<ManifestFile> Files { get; set; } = [];

    /// <summary>
    /// Gets or sets platform-specific builds of this content.
    /// <para>
    /// Optional and empty by default, so every manifest written before variants existed
    /// keeps working unchanged: an empty list means "<see cref="Files"/> is the only
    /// build". Populate it when one release ships several platform builds that share a
    /// version but differ in file list or entry point.
    /// </para>
    /// </summary>
    public List<ArtifactVariant> Variants
    {
        get => _variants;
        set => _variants = value ?? [];
    }

    /// <summary>
    /// Gets or sets the relative path of the file to launch, for single-variant content.
    /// <para>
    /// Declared rather than inferred from file extensions. Without it, resolution falls
    /// back to guessing from the file list, which is ambiguous as soon as more than one
    /// file qualifies and then depends on enumeration order.
    /// </para>
    /// <para>
    /// When <see cref="Variants"/> is populated, each variant carries its own entry
    /// point and this is ignored.
    /// </para>
    /// </summary>
    public string? EntryPoint { get; set; }

    /// <summary>Gets or sets the required directory structure.</summary>
    public List<string> RequiredDirectories { get; set; } = [];

    /// <summary>Gets or sets the installation instructions and hooks.</summary>
    public InstallationInstructions InstallationInstructions { get; set; } = new();
}
