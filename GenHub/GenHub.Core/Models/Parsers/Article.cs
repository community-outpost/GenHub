namespace GenHub.Core.Models.Parsers;

/// <summary>
/// Represents a news article extracted from a web page.
/// </summary>
/// <param name="Title">The article title.</param>
/// <param name="Author">The article author (optional).</param>
/// <param name="PublishDate">The publication date (optional).</param>
/// <param name="Content">The article content/body (optional).</param>
/// <param name="Url">The URL to the full article (optional).</param>
public record Article(
    string Title,
    string? Author = null,
    DateTime? PublishDate = null,
    string? Content = null,
    string? Url = null) : ContentSection(SectionType.Article, Title);
