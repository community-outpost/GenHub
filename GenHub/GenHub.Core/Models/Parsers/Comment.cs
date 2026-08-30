namespace GenHub.Core.Models.Parsers;

/// <summary>
/// Represents a page comment extracted from a web page.
/// </summary>
/// <param name="Author">The comment author (optional).</param>
/// <param name="Content">The comment content (optional).</param>
/// <param name="Date">The comment date (optional).</param>
/// <param name="Karma">The karma/vote score (optional).</param>
/// <param name="IsCreator">Whether the comment is from the content creator (optional).</param>
public record Comment(
    string? Author = null,
    string? Content = null,
    DateTime? Date = null,
    int? Karma = null,
    bool? IsCreator = null) : ContentSection(SectionType.Comment, "Comment");
