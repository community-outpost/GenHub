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
}
