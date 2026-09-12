using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Parsers;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Represents detailed information about a content item parsed from a provider's detail page.
/// </summary>
/// <param name="Name">Content name.</param>
/// <param name="Description">Full description.</param>
/// <param name="Author">Author/creator name.</param>
/// <param name="PreviewImage">Main preview image URL.</param>
/// <param name="Screenshots">List of screenshot URLs.</param>
/// <param name="FileSize">File size in bytes.</param>
/// <param name="DownloadCount">Number of downloads.</param>
/// <param name="SubmissionDate">Date submitted/released.</param>
/// <param name="DownloadUrl">Direct download URL.</param>
/// <param name="TargetGame">Target game type.</param>
/// <param name="ContentType">Mapped content type.</param>
/// <param name="FileType">File extension/type (optional).</param>
/// <param name="Rating">Content rating (optional).</param>
/// <param name="RefererUrl">Referrer URL for tracking source (optional).</param>
/// <param name="AdditionalFiles">Additional files associated with the content (optional).</param>
public record ParsedContentDetails(
    string Name,
    string Description,
    string Author,
    string PreviewImage,
    List<string>? Screenshots,
    long FileSize,
    int DownloadCount,
    DateTime SubmissionDate,
    string DownloadUrl,
    GameType TargetGame,
    ContentType ContentType,
    string? FileType = null,
    float? Rating = null,
    string? RefererUrl = null,
    List<GenHub.Core.Models.Parsers.File>? AdditionalFiles = null);
