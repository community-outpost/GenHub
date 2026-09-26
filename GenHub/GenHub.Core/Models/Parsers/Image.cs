namespace GenHub.Core.Models.Parsers;

/// <summary>
/// Represents a gallery image extracted from a web page.
/// </summary>
/// <param name="Title">The image title or caption.</param>
/// <param name="ThumbnailUrl">URL to the thumbnail image (optional).</param>
/// <param name="FullSizeUrl">URL to the full-size image (optional).</param>
/// <param name="Description">Image description (optional).</param>
public record Image(
    string Title,
    string? ThumbnailUrl = null,
    string? FullSizeUrl = null,
    string? Description = null) : ContentSection(SectionType.Image, Title);
