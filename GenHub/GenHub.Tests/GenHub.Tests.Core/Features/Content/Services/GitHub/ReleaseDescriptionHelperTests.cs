using System;
using GenHub.Features.Content.Services.Helpers;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services.GitHub;

/// <summary>
/// Tests markdown/HTML formatting for GitHub release bodies destined for card and detail descriptions.
/// </summary>
public class ReleaseDescriptionHelperTests
{
    /// <summary>
    /// Verifies that HTML paragraph tags are stripped while preserving text.
    /// </summary>
    [Fact]
    public void ToPlainText_StripsHtmlParagraphTags()
    {
        var result = ReleaseDescriptionHelper.ToPlainText("<p>Weekly build of the game client.</p>");

        Assert.Equal("Weekly build of the game client.", result);
    }

    /// <summary>
    /// Verifies that ATX headers are cleanly formatted.
    /// </summary>
    [Fact]
    public void ToPlainText_StripsAtxHeaders()
    {
        var result = ReleaseDescriptionHelper.ToPlainText("#### Changes\nFixed a crash.");

        Assert.DoesNotContain("#", result);
        Assert.Contains("Changes", result);
        Assert.Contains("Fixed a crash.", result);
    }

    /// <summary>
    /// Verifies that image tags and markdown images are stripped.
    /// </summary>
    [Fact]
    public void ToPlainText_StripsImages()
    {
        var result = ReleaseDescriptionHelper.ToPlainText("![alt](https://example.com/x.png) Done");

        Assert.DoesNotContain("![", result);
        Assert.DoesNotContain("example.com", result);
        Assert.Contains("Done", result);
    }

    /// <summary>
    /// Verifies that common HTML entities are decoded.
    /// </summary>
    [Fact]
    public void ToPlainText_DecodesCommonEntities()
    {
        var result = ReleaseDescriptionHelper.ToPlainText("Tom &amp; Jerry &lt;3");

        Assert.Equal("Tom & Jerry <3", result);
    }

    /// <summary>
    /// Verifies that excessive blank lines are normalized while preserving paragraphs.
    /// </summary>
    [Fact]
    public void ToPlainText_NormalizesWhitespace()
    {
        var result = ReleaseDescriptionHelper.ToPlainText("Line one.\n\n\nLine two.\tTabbed.");

        Assert.Equal($"Line one.{Environment.NewLine}{Environment.NewLine}Line two.\tTabbed.", result);
    }

    /// <summary>
    /// Verifies that bullet lists in markdown are preserved with line breaks and bullets.
    /// </summary>
    [Fact]
    public void ToFormattedText_PreservesBulletListsAndLineBreaks()
    {
        var markdown = "## What's Changed\n* bugfix: Fix audio issue by @Stubbjax in #109\n* bugfix: Fix crash by @Stubbjax in #113\n\nFull Changelog: https://github.com/repo/commits/1.0.0";
        var result = ReleaseDescriptionHelper.ToFormattedText(markdown);

        Assert.Contains("What's Changed", result);
        Assert.Contains("• bugfix: Fix audio issue by @Stubbjax in #109", result);
        Assert.Contains("• bugfix: Fix crash by @Stubbjax in #113", result);
        Assert.Contains("Full Changelog: https://github.com/repo/commits/1.0.0", result);
        Assert.Contains(Environment.NewLine, result);
    }

    /// <summary>
    /// Verifies that empty string is returned for null or whitespace input.
    /// </summary>
    [Fact]
    public void ToPlainText_ReturnsEmptyForNullOrWhitespace()
    {
        Assert.Equal(string.Empty, ReleaseDescriptionHelper.ToPlainText(null));
        Assert.Equal(string.Empty, ReleaseDescriptionHelper.ToPlainText("   "));
    }

    /// <summary>
    /// Verifies that long text is clamped to max length with ellipsis in summary.
    /// </summary>
    [Fact]
    public void ToSummary_ClampsToMaxLengthWithEllipsis()
    {
        var longText = new string('a', 200);
        var result = ReleaseDescriptionHelper.ToSummary(longText, maxLength: 20);

        Assert.True(result.Length <= 20);
        Assert.EndsWith("...", result);
    }

    /// <summary>
    /// Verifies that full text is returned when under the length limit in summary.
    /// </summary>
    [Fact]
    public void ToSummary_ReturnsFullTextWhenUnderLimit()
    {
        var result = ReleaseDescriptionHelper.ToSummary("Short text", maxLength: 150);

        Assert.Equal("Short text", result);
    }

    /// <summary>
    /// Verifies that multi-line text is collapsed to a single line in summary.
    /// </summary>
    [Fact]
    public void ToSummary_CollapsesMultiLineToSingleLine()
    {
        var result = ReleaseDescriptionHelper.ToSummary("Line one.\n\n\nLine two.\tTabbed.");

        Assert.Equal("Line one. Line two. Tabbed.", result);
    }
}
