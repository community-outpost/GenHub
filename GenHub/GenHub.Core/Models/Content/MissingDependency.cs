using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results.Content;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Represents a dependency that is not installed and needs resolution.
/// </summary>
public class MissingDependency
{
    /// <summary>
    /// Gets or sets the dependency from the manifest.
    /// </summary>
    public ContentDependency Dependency { get; set; } = new();

    /// <summary>
    /// Gets or sets the catalog dependency information if available.
    /// </summary>
    public CatalogDependency? CatalogDependency { get; set; }

    /// <summary>
    /// Gets or sets the content search result that can satisfy this dependency.
    /// </summary>
    public ContentSearchResult? ResolvableContent { get; set; }

    /// <summary>
    /// Gets a value indicating whether this dependency can be automatically installed.
    /// </summary>
    public bool CanAutoInstall => ResolvableContent != null;

    /// <summary>
    /// Gets or sets the catalog URL where this dependency can be found.
    /// </summary>
    public string? CatalogUrl { get; set; }

    /// <summary>
    /// Gets or sets the publisher ID that provides this dependency.
    /// </summary>
    public string? PublisherId { get; set; }
}
