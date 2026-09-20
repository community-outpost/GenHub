using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Rich presentation metadata for content display in the UI.
/// </summary>
public class ContentRichMetadata
{
    /// <summary>
    /// Gets or sets the banner image URL for content detail pages.
    /// </summary>
    [JsonPropertyName("bannerUrl")]
    public string? BannerUrl { get; set; }

    /// <summary>
    /// Gets or sets the per-content icon URL shown on cards and detail headers.
    /// Falls back to the publisher avatar when omitted.
    /// </summary>
    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; set; }

    /// <summary>
    /// Gets or sets the wide backdrop/cover image URL for content detail headers.
    /// Falls back to the banner image when omitted.
    /// </summary>
    [JsonPropertyName("backdropUrl")]
    public string? BackdropUrl { get; set; }

    /// <summary>
    /// Gets or sets the accent color for this content as a hex string (e.g. "#7C3AED").
    /// Used for card stripes and detail highlights. Ignored when invalid or omitted.
    /// </summary>
    [JsonPropertyName("accentColor")]
    public string? AccentColor { get; set; }

    /// <summary>
    /// Gets or sets a collection of screenshot URLs.
    /// </summary>
    [JsonPropertyName("screenshotUrls")]
    public List<string> ScreenshotUrls { get; set; } = [];

    /// <summary>
    /// Gets or sets a video URL (YouTube, Vimeo, direct MP4).
    /// </summary>
    [JsonPropertyName("videoUrl")]
    public string? VideoUrl { get; set; }

    /// <summary>
    /// Gets or sets a documentation or wiki URL.
    /// </summary>
    [JsonPropertyName("documentationUrl")]
    public string? DocumentationUrl { get; set; }

    /// <summary>
    /// Gets or sets the author display name (if different from publisher).
    /// </summary>
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    /// <summary>
    /// Gets or sets the license type (MIT, GPL, etc.).
    /// </summary>
    [JsonPropertyName("license")]
    public string? License { get; set; }

    /// <summary>
    /// Gets or sets an optional category label shown on download cards and usable by filters.
    /// </summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>
    /// Gets or sets an optional player-count value shown on download cards.
    /// </summary>
    [JsonPropertyName("playerCount")]
    public int? PlayerCount { get; set; }
}
