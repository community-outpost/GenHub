namespace GenHub.Core.Models.Parsers;

/// <summary>
/// Represents a fully parsed web page with all extracted content sections.
/// This is the root container for all parsed data from a web page.
/// </summary>
/// <param name="Url">The URL of the page that was parsed.</param>
/// <param name="Context">The global context information (title, developer, etc.).</param>
/// <param name="Sections">List of all content sections extracted from the page.</param>
/// <param name="PageType">The detected type of the page.</param>
public record ParsedWebPage(
    string Url,
    GlobalContext Context,
    List<ContentSection> Sections,
    PageType PageType);
