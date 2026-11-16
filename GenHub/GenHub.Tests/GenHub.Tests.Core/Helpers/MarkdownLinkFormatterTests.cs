using GenHub.Core.Helpers;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="MarkdownLinkFormatter"/>.
/// </summary>
public sealed class MarkdownLinkFormatterTests
{
    /// <summary>
    /// Verifies that null or whitespace inputs return an empty string.
    /// </summary>
    /// <param name="input">The input string to format.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FormatLinks_NullOrWhitespace_ReturnsEmptyString(string? input)
    {
        var result = MarkdownLinkFormatter.FormatLinks(input);
        Assert.Equal(string.Empty, result);
    }

    /// <summary>
    /// Verifies that HTML image tags become markdown images and paragraph wrappers are
    /// unwrapped so the viewer parses the image instead of treating it as raw HTML.
    /// </summary>
    [Fact]
    public void FormatLinks_HtmlImage_ConvertsToMarkdownImage()
    {
        var input = "<p><img src=\"https://example.test/shot.png\" width=\"1920\" alt=\"Menu\" /></p>";
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Equal("\n\n![Menu](https://example.test/shot.png)\n\n", result);
    }

    /// <summary>
    /// Verifies that a multiline paragraph-wrapped screenshot (the common GitHub README
    /// screenshots shape) unwraps to a standalone markdown image.
    /// </summary>
    [Fact]
    public void FormatLinks_PWrappedScreenshot_UnwrapsWrapperAndKeepsImage()
    {
        var input = "<p float=\"left\">\n  <img src=\"https://example.test/shot.png\" width=\"1920\" />\n\n</p>";
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Equal("\n\n\n  ![](https://example.test/shot.png)\n\n\n\n", result);
    }

    /// <summary>
    /// Verifies that division-wrapped images unwrap to a standalone markdown image.
    /// </summary>
    [Fact]
    public void FormatLinks_DivWrappedImage_UnwrapsWrapperAndKeepsImage()
    {
        var input = "<div align=\"center\"><img src=\"https://example.test/a.png\" alt=\"A\" /></div>";
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Equal("\n\n![A](https://example.test/a.png)\n\n", result);
    }

    /// <summary>
    /// Verifies that unwrapping a paragraph preserves inner text and inline markup.
    /// </summary>
    [Fact]
    public void FormatLinks_ParagraphText_PreservesInnerContent()
    {
        var input = "<p>Hello <b>world</b></p>";
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Equal("\n\nHello <b>world</b>\n\n", result);
    }

    /// <summary>
    /// Verifies that HTML images with relative sources degrade to their alt text.
    /// </summary>
    [Fact]
    public void FormatLinks_HtmlImageWithRelativeSource_FallsBackToAltText()
    {
        var input = "<img src=\"docs/shot.png\" alt=\"Menu\" />";
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Equal("Menu", result);
    }

    /// <summary>
    /// Verifies that whitespace in an HTML image source is percent-encoded so the emitted
    /// markdown destination stays a single matchable token.
    /// </summary>
    [Fact]
    public void FormatLinks_HtmlImage_WithWhitespaceInSource_EncodesDestination()
    {
        var input = "<img src=\"https://example.test/my image.png\" alt=\"Shot\" />";
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Equal("![Shot](https://example.test/my%20image.png)", result);
    }

    /// <summary>
    /// Verifies that an HTML image with a dangerous scheme and whitespace still sanitizes
    /// to its alt text once the destination is encoded into a matchable token.
    /// </summary>
    [Fact]
    public void FormatLinks_HtmlImage_DangerousSchemeWithWhitespace_FallsBackToAltText()
    {
        var input = "<img src=\"javascript:alert (1)\" alt=\"X\" />";
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Equal("X", result);
    }

    /// <summary>
    /// Verifies that bare URLs inside HTML tag attributes are left untouched.
    /// </summary>
    [Fact]
    public void FormatLinks_BareUrlInsideHtmlTag_PreservesTag()
    {
        var input = "<a href=\"https://example.test/docs\">docs</a> and https://example.test/plain";
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Equal("<a href=\"https://example.test/docs\">docs</a> and [https://example.test/plain](https://example.test/plain)", result);
    }

    /// <summary>
    /// Verifies that GitHub pull request URLs are converted into short [#109](url) links.
    /// </summary>
    [Fact]
    public void FormatLinks_GitHubPullUrl_FormatsToShortPrLink()
    {
        var input = "Added feature in https://github.com/community-outpost/GenHub/pull/109 by dev";
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Equal("Added feature in [#109](https://github.com/community-outpost/GenHub/pull/109) by dev", result);
    }

