using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using ColorTextBlock.Avalonia;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Features.GameProfiles.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using MarkdownEngine = Markdown.Avalonia.Markdown;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Tests for <see cref="MissingDependencyDialog"/>.
/// </summary>
public sealed class MissingDependencyDialogTests
{
    private const char WordJoiner = '⁠';

    /// <summary>
    /// A manifest-supplied name renders as plain text in the dialog body: no link, image,
    /// heading, list, quote, HTML or text decoration, and no stray backslash.
    /// </summary>
    /// <param name="name">The manifest-supplied name.</param>
    /// <param name="expected">The text the player must see.</param>
    [AvaloniaTheory]
    [InlineData("[Free Coins](http://example.invalid)", "[Free Coins](http://example.invalid)")]
    [InlineData("![Logo](http://example.invalid/logo.png)", "![Logo](http://example.invalid/logo.png)")]
    [InlineData("<a href=\"http://example.invalid\">Free</a>", "<a href=\"http://example.invalid\">Free</a>")]
    [InlineData("<img src=\"http://example.invalid/logo.png\">", "<img src=\"http://example.invalid/logo.png\">")]
    [InlineData("# Free Points", "# Free Points")]
    [InlineData("1. Step", "1. Step")]
    [InlineData("b. Setup", "b. Setup")]
    [InlineData("> Quoted", "> Quoted")]
    [InlineData("- Dash", "- Dash")]
    [InlineData("**Bold** _it_ ~~gone~~", "**Bold** _it_ ~~gone~~")]
    [InlineData("%{color:red}Red% :smile:", "%{color:red}Red% :smile:")]
    [InlineData("Back\\slash", "Back\\slash")]
    [InlineData("Line one\n# Line two", "Line one # Line two")]
    public void BuildContent_NameWithMarkdown_RendersAsPlainText(string name, string expected)
    {
        var gap = CreateGap(name, name);

        var rendered = new MarkdownEngine { Plugins = new Markdown.Avalonia.Full.MdAvPlugins() }.Transform(MissingDependencyDialog.BuildContent(null, "Content", [gap]));

        var blocks = Assert.IsAssignableFrom<Panel>(rendered).Children;
        var intro = Assert.IsType<CTextBlock>(blocks[0]);
        Assert.DoesNotContain(intro.Classes, cls => cls.StartsWith("Heading", StringComparison.Ordinal));
        Assert.StartsWith(expected, GetText(intro), StringComparison.Ordinal);
        Assert.Contains(expected, string.Concat(GetTextBlocks(blocks[1]).Select(GetText)), StringComparison.Ordinal);
        Assert.DoesNotContain(GetInlines(rendered), inline => inline is CHyperlink or CImage or CItalic or CUnderline or CStrikethrough);
        var itemName = Assert.Single(GetInlines(rendered).OfType<CBold>());
        Assert.All(Flatten(itemName.Content), inline => Assert.IsType<CRun>(inline));
        Assert.Equal(expected, Strip(string.Concat(Flatten(itemName.Content).OfType<CRun>().Select(run => run.Text))));
    }

    /// <summary>
    /// One item required twice with different version limits is counted and listed once, with every requirer.
    /// </summary>
    [Fact]
    public void BuildContent_SameItemTwice_ListsItOnce()
    {
        var first = CreateGap("GenTool", "Control Bar Pro");
        var second = first with { RequiredByName = "Legionnaire's Hotkeys", RequiredById = "1.0.examplepublisher.addon.hotkeys" };
        var other = CreateGap("Widget", "Control Bar Pro") with { DependencyId = "1.0.examplepublisher.addon.widget" };
        IReadOnlyList<ContentDependencyGap> gaps = [first, second, other];

        var title = MissingDependencyDialog.BuildTitle(null, gaps);
        var content = Strip(MissingDependencyDialog.BuildContent(null, "Content", gaps));
        var names = MissingDependencyDialog.GetDisplayNames(null, gaps);

        Assert.Equal("2 required items are missing", title);
        Assert.Equal(1, CountOccurrences(content, "GenTool"));
        Assert.Contains("(needed by Control Bar Pro, Legionnaire's Hotkeys)", content, StringComparison.Ordinal);
        Assert.Equal(["GenTool", "Widget"], names);
        Assert.Equal("Content was not added. It still needs GenTool, Widget.", MissingDependencyDialog.BuildStillMissingMessage(null, "Content", gaps));
    }

    /// <summary>
    /// Items with no declared name stay separate even though they share the generic name.
    /// </summary>
    [Fact]
    public void BuildTitle_UnnamedItems_CountsEach()
    {
        var first = CreateGap(string.Empty, "Big Mod");
        var second = first with { DependencyId = "1.0.examplepublisher.addon.other" };

        Assert.Equal("2 required items are missing", MissingDependencyDialog.BuildTitle(null, [first, second]));
    }

    private static ContentDependencyGap CreateGap(string displayName, string requiredByName) => new()
    {
        DisplayName = displayName,
        DependencyId = "1.0.examplepublisher.addon.gent",
        DependencyType = ContentType.Addon,
        CanDownload = true,
        RequiredByName = requiredByName,
        RequiredById = "1.0.examplepublisher.mod.bigmod",
    };

    private static string Strip(string text) => text.Replace(WordJoiner.ToString(), string.Empty, StringComparison.Ordinal);

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static IEnumerable<CTextBlock> GetTextBlocks(Control control)
    {
        if (control is CTextBlock textBlock)
        {
            yield return textBlock;
            yield break;
        }

        foreach (var child in ((ILogical)control).LogicalChildren.OfType<Control>())
        {
            foreach (var descendant in GetTextBlocks(child))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<CInline> GetInlines(Control control) =>
        GetTextBlocks(control).SelectMany(textBlock => Flatten(textBlock.Content));

    private static IEnumerable<CInline> Flatten(IEnumerable<CInline> inlines)
    {
        foreach (var inline in inlines)
        {
            yield return inline;
            if (inline is CSpan span)
            {
                foreach (var child in Flatten(span.Content))
                {
                    yield return child;
                }
            }
        }
    }

    private static string GetText(CTextBlock textBlock)
    {
        var builder = new StringBuilder();
        foreach (var inline in Flatten(textBlock.Content).OfType<CRun>())
        {
            builder.Append(inline.Text);
        }

        return Strip(builder.ToString());
    }
}
