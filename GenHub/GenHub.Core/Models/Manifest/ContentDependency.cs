using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Enhanced dependency specification with advanced relationship management.
/// </summary>
public class ContentDependency
{
    /// <summary>Gets or sets the dependency ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the dependency name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the type of dependency content.</summary>
    public ContentType DependencyType { get; set; }

    /// <summary>Gets or sets the minimum version required.</summary>
    public string MinVersion { get; set; } = string.Empty;

    /// <summary>Gets or sets the maximum version allowed.</summary>
    public string MaxVersion { get; set; } = string.Empty;

    /// <summary>Gets or sets the list of compatible versions for this dependency.</summary>
    public List<string> CompatibleVersions { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether this dependency is exclusive (cannot coexist with others).</summary>
    public bool IsExclusive { get; set; } = false;

    /// <summary>Gets or sets the list of conflicting dependency IDs.</summary>
    public List<string> ConflictsWith { get; set; } = [];

    /// <summary>Gets or sets the installation behavior for this dependency.</summary>
    public DependencyInstallBehavior InstallBehavior { get; set; } = DependencyInstallBehavior.RequireExisting;
}