    /// <summary>
    /// Verifies that GitHub commit URLs are converted into short [`abcdef1`](url) links.
    /// </summary>
    [Fact]
    public void FormatLinks_GitHubCommitUrl_FormatsToShortShaLink()
    {
        var input = "Fixed bug in https://github.com/community-outpost/GenHub/commit/a1b2c3d4e5f6789012345678";
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Equal("Fixed bug in [`a1b2c3d`](https://github.com/community-outpost/GenHub/commit/a1b2c3d4e5f6789012345678)", result);
    }

    /// <summary>
    /// Verifies that parenthesized PR references like (#109) are converted to links when sourceUrl is provided.
    /// </summary>
    [Fact]
    public void FormatLinks_ParenthesizedIssue_WithSourceUrl_FormatsLink()
    {
        var input = "Fix map crash (#109)";
        var sourceUrl = "https://github.com/community-outpost/GenHub";
        var result = MarkdownLinkFormatter.FormatLinks(input, sourceUrl);

        Assert.Equal("Fix map crash ([#109](https://github.com/community-outpost/GenHub/pull/109))", result);
    }

    /// <summary>
    /// Verifies that standalone #109 references are converted to links when sourceUrl is provided.
    /// </summary>
    [Fact]
    public void FormatLinks_StandaloneIssue_WithSourceUrl_FormatsLink()
    {
        var input = "Closes #109 and #42.";
        var sourceUrl = "https://github.com/community-outpost/GenHub";
        var result = MarkdownLinkFormatter.FormatLinks(input, sourceUrl);

        Assert.Equal("Closes [#109](https://github.com/community-outpost/GenHub/pull/109) and [#42](https://github.com/community-outpost/GenHub/pull/42).", result);
    }

    /// <summary>
    /// Verifies that GitHub user mentions are converted to clickable profile links.
    /// </summary>
    [Fact]
    public void FormatLinks_GitHubMention_FormatsToUserLink()
    {
        var input = "Thanks to @Stubbjax and (@TheSuperHack)";
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Equal("Thanks to [@Stubbjax](https://github.com/Stubbjax) and ([@TheSuperHack](https://github.com/TheSuperHack))", result);
    }

    /// <summary>
    /// Verifies that bare URLs are wrapped in markdown link syntax while trailing punctuation is preserved.
    /// </summary>
    [Fact]
    public void FormatLinks_BareUrl_WrapsInMarkdownLink()
    {
        var input = "Visit https://generalsonline.net, or see https://example.com/info.";
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Equal("Visit [https://generalsonline.net](https://generalsonline.net), or see [https://example.com/info](https://example.com/info).", result);
    }

    /// <summary>
    /// Verifies that existing markdown links are not duplicated or double-wrapped.
    /// </summary>
    [Fact]
    public void FormatLinks_ExistingMarkdownLink_PreservesWithoutDoubleWrapping()
    {
        var input = "Check out [our website](https://generalsonline.net) for details.";
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Equal("Check out [our website](https://generalsonline.net) for details.", result);
    }

    /// <summary>
    /// Verifies that Unicode bullet points at line starts are normalized to Markdown list items.
    /// </summary>
    [Fact]
    public void FormatLinks_UnicodeBullets_ConvertsToMarkdownListItems()
    {
        var input = "Update 082826 (28th August 2026)\n\n• First change\n• Second change\n  • Nested change";
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Contains("- First change", result);
        Assert.Contains("- Second change", result);
        Assert.Contains("  - Nested change", result);
        Assert.DoesNotContain("•", result);
    }

    /// <summary>
    /// Verifies that an empty line is inserted before list items when immediately following a paragraph.
    /// </summary>
    [Fact]
    public void FormatLinks_ListImmediatelyFollowingParagraph_InsertsBlankLine()
    {
        var input = "Heading line\n• First change\n• Second change";
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Equal("Heading line\n\n- First change\n- Second change", result);
    }

    /// <summary>
    /// Verifies that inline bullet characters are preserved and not converted into list items.
    /// </summary>
    [Fact]
    public void FormatLinks_InlineBullets_PreservedAsIs()
    {
        var input = "Generals Online Team • generalsonline • 082826";
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Equal(input, result);
    }

