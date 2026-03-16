using System.Text.Json.Serialization;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// A content item entry within a publisher catalog.
/// Represents a mod, map, addon, or other content with one or more releases.
/// </summary>
public class CatalogContentItem
{
    /// <summary>
    /// Gets or sets the unique content identifier within this publisher's catalog.
    /// Combined with publisher ID to form the full manifest ID.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable content name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the content description.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the content type (Mod, Map, Addon, etc.).
    /// </summary>
    [JsonPropertyName("contentType")]
    public ContentType ContentType { get; set; } = ContentType.Mod;

    /// <summary>
    /// Gets or sets the target game for this content.
    /// </summary>
    [JsonPropertyName("targetGame")]
    public GameType TargetGame { get; set; } = GameType.ZeroHour;

    /// <summary>
    /// Gets or sets the list of releases (versions) for this content.
    /// </summary>
    [JsonPropertyName("releases")]
    public List<ContentRelease> Releases { get; set; } = [];

    /// <summary>
    /// Gets or sets rich presentation metadata (banners, screenshots, videos).
    /// </summary>
    [JsonPropertyName("metadata")]
    public ContentRichMetadata? Metadata { get; set; }

    /// <summary>
    /// Gets or sets tags for categorization and search.
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];
}
