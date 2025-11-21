using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Enhanced dependency specification with advanced relationship management.
/// </summary>
public class ContentDependency
{
    /// <summary>Gets or sets the dependency ID.</summary>
    public ManifestId Id { get; set; } = ManifestId.Create(ManifestConstants.DefaultContentDependencyId);

    /// <summary>Gets or sets the dependency name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the type of dependency content.</summary>
    public ContentType DependencyType { get; set; }

    /// <summary>
    /// Gets or sets the publisher type for this dependency.
    /// This is dynamically determined from the IContentProvider.SourceName that supplied the content.
    /// For installation sources, see <see cref="InstallationSourceConstants"/>.
    /// This allows expressing dependencies like "requires Steam version of Zero Hour" vs "any Zero Hour".
    /// </summary>
    public string? PublisherType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a strict publisher requirement.
    /// When true, only content from the specified publisher type will satisfy this dependency.
    /// When false, any publisher can satisfy if other constraints (version, etc.) match.
    /// Example: GeneralsOnline client requires Zero Hour but doesn't care if it's Steam or EA.
    /// </summary>
    public bool StrictPublisher { get; set; } = false;

    /// <summary>Gets or sets the minimum version required.</summary>
    public string? MinVersion { get; set; }

    /// <summary>Gets or sets the maximum version allowed.</summary>
    public string? MaxVersion { get; set; }

    /// <summary>
    /// Gets or sets the exact version required (optional, overrides min/max if specified).
    /// Example: "1.04" for Zero Hour, "1.08" for Generals.
    /// </summary>
    public string? ExactVersion { get; set; }

    /// <summary>Gets or sets the list of compatible versions for this dependency.</summary>
    public List<string> CompatibleVersions { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of compatible game types for this dependency.
    /// When specified, only installations of these game types will satisfy the dependency.
    /// Example: A GeneralsOnline client might only be compatible with ZeroHour.
    /// </summary>
    public List<GameType> CompatibleGameTypes { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether this dependency is exclusive (cannot coexist with others).</summary>
    public bool IsExclusive { get; set; } = false;

    /// <summary>Gets or sets the list of conflicting dependency IDs.</summary>
    public List<ManifestId> ConflictsWith { get; set; } = [];

    /// <summary>Gets or sets the installation behavior for this dependency.</summary>
    public DependencyInstallBehavior InstallBehavior { get; set; } = DependencyInstallBehavior.RequireExisting;

    /// <summary>
    /// Gets or sets a value indicating whether this dependency is optional.
    /// Optional dependencies enhance functionality but aren't required for basic operation.
    /// Example: A mod might optionally depend on ControlBar for better UX.
    /// </summary>
    public bool IsOptional { get; set; } = false;

    /// <summary>
    /// Gets or sets the fallback dependency ID if this dependency cannot be satisfied.
    /// Enables graceful degradation when preferred dependencies are unavailable.
    /// </summary>
    public ManifestId? FallbackDependency { get; set; }
}