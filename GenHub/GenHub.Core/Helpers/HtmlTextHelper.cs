using System;
using System.Net;
using System.Text.RegularExpressions;

namespace GenHub.Core.Helpers;

/// <summary>
/// Provides high-performance utilities for stripping HTML tags, decoding HTML entities,
/// and normalizing text descriptions for display across the application.
/// </summary>
public static partial class HtmlTextHelper
{
    /// <summary>
    /// Converts an HTML snippet or formatted description into clean, normalized plain text:
    /// - Replaces &lt;br&gt; and block element closures (&lt;/p&gt;, &lt;/div&gt;, etc.) with line breaks.
    /// - Strips all remaining HTML tags.
    /// - Decodes HTML entities (e.g., &amp;amp;, &amp;quot;, &amp;gt;, &amp;nbsp;).
    /// - Normalizes whitespace and excessive blank lines.
    /// - Uses the platform newline format.
    /// </summary>
    /// <param name="html">The raw HTML or formatted text string to normalize.</param>
    /// <returns>Normalized plain text, or empty string if input is null or whitespace.</returns>
    public static string NormalizeHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        // 0. Remove script and style elements along with their contents
        var text = ScriptTagRegex().Replace(html, string.Empty);
        text = StyleTagRegex().Replace(text, string.Empty);

        // 1. Convert <br> tags to newline
        text = BrTagRegex().Replace(text, "\n");

        // 2. Convert paragraph closing tags to double newline for paragraph separation
        text = ParagraphCloseTagRegex().Replace(text, "\n\n");

        // 3. Convert other block-level closing tags and <hr> tags to newline
        text = BlockCloseTagRegex().Replace(text, "\n");

        // 4. Strip all remaining HTML/XML tags
        text = HtmlTagRegex().Replace(text, string.Empty);

        // 5. Decode HTML entities (&nbsp;, &gt;, &quot;, &#39;, numeric entities, etc.)
        text = WebUtility.HtmlDecode(text);

        // 6. Normalize non-breaking spaces and line endings
        text = text.Replace('\u00A0', ' ')
                   .Replace("\r\n", "\n")
                   .Replace('\r', '\n');

        // 7. Clean trailing whitespace on lines and collapse excess blank lines
        text = TrailingWhitespaceBeforeNewlineRegex().Replace(text, "\n");
        text = ExcessBlankLinesRegex().Replace(text, "\n\n");

        // 8. Trim and unify with environment newline
        text = text.Trim();
        text = text.Replace("\n", Environment.NewLine);

        return text;
    }

    /// <summary>
    /// Converts an HTML snippet or multi-line text into a single-line summary without HTML tags,
    /// collapsing all whitespace runs into a single space, and optionally truncating with an ellipsis.
    /// </summary>
    /// <param name="htmlOrText">The input HTML or text string.</param>
    /// <param name="maxLength">Optional maximum character length including ellipsis.</param>
    /// <returns>A single-line plain text summary.</returns>
    public static string CleanToSingleLine(string? htmlOrText, int? maxLength = null)
    {
        if (string.IsNullOrWhiteSpace(htmlOrText))
        {
            return string.Empty;
        }

        // Strip HTML if tags exist, decode entities, and normalize
        var text = NormalizeHtml(htmlOrText);

        // Collapse all newlines, tabs, and multiple spaces into a single space
        text = MultiWhitespaceRegex().Replace(text, " ").Trim();

        if (maxLength.HasValue && maxLength.Value > 0 && text.Length > maxLength.Value)
        {
            return TruncateWithEllipsis(text, maxLength.Value);
        }

        return text;
    }

    /// <summary>
    /// Truncates a string to a specified maximum length and appends an ellipsis ("...") if truncated.
    /// </summary>
    /// <param name="text">The text to truncate.</param>
    /// <param name="maxLength">The maximum allowed length (including the ellipsis).</param>
    /// <returns>The truncated text with an ellipsis if it exceeded maxLength, or the original text.</returns>
    public static string TruncateWithEllipsis(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || maxLength <= 0)
        {
            return string.Empty;
        }

        if (text.Length <= maxLength)
        {
            return text;
        }

        if (maxLength <= 3)
        {
            return text[..maxLength];
        }

        return string.Concat(text.AsSpan(0, maxLength - 3), "...");
    }

    [GeneratedRegex(@"<script\b[^>]*>[\s\S]*?</script>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptTagRegex();

    [GeneratedRegex(@"<style\b[^>]*>[\s\S]*?</style>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StyleTagRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BrTagRegex();

    [GeneratedRegex(@"</p\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ParagraphCloseTagRegex();

    [GeneratedRegex(@"</?(?:div|li|h[1-6]|tr|section|article|blockquote|header|footer|hr)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockCloseTagRegex();

    [GeneratedRegex(@"</?[A-Za-z][^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"[ \t]+\n", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingWhitespaceBeforeNewlineRegex();

    [GeneratedRegex(@"(?:\n){3,}", RegexOptions.CultureInvariant)]
    private static partial Regex ExcessBlankLinesRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex MultiWhitespaceRegex();
}
