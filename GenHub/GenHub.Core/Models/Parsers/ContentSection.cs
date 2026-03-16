namespace GenHub.Core.Models.Parsers;

/// <summary>
/// Base class for all content sections extracted from a web page.
/// </summary>
/// <param name="Type">The type of content section.</param>
/// <param name="Title">The title of the content section.</param>
public abstract record ContentSection(
    SectionType Type,
    string Title);
