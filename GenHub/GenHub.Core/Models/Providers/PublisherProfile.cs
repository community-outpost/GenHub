using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Publisher identity and branding information within a catalog.
/// </summary>
public class PublisherProfile
{
    /// <summary>
    /// Gets or sets the unique publisher identifier (e.g., "my-mods", "general-steve").
    /// Used in manifest ID generation and subscription matching.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable publisher name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the publisher's website URL.
    /// </summary>
    [JsonPropertyName("website")]
    public string? Website { get; set; }

    /// <summary>
    /// Gets or sets the publisher's avatar/logo URL.
    /// </summary>
    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Gets or sets the support URL (Discord, GitHub Issues, etc.).
    /// </summary>
    [JsonPropertyName("supportUrl")]
    public string? SupportUrl { get; set; }

    /// <summary>
    /// Gets or sets the publisher's contact email.
    /// </summary>
    [JsonPropertyName("contactEmail")]
    public string? ContactEmail { get; set; }
}
