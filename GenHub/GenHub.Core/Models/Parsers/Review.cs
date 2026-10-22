namespace GenHub.Core.Models.Parsers;

/// <summary>
/// Represents a user review extracted from a web page.
/// </summary>
/// <param name="Author">The review author (optional).</param>
/// <param name="Rating">The rating score (optional).</param>
/// <param name="Content">The review content (optional).</param>
/// <param name="Date">The review date (optional).</param>
/// <param name="HelpfulVotes">Number of helpful votes (optional).</param>
public record Review(
    string? Author = null,
    float? Rating = null,
    string? Content = null,
    DateTime? Date = null,
    int? HelpfulVotes = null) : ContentSection(SectionType.Review, "Review");
