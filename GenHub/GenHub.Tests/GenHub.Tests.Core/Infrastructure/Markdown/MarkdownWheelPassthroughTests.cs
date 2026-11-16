using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using GenHub.Infrastructure.Markdown;
using Markdown.Avalonia;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Markdown;

/// <summary>
/// Headless tests for <see cref="MarkdownWheelPassthrough"/>.
/// </summary>
public sealed class MarkdownWheelPassthroughTests
{
    private const string RichMarkdown = "# Title\n\n```csharp\nvar x = 1;\n```\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\nClosing paragraph.";

    /// <summary>
    /// Replacing the Markdown after the viewer is shown keeps every inner scroller from scrolling vertically,
    /// so a live preview re-render does not take the wheel back.
    /// </summary>
    [AvaloniaFact]
    public void MarkdownChangedAfterAttach_KeepsInnerScrollingDisabled()
    {
        var viewer = new MarkdownScrollViewer { Markdown = "Intro" };
        MarkdownWheelPassthrough.SetIsEnabled(viewer, true);
        var window = new Window { Content = viewer, Width = 800, Height = 600 };
        window.Show();
        try
        {
            window.UpdateLayout();
            var before = InnerScrollViewers(viewer);

            viewer.Markdown = RichMarkdown;
            window.UpdateLayout();

            var after = InnerScrollViewers(viewer);
            Assert.True(after.Count > before.Count, "The rich Markdown should add inner scrollers to the viewer.");
            Assert.All(after, inner => Assert.Equal(ScrollBarVisibility.Disabled, inner.VerticalScrollBarVisibility));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Turning the passthrough off gives the inner scrollers back their own vertical scrolling.
    /// </summary>
    [AvaloniaFact]
    public void IsEnabledSetFalse_RestoresInnerScrolling()
    {
        var plain = new MarkdownScrollViewer { Markdown = RichMarkdown };
        var viewer = new MarkdownScrollViewer { Markdown = RichMarkdown };
        MarkdownWheelPassthrough.SetIsEnabled(viewer, true);
        var panel = new StackPanel { Children = { plain, viewer } };
        var window = new Window { Content = panel, Width = 800, Height = 600 };
        window.Show();
        try
        {
            window.UpdateLayout();
            var expected = InnerScrollViewers(plain).Select(s => s.VerticalScrollBarVisibility).ToList();
            Assert.Contains(expected, v => v != ScrollBarVisibility.Disabled);

            MarkdownWheelPassthrough.SetIsEnabled(viewer, false);

            Assert.Equal(expected, InnerScrollViewers(viewer).Select(s => s.VerticalScrollBarVisibility).ToList());
        }
        finally
        {
            window.Close();
        }
    }

    private static List<ScrollViewer> InnerScrollViewers(MarkdownScrollViewer viewer) =>
        [.. viewer.GetVisualDescendants().OfType<ScrollViewer>()];
}
