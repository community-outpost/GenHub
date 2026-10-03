using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FluentAssertions;
using GenHub.Infrastructure.Controls;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Tests.Core.Infrastructure.Controls;

/// <summary>
/// Headless tests for <see cref="MarkdownTextBlock"/> markdown normalization.
/// </summary>
public sealed class MarkdownTextBlockTests
{
    /// <summary>
    /// Verifies that tilde-fenced code blocks are recognized during dedent so the fence survives normalization.
    /// </summary>
    [AvaloniaFact]
    public void TildeFencedBlock_RendersCodeBlock()
    {
        var control = new MarkdownTextBlock { Markdown = "    Intro\n    ~~~\ncode();\n    ~~~\n" };
        var window = new Window { Content = control };
        try
        {
            window.Show();

            control.Content.Should().BeOfType<StackPanel>();
            DescendantScrollViewers(control).Should().ContainSingle();
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that a tab-indented line counts as visual columns instead of pinning the common indent to one character.
    /// </summary>
    [AvaloniaFact]
    public void TabIndentedLine_DedentsByVisualColumns()
    {
        var control = new MarkdownTextBlock { Markdown = "\tIntro\n```\n  keep();\n```\n" };
        var window = new Window { Content = control };
        try
        {
            window.Show();

            var viewer = DescendantScrollViewers(control).Should().ContainSingle().Subject;
            var text = (viewer.Content as Border)?.Child as TextBlock;

            text.Should().NotBeNull();
            var code = text!.Text ?? string.Empty;
            code.Should().Contain("keep();");
            code.Should().NotContain(" keep();");
        }
        finally
        {
            window.Close();
        }
    }

    private static IEnumerable<ScrollViewer> DescendantScrollViewers(Control root)
    {
        var stack = new Stack<Control>(new[] { root });
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is ScrollViewer viewer)
            {
                yield return viewer;
            }

            foreach (var child in current.GetVisualChildren().OfType<Control>())
            {
                stack.Push(child);
            }
        }
    }
}
