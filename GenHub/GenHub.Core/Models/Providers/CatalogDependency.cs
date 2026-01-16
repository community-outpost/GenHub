using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Represents a dependency on content from another publisher.
/// </summary>
public class CatalogDependency
{
    /// <summary>
    /// Gets or sets the publisher ID of the dependency.
    /// </summary>
    [JsonPropertyName("publisherId")]
    public string PublisherId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the content ID within the publisher's catalog.
    /// </summary>
    [JsonPropertyName("contentId")]
    public string ContentId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version constraint (e.g., ">=1.0.0", "^2.0", "1.5.0").
    /// </summary>
    [JsonPropertyName("versionConstraint")]
    public string? VersionConstraint { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the dependency is optional.
    /// </summary>
    [JsonPropertyName("isOptional")]
    public bool IsOptional { get; set; }

    /// <summary>
    /// Gets or sets a hint for where to find this dependency (catalog URL).
    /// </summary>
    [JsonPropertyName("catalogUrl")]
    public string? CatalogUrl { get; set; }

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
