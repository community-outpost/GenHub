using System;
using GenHub.Core.Helpers;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="HtmlTextHelper"/>.
/// </summary>
public sealed class HtmlTextHelperTests
{
    /// <summary>
    /// Verifies that NormalizeHtml returns an empty string when input is null or whitespace.
    /// </summary>
    /// <param name="input">The test input string.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void NormalizeHtml_NullOrWhitespace_ReturnsEmptyString(string? input)
    {
        var result = HtmlTextHelper.NormalizeHtml(input);
        Assert.Equal(string.Empty, result);
    }

    /// <summary>
    /// Verifies that NormalizeHtml converts paragraph tags into paragraphs separated by newlines.
    /// </summary>
    [Fact]
    public void NormalizeHtml_ParagraphTags_ConvertsToParagraphsAndStripsTags()
    {
        var html = "<p>First paragraph.</p><p>Second paragraph.</p>";
        var result = HtmlTextHelper.NormalizeHtml(html);

        var expected = $"First paragraph.{Environment.NewLine}{Environment.NewLine}Second paragraph.";
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that NormalizeHtml converts break tags to line breaks.
    /// </summary>
    [Fact]
    public void NormalizeHtml_BreakTags_ConvertsToNewlines()
    {
        var html = "Line 1<br>Line 2<br/>Line 3<br />Line 4";
        var result = HtmlTextHelper.NormalizeHtml(html);

        var expected = $"Line 1{Environment.NewLine}Line 2{Environment.NewLine}Line 3{Environment.NewLine}Line 4";
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that NormalizeHtml strips inline HTML formatting tags.
    /// </summary>
    [Fact]
    public void NormalizeHtml_InlineTags_StripsTagsCleanly()
    {
        var html = "<b>Bold</b> <i>Italic</i> <a href=\"https://example.com\">Link</a> <span class=\"highlight\">Text</span>";
        var result = HtmlTextHelper.NormalizeHtml(html);

        Assert.Equal("Bold Italic Link Text", result);
    }

    /// <summary>
    /// Verifies that NormalizeHtml decodes HTML entities into appropriate characters.
    /// </summary>
    [Fact]
    public void NormalizeHtml_HtmlEntities_DecodesCorrectly()
    {
        var html = "&quot;Hello &amp; Welcome&#39;s &lt;World&gt;&quot; &nbsp; &#8211;";
        var result = HtmlTextHelper.NormalizeHtml(html);

        Assert.Equal("\"Hello & Welcome's <World>\"   –", result);
    }

    /// <summary>
    /// Verifies that NormalizeHtml strips paragraph tags from CNC Labs description snippets.
    /// </summary>
    [Fact]
    public void NormalizeHtml_CncLabsDescriptionWithPTags_ResolvesCleanly()
    {
        var html = "<p>The Ships and Boats War map is a game map that takes place almost</p>";
        var result = HtmlTextHelper.NormalizeHtml(html);

        Assert.Equal("The Ships and Boats War map is a game map that takes place almost", result);
    }

    /// <summary>
    /// Verifies that NormalizeHtml collapses runs of excess blank lines to a double newline.
    /// </summary>
    [Fact]
    public void NormalizeHtml_ExcessBlankLines_CollapsedToDoubleNewline()
    {
        var html = "First paragraph\n\n\n\n\nSecond paragraph";
        var result = HtmlTextHelper.NormalizeHtml(html);

        var expected = $"First paragraph{Environment.NewLine}{Environment.NewLine}Second paragraph";
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that CleanToSingleLine collapses multiple whitespace characters and newlines into a single space.
    /// </summary>
    [Fact]
    public void CleanToSingleLine_WithHtmlAndNewlines_CollapsesWhitespace()
    {
        var html = "<p>First line</p>\n\n<p>Second   line\twith   spaces</p>";
        var result = HtmlTextHelper.CleanToSingleLine(html);

        Assert.Equal("First line Second line with spaces", result);
    }

    /// <summary>
    /// Verifies that CleanToSingleLine truncates strings exceeding maximum length and appends an ellipsis.
    /// </summary>
    [Fact]
    public void CleanToSingleLine_WithMaxLength_TruncatesWithEllipsis()
    {
        var html = "<p>The Ships and Boats War map is a game map that takes place almost</p>";
        var result = HtmlTextHelper.CleanToSingleLine(html, 30);

        Assert.Equal(30, result.Length);
        Assert.EndsWith("...", result, StringComparison.Ordinal);
        Assert.Equal("The Ships and Boats War map...", result);
    }

    /// <summary>
    /// Verifies that TruncateWithEllipsis handles various length inputs and edge cases.
    /// </summary>
    /// <param name="input">The test input string.</param>
    /// <param name="maxLength">The maximum allowed length.</param>
    /// <param name="expected">The expected truncated output.</param>
    [Theory]
    [InlineData(null, 10, "")]
    [InlineData("", 10, "")]
    [InlineData("Short text", 20, "Short text")]
    [InlineData("ExactLengthText", 15, "ExactLengthText")]
    [InlineData("A very long string exceeding limit", 10, "A very ...")]
    [InlineData("Abcdef", 3, "Abc")]
    [InlineData("Abcdef", 2, "Ab")]
    public void TruncateWithEllipsis_VariousInputs_BehavesCorrectly(string? input, int maxLength, string expected)
    {
        var result = HtmlTextHelper.TruncateWithEllipsis(input, maxLength);
        Assert.Equal(expected, result);
    }
}