    /// <summary>
    /// Verifies that markdown links with non-http(s) schemes (e.g. file://, UNC, javascript:)
    /// are stripped of link markup and rendered as plain text.
    /// </summary>
    /// <param name="input">The input markdown text containing links.</param>
    /// <param name="expected">The expected sanitized output text.</param>
    [Theory]
    [InlineData("[Fix the crash](file:///C:/Users/Public/payload.exe)", "Fix the crash")]
    [InlineData("[Read UNC](\\\\192.168.1.1\\share\\payload.bat)", "Read UNC")]
    [InlineData("[Exploit](javascript:alert(1))", "Exploit")]
    [InlineData("[App Data](data:text/html,payload)", "App Data")]
    public void FormatLinks_NonHttpLinks_SanitizedToPlainText(string input, string expected)
    {
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that markdown images with non-http(s) schemes (e.g. file://, avares://)
    /// are stripped of image markup and rendered as alt text.
    /// </summary>
    /// <param name="input">The input markdown text containing images.</param>
    /// <param name="expected">The expected sanitized output text.</param>
    [Theory]
    [InlineData("![Local file](file:///etc/passwd)", "Local file")]
    [InlineData("![App asset](avares://GenHub/Assets/logo.png)", "App asset")]
    public void FormatLinks_NonHttpImages_SanitizedToAltText(string input, string expected)
    {
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that valid HTTP and HTTPS markdown links and images are preserved.
    /// </summary>
    [Fact]
    public void FormatLinks_HttpAndHttpsLinksAndImages_Preserved()
    {
        var input = "[Valid Link](https://example.com) and ![Valid Image](https://example.com/pic.png)";
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Equal("[Valid Link](https://example.com) and ![Valid Image](https://example.com/pic.png)", result);
    }

    /// <summary>
    /// Verifies that existing markdown links whose destination is a GitHub PR or commit URL
    /// are not rewritten or corrupted.
    /// </summary>
    [Fact]
    public void FormatLinks_ExistingMarkdownLink_WithGitHubUrl_DoesNotCorruptLink()
    {
        var input = "Check out [release notes](https://github.com/community-outpost/GenHub/pull/109) and [commit](https://github.com/community-outpost/GenHub/commit/a1b2c3d4e5f6789012345678).";
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Equal(input, result);
    }

    /// <summary>
    /// Verifies that bare URLs containing balanced parentheses preserve their closing parenthesis.
    /// </summary>
    [Fact]
    public void FormatLinks_BareUrl_WithBalancedParentheses_PreservesClosingParen()
    {
        var input = "Download at https://github.com/owner/repo/releases/tag/v1.0_(hotfix) now.";
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Equal("Download at [https://github.com/owner/repo/releases/tag/v1.0_(hotfix)](https://github.com/owner/repo/releases/tag/v1.0_(hotfix)) now.", result);
    }

    /// <summary>
    /// Verifies that markdown links with leading whitespace inside parens or badge-style image links
    /// are properly sanitized against dangerous schemes.
    /// </summary>
    [Fact]
    public void FormatLinks_WhitespaceAndBadgeLinks_SanitizesDangerousSchemes()
    {
        var whitespaceLink = "[Click](   javascript:alert(1))";
        var sanitizedWhitespace = MarkdownLinkFormatter.FormatLinks(whitespaceLink);
        Assert.Equal("Click", sanitizedWhitespace);

        var badgeLink = "[![badge](https://example.com/badge.png)](javascript:alert(1))";
        var sanitizedBadge = MarkdownLinkFormatter.FormatLinks(badgeLink);
        Assert.Equal("![badge](https://example.com/badge.png)", sanitizedBadge);
    }

    /// <summary>
    /// Verifies that fenced code blocks are not corrupted by bullet list normalization.
    /// </summary>
    [Fact]
    public void FormatLinks_FencedCodeBlocks_PreservedUntouched()
    {
        var input = "Intro:\n```\n• inside code\n  • indented\n```\n• outside list";
        var result = MarkdownLinkFormatter.FormatLinks(input);

        Assert.Contains("```\n• inside code\n  • indented\n```", result);
        Assert.Contains("- outside list", result);
    }

    /// <summary>
    /// Verifies that PreserveLineBreaks appends two trailing spaces to regular lines while leaving
    /// headers, blockquotes, code fences, and bullet lists untouched.
    /// </summary>
    [Fact]
    public void PreserveLineBreaks_AppendsTwoTrailingSpacesToRegularLines()
    {
        var input = "Line 1\nLine 2\n# Header\n- Bullet 1\nLine 3";
        var result = MarkdownLinkFormatter.PreserveLineBreaks(input);

        Assert.Equal("Line 1  \nLine 2  \n# Header\n- Bullet 1\nLine 3  ", result);
    }

    /// <summary>
    /// Verifies that ordered lists are not padded with trailing spaces, but decimal numbers are.
    /// </summary>
    [Fact]
    public void PreserveLineBreaks_OrderedList_PreservedUntouched()
    {
        var input = "1. First\n2) Second\n3.5 million players";
        var result = MarkdownLinkFormatter.PreserveLineBreaks(input);

        Assert.Equal("1. First\n2) Second\n3.5 million players  ", result);
    }

    /// <summary>
    /// Verifies that block code fences toggle correctly while inline backticks do not.
    /// </summary>
    [Fact]
    public void PreserveLineBreaks_CodeFencesAndInlineBackticks_HandledCorrectly()
    {
        var input = "```csharp\nvar x = 1;\n```\n```inline``` text";
        var result = MarkdownLinkFormatter.PreserveLineBreaks(input);

        Assert.Equal("```csharp\nvar x = 1;\n```\n```inline``` text  ", result);
    }
}
