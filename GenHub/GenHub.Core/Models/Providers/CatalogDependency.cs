using System.Collections.Generic;
using System.Text.Json.Serialization;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// A dependency declared by a content release.
/// Can reference content within the same catalog or from an external publisher.
/// </summary>
public class CatalogDependency
{
    /// <summary>
    /// Gets or sets the publisher ID that provides this dependency.
    /// When null or empty, refers to content within the same publisher.
    /// </summary>
    [JsonPropertyName("publisherId")]
    public string? PublisherId { get; set; }

    /// <summary>
    /// Gets or sets the content ID of the required item.
    /// </summary>
    [JsonPropertyName("contentId")]
    public string ContentId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version constraint (e.g., ">= 1.0.0", "^2.0.0").
    /// Null means any version is acceptable.
    /// </summary>
    [JsonPropertyName("versionConstraint")]
    public string? VersionConstraint { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the dependency is optional.
    /// </summary>
    [JsonPropertyName("isOptional")]
    public bool IsOptional { get; set; }

    /// <summary>
    /// Gets or sets the content type of the dependency (e.g., "GameInstallation", "Mod").
    /// When omitted, the resolver infers the type: a dependency declared by a GameClient
    /// on its base game is treated as a <see cref="GenHub.Core.Models.Enums.ContentType.GameInstallation"/>.
    /// </summary>
    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    /// <summary>
    /// Gets or sets a hint for where to find this dependency (catalog URL).
    /// </summary>
    [JsonPropertyName("catalogUrl")]
    public string? CatalogUrl { get; set; }

    /// <summary>
    /// Gets or sets the dependency type (Required, Recommended, Bundled).
    /// </summary>
    [JsonPropertyName("dependencyType")]
    public DependencyType DependencyType { get; set; } = DependencyType.Required;

    /// <summary>
    /// Gets or sets the URL to the provider definition for this dependency.
    /// Recommended over CatalogUrl for robust discovery.
    /// </summary>
    [JsonPropertyName("definitionUrl")]
    public string? DefinitionUrl { get; set; }

    /// <summary>
    /// Gets or sets a list of manifest IDs that conflict with this dependency.
    /// </summary>
    [JsonPropertyName("conflictsWith")]
    public List<string> ConflictsWith { get; set; } = [];
}
