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
}
