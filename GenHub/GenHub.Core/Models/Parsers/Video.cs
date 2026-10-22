namespace GenHub.Core.Models.Parsers;

/// <summary>
/// Represents an embedded video extracted from a web page.
/// </summary>
/// <param name="Title">The video title.</param>
/// <param name="ThumbnailUrl">URL to the video thumbnail (optional).</param>
/// <param name="EmbedUrl">The embed URL for the video (optional).</param>
/// <param name="Platform">The video platform (e.g., YouTube, Vimeo) (optional).</param>
public record Video(
    string Title,
    string? ThumbnailUrl = null,
    string? EmbedUrl = null,
    string? Platform = null) : ContentSection(SectionType.Video, Title);
