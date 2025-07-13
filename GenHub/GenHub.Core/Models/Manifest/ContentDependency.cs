using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Dependency specification for content requirements.
/// </summary>
public class ContentDependency
{
    /// <summary>
    /// Gets or sets the dependency ID.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the dependency name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the minimum version required.
    /// </summary>
    public string MinVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maximum version allowed.
    /// </summary>
    public string MaxVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this dependency is required.
    /// </summary>
    public bool IsRequired { get; set; } = true;

    /// <summary>
    /// Gets or sets the type of dependency.
    /// </summary>
    public ContentType DependencyType { get; set; }
}
