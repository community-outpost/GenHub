using System;
using System.Collections.Generic;
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
    /// Gets or sets optional publisher-provided display names for screenshots,
    /// keyed by the exact URL or path stored in <see cref="ScreenshotUrls"/>.
    /// Screenshots without an entry fall back to a generic numbered name.
    /// </summary>
    [JsonPropertyName("screenshotTitles")]
    public Dictionary<string, string> ScreenshotTitles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the publisher-provided display name for a screenshot, if one was set.
    /// </summary>
    /// <param name="screenshotUrl">The URL or path stored in <see cref="ScreenshotUrls"/>.</param>
    /// <returns>The display name, or null when the screenshot is unnamed.</returns>
    public string? GetScreenshotTitle(string? screenshotUrl)
    {
        if (string.IsNullOrWhiteSpace(screenshotUrl) || ScreenshotTitles == null)
        {
            return null;
        }

        foreach (var entry in ScreenshotTitles)
        {
            if (string.Equals(entry.Key, screenshotUrl, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(entry.Value))
            {
                return entry.Value.Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Sets or clears the publisher-provided display name for a screenshot.
    /// A blank name removes the entry so the screenshot uses its generic name.
    /// </summary>
    /// <param name="screenshotUrl">The URL or path stored in <see cref="ScreenshotUrls"/>.</param>
    /// <param name="title">The display name to store, or blank to clear it.</param>
    public void SetScreenshotTitle(string? screenshotUrl, string? title)
    {
        if (string.IsNullOrWhiteSpace(screenshotUrl))
        {
            return;
        }

        RemoveScreenshotTitle(screenshotUrl);
        if (!string.IsNullOrWhiteSpace(title))
        {
            ScreenshotTitles ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ScreenshotTitles[screenshotUrl] = title.Trim();
        }
    }

    /// <summary>
    /// Removes any publisher-provided display name for a screenshot.
    /// </summary>
    /// <param name="screenshotUrl">The URL or path stored in <see cref="ScreenshotUrls"/>.</param>
    public void RemoveScreenshotTitle(string? screenshotUrl)
    {
        if (string.IsNullOrWhiteSpace(screenshotUrl) || ScreenshotTitles == null)
        {
            return;
        }

        string? existingKey = null;
        foreach (var key in ScreenshotTitles.Keys)
        {
            if (string.Equals(key, screenshotUrl, StringComparison.OrdinalIgnoreCase))
            {
                existingKey = key;
                break;
            }
        }

        if (existingKey != null)
        {
            ScreenshotTitles.Remove(existingKey);
        }
    }

    /// <summary>
    /// Moves a screenshot display name to a new URL or path, for example when a
    /// local file is replaced by its hosted URL during publishing.
    /// </summary>
    /// <param name="oldUrl">The previous URL or path.</param>
    /// <param name="newUrl">The replacement URL or path.</param>
    public void RemapScreenshotTitle(string? oldUrl, string? newUrl)
    {
        var title = GetScreenshotTitle(oldUrl);
        if (title == null || string.IsNullOrWhiteSpace(newUrl))
        {
            return;
        }

        RemoveScreenshotTitle(oldUrl);
        ScreenshotTitles[newUrl] = title;
    }

    /// <summary>
    /// Gets or sets a video URL (YouTube, Vimeo, direct MP4).
    /// </summary>
    [JsonPropertyName("videoUrl")]
    public string? VideoUrl { get; set; }

    /// <summary>
    /// Gets or sets a collection of video URLs (trailers, gameplay showcases).
    /// </summary>
    [JsonPropertyName("videoUrls")]
    public List<string> VideoUrls { get; set; } = [];

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

    /// <summary>
    /// Gets or sets a value indicating whether this content is featured.
    /// </summary>
    [JsonPropertyName("isFeatured")]
    public bool IsFeatured { get; set; }

    /// <summary>
    /// Gets or sets a custom badge label shown on featured cards.
    /// </summary>
    [JsonPropertyName("featuredBadge")]
    public string? FeaturedBadge { get; set; }
}
