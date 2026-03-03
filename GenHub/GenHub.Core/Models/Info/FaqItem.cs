namespace GenHub.Core.Models.Info;

/// <summary>
/// Represents a single FAQ question and answer.
/// </summary>
/// <param name="Id">The unique identifier for the item (e.g., anchor name).</param>
/// <param name="Question">The question text.</param>
/// <param name="Answer">The answer text/HTML.</param>
/// <param name="AnchorLink">The anchor link for navigation.</param>
public record FaqItem(
    string Id,
    string Question,
    string Answer,
    string? AnchorLink);
