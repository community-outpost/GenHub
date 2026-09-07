using System;
using GenHub.Core.Models.Providers;

namespace GenHub.Core.Models.Publishers;

/// <summary>
/// Represents a Publisher Studio project containing a catalog and metadata.
/// </summary>
public class PublisherStudioProject
{
    /// <summary>
    /// Gets or sets the project name.
    /// </summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file path where this project is saved.
    /// </summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the publisher catalog.
    /// </summary>
    public PublisherCatalog Catalog { get; set; } = new();

    /// <summary>
    /// Gets or sets the collection of named catalogs for multi-catalog support.
    /// </summary>
    public List<NamedCatalog> Catalogs { get; set; } = [];

    /// <summary>
    /// Gets or sets the last modified timestamp.
    /// </summary>
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets a value indicating whether the project has unsaved changes.
    /// </summary>
    public bool IsDirty { get; set; }

    /// <summary>
    /// Gets or sets the catalog file name.
    /// </summary>
    public string CatalogFileName { get; set; } = "catalog.json";

    /// <summary>
    /// Gets or sets the provider definition file name.
    /// </summary>
    public string ProviderDefinitionFileName { get; set; } = "publisher.json";

    /// <summary>
    /// Gets or sets the list of tags for the publisher.
    /// </summary>
    public List<string> Tags { get; set; } = [];
}
