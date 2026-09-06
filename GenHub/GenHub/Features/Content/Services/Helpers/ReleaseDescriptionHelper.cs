using System;
using System.Net;
using System.Text.RegularExpressions;
using GenHub.Core.Helpers;
using Markdig;

namespace GenHub.Features.Content.Services.Helpers;

/// <summary>
/// Converts raw GitHub release notes (markdown/HTML) into clean plain text suitable for
/// card and detail display. Preserves headings, bullet points, paragraphs, and line breaks
/// while stripping images and scaffolding.
/// </summary>
public static partial class ReleaseDescriptionHelper
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    /// <summary>
    /// Converts raw GitHub release notes (markdown/HTML) into clean, multi-line formatted plain text.
    /// Preserves paragraphs, bullet points, headers, and line breaks while stripping HTML tags and scaffolding.
    /// </summary>
    /// <param name="markdown">The raw release body (may be markdown, HTML, or plain text).</param>
    /// <returns>Clean multi-line formatted text, or an empty string when input is null or whitespace.</returns>
    public static string ToFormattedText(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        // Remove markdown images and reference-style link definitions first
        var text = MarkdownImageRegex().Replace(markdown.Trim(), string.Empty);
        text = ReferenceLinkRegex().Replace(text, string.Empty);

        // Convert markdown to HTML via Markdig to parse all markdown syntax structures
        var html = Markdig.Markdown.ToHtml(text, Pipeline);

        // 1. Remove script, style, and img tags
        html = ScriptTagRegex().Replace(html, string.Empty);
        html = StyleTagRegex().Replace(html, string.Empty);
        html = ImgTagRegex().Replace(html, string.Empty);

        // 2. Format anchors: <a href="url">text</a> -> text if text is non-empty, otherwise href
        html = AnchorTagRegex().Replace(html, match =>
        {
            var href = match.Groups["href"].Value.Trim();
            var linkText = match.Groups["text"].Value.Trim();
            return string.IsNullOrEmpty(linkText) ? href : linkText;
        });

        // 3. Convert list items to bullet points
        html = LiOpenTagRegex().Replace(html, "\n• ");
        html = LiCloseTagRegex().Replace(html, string.Empty);

        // 4. Convert headings to separated lines
        html = HeadingOpenTagRegex().Replace(html, "\n\n");
        html = HeadingCloseTagRegex().Replace(html, "\n");

        // 5. Convert line breaks and paragraph separation
        html = BrTagRegex().Replace(html, "\n");
        html = ParagraphOpenTagRegex().Replace(html, string.Empty);
        html = ParagraphCloseTagRegex().Replace(html, "\n\n");
        html = HrTagRegex().Replace(html, "\n---\n");
        html = BlockTagRegex().Replace(html, "\n");

        // 6. Strip all remaining HTML tags
        html = HtmlTagRegex().Replace(html, string.Empty);

        // 7. Decode HTML entities (&nbsp;, &gt;, &quot;, &#39;, etc.)
        html = WebUtility.HtmlDecode(html);

        // 8. Normalize non-breaking spaces and line endings
        html = html.Replace('\u00A0', ' ')
                   .Replace("\r\n", "\n")
                   .Replace('\r', '\n');

        // 9. Clean trailing whitespace on lines and collapse excess blank lines
        html = TrailingWhitespaceBeforeNewlineRegex().Replace(html, "\n");
        html = ExcessBlankLinesRegex().Replace(html, "\n\n");

        // 10. Trim and unify with environment newline
        html = html.Trim();
        html = html.Replace("\n", Environment.NewLine);

        return html;
    }

    /// <summary>
    /// Returns clean formatted text of the supplied markdown body.
    /// </summary>
    /// <param name="markdown">The raw release body (may be markdown, HTML, or plain text).</param>
    /// <returns>Clean text, or an empty string when the body collapses to nothing.</returns>
    public static string ToPlainText(string? markdown) => ToFormattedText(markdown);

    /// <summary>
    /// Produces a single-line summary suitable for a download card, collapsing the body
    /// to its first meaningful line and clamping it to <paramref name="maxLength"/>.
    /// </summary>
    /// <param name="markdown">The raw release body.</param>
    /// <param name="maxLength">Maximum character length of the summary.</param>
    /// <returns>A short summary string.</returns>
    public static string ToSummary(string? markdown, int maxLength = 150)
    {
        return HtmlTextHelper.CleanToSingleLine(ToFormattedText(markdown), maxLength);
    }

    [GeneratedRegex(@"<script\b[^>]*>[\s\S]*?</script>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptTagRegex();

    [GeneratedRegex(@"<style\b[^>]*>[\s\S]*?</style>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StyleTagRegex();

    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImgTagRegex();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]+\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownImageRegex();

    [GeneratedRegex(@"^\[[^\]]+\]:\s*\S+.*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceLinkRegex();

    [GeneratedRegex(@"<li\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LiOpenTagRegex();

    [GeneratedRegex(@"</li>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LiCloseTagRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BrTagRegex();

    [GeneratedRegex(@"</p\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ParagraphCloseTagRegex();

    [GeneratedRegex(@"<p\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ParagraphOpenTagRegex();

    [GeneratedRegex(@"<h[1-6]\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingOpenTagRegex();

    [GeneratedRegex(@"</h[1-6]>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingCloseTagRegex();

    [GeneratedRegex(@"<hr\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HrTagRegex();

    [GeneratedRegex(@"</?(?:div|tr|section|article|blockquote|header|footer|pre)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockTagRegex();

    [GeneratedRegex(@"<a\b[^>]*href=[""'](?<href>[^""']*)[""'][^>]*>(?<text>[\s\S]*?)</a>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnchorTagRegex();

    [GeneratedRegex(@"</?[A-Za-z][^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"[ \t]+\n", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingWhitespaceBeforeNewlineRegex();

    [GeneratedRegex(@"(?:\n\s*){3,}", RegexOptions.CultureInvariant)]
    private static partial Regex ExcessBlankLinesRegex();
}
