using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using GenHub.Infrastructure.Markdown;
using Markdown.Avalonia;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Markdown;

/// <summary>
/// Headless render tests for the shared markdown viewer configuration.
/// Guards against Markdown.Avalonia/Avalonia incompatibilities that blank
/// rich release notes and READMEs (for example, inline code spans).
/// </summary>
public sealed class MarkdownScrollViewerRenderTests
{
    private static readonly string[] ChildPropertyNames = ["Content", "Inlines", "Children", "Document"];

    /// <summary>
    /// Verifies that GitHub-style markdown with inline code, fenced blocks,
    /// lists, and links attaches and renders its text.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task Render_GitHubStyleMarkdown_RendersInlineCodeAndFencedBlocksAsync()
    {
        // Arrange
        const string markdown = "## HeadingMarker\n\nParagraph with `InlineCodeMarker` and **bold** text.\n\n- ListMarkerOne\n- ListMarkerTwo\n\n```bash\nFenceBodyMarker\n```\n\n[LinkMarker](https://example.com/docs)\n";
        string[] markers = ["HeadingMarker", "InlineCodeMarker", "ListMarkerOne", "FenceBodyMarker", "LinkMarker"];
        var viewer = CreateViewer();
        viewer.Markdown = markdown;
        var window = new Window { Content = viewer, Width = 900, Height = 1200 };

        // Act
        window.Show();
        window.UpdateLayout();
        var rendered = await WaitForRenderedTextAsync(viewer, markers, TimeSpan.FromSeconds(15));
        window.Close();

        // Assert
        foreach (var marker in markers)
        {
            Assert.Contains(marker, rendered);
        }
    }

    private static MarkdownScrollViewer CreateViewer()
    {
        var viewer = new MarkdownScrollViewer { Width = 900, Height = 1200 };
        viewer.Plugins = new MdAvPlugins
        {
            HyperlinkCommand = new SafeMarkdownHyperlinkCommand(),
            PathResolver = new SafeMarkdownPathResolver(),
        };
        return viewer;
    }

    private static async Task<string> WaitForRenderedTextAsync(MarkdownScrollViewer viewer, string[] markers, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var rendered = string.Empty;
        while (DateTime.UtcNow < deadline)
        {
            rendered = ExtractRenderedText(viewer);
            if (markers.All(rendered.Contains))
            {
                return rendered;
            }

            await Task.Delay(50);
        }

        return rendered;
    }

    private static string ExtractRenderedText(MarkdownScrollViewer viewer)
    {
        var sb = new StringBuilder();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var visual in viewer.GetVisualDescendants())
        {
            AppendNodeText(visual, sb, visited);
        }

        return sb.ToString();
    }

    private static void AppendNodeText(object node, StringBuilder sb, HashSet<object> visited)
    {
        if (!visited.Add(node))
        {
            return;
        }

        var type = node.GetType();
        if (type.GetProperty("Text")?.GetValue(node) is string text)
        {
            sb.Append(text);
        }

        foreach (var name in ChildPropertyNames)
        {
            var value = type.GetProperty(name)?.GetValue(node);
            if (value is string || value is Visual)
            {
                continue;
            }

            if (value is IEnumerable items)
            {
                foreach (var child in items)
                {
                    if (child != null)
                    {
                        AppendNodeText(child, sb, visited);
                    }
                }
            }
            else if (value != null)
            {
                AppendNodeText(value, sb, visited);
            }
        }
    }
}
