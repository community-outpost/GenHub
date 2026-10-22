namespace GenHub.Core.Models.Parsers;

/// <summary>
/// Represents a downloadable file extracted from a web page.
/// </summary>
/// <param name="Name">The file name.</param>
/// <param name="Version">The file version (optional).</param>
/// <param name="SizeBytes">File size in bytes (optional).</param>
/// <param name="SizeDisplay">Human-readable file size (optional).</param>
/// <param name="UploadDate">The upload date (optional).</param>
/// <param name="Category">The file category (optional).</param>
/// <param name="Uploader">The uploader name (optional).</param>
/// <param name="DownloadUrl">The download URL (optional).</param>
/// <param name="Md5Hash">The MD5 hash of the file (optional).</param>
/// <param name="CommentCount">Number of comments (optional).</param>
/// <param name="ThumbnailUrl">The thumbnail image URL (optional).</param>
/// <param name="DownloadCount">Number of downloads (optional).</param>
public record File(
    string Name,
    string? Version = null,
    long? SizeBytes = null,
    string? SizeDisplay = null,
    DateTime? UploadDate = null,
    string? Category = null,
    string? Uploader = null,
    string? DownloadUrl = null,
    string? Md5Hash = null,
    int? CommentCount = null,
    string? ThumbnailUrl = null,
    int? DownloadCount = null) : ContentSection(SectionType.File, Name);
