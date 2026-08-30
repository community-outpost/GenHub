using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>
    /// Parses a specific file or item detail page.
    /// Default implementation delegates to <see cref="ParseAsync(string, CancellationToken)"/>.
    /// </summary>
    /// <param name="url">The detail page URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A parsed web page containing the detailed file information.</returns>
    Task<ParsedWebPage> ParseFileDetailAsync(string url, CancellationToken cancellationToken = default)
        => ParseAsync(url, cancellationToken);

    /// <summary>
    /// Parses multiple file or item detail pages in a batch.
    /// Default implementation delegates to <see cref="ParseFileDetailAsync(string, CancellationToken)"/>.
    /// </summary>
    /// <param name="urls">The detail page URLs to parse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary mapping each URL to its parsed web page result.</returns>
    async Task<IReadOnlyDictionary<string, ParsedWebPage>> ParseFileDetailsManyAsync(
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(urls);
        var results = new Dictionary<string, ParsedWebPage>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in urls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var page = await ParseFileDetailAsync(url, cancellationToken);
                results[url] = page;
            }
            catch (HttpRequestException)
            {
                // soft failure per url in batch
            }
            catch (IOException)
            {
                // soft failure per url in batch
            }
            catch (InvalidOperationException)
            {
                // soft failure per url in batch
            }
            catch (FormatException)
            {
                // soft failure per url in batch
            }
        }

        return results;
    }
}
