using GenHub.Core.Models.Parsers;

namespace GenHub.Core.Interfaces.Parsers;

/// <summary>
/// Universal interface for parsing web pages and extracting rich content.
/// Designed to be provider-agnostic and reusable across different content sources.
/// </summary>
public interface IWebPageParser
{
    /// <summary>
    /// Gets the unique identifier for this parser implementation.
    /// </summary>
    string ParserId { get; }

    /// <summary>
    /// Determines if this parser can handle the given URL.
    /// </summary>
    /// <param name="url">The URL to check.</param>
    /// <returns>True if this parser can handle the URL; otherwise, false.</returns>
    bool CanParse(string url);

    /// <summary>
    /// Parses a web page and extracts all available content.
    /// </summary>
    /// <param name="url">The URL to parse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A parsed web page with all extracted content sections.</returns>
    Task<ParsedWebPage> ParseAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses a web page from pre-fetched HTML content.
    /// </summary>
    /// <param name="url">The source URL.</param>
    /// <param name="html">The HTML content to parse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A parsed web page with all extracted content sections.</returns>
    Task<ParsedWebPage> ParseAsync(string url, string html, CancellationToken cancellationToken = default);
}
