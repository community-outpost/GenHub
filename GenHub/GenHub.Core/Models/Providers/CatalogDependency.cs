using System.Text.Json.Serialization;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Represents a dependency on content from another publisher.
/// </summary>
public record CatalogDependency
{
    /// <summary>
    /// Gets the publisher ID of the dependency.
    /// </summary>
    [JsonPropertyName("publisherId")]
    public string PublisherId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the content ID within the publisher's catalog.
    /// </summary>
    [JsonPropertyName("contentId")]
    public string ContentId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the version constraint (e.g., ">=1.0.0", "^2.0", "1.5.0").
    /// </summary>
    [JsonPropertyName("versionConstraint")]
    public string? VersionConstraint { get; init; }

    /// <summary>
    /// Gets a value indicating whether the dependency is optional.
    /// </summary>
    [JsonPropertyName("isOptional")]
    public bool IsOptional { get; init; }

    /// <summary>
    /// Gets a hint for where to find this dependency (catalog URL).
    /// </summary>
    [JsonPropertyName("catalogUrl")]
    public string? CatalogUrl { get; init; }

    /// <summary>
    /// Gets the content type of the dependency.
    /// Defaults to Mod if not specified.
    /// </summary>
    [JsonPropertyName("contentType")]
    public ContentType ContentType { get; init; } = ContentType.Mod;
}
