using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AvaloniaEdit;
using GenHub.Infrastructure.Markdown;
using Markdown.Avalonia;
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
        var viewer = new MarkdownScrollViewer { Width = 900, Height = 1200 };
        viewer.Plugins = new MdAvPlugins
        {
            HyperlinkCommand = new SafeMarkdownHyperlinkCommand(),
            PathResolver = new SafeMarkdownPathResolver(),
        };
        viewer.Markdown = markdown;
        var window = new Window { Content = viewer, Width = 900, Height = 1200 };

        // Act
        window.Show();
        window.UpdateLayout();
        await Task.Delay(500);
        var rendered = ExtractRenderedText(viewer);
        window.Close();

        // Assert
        Assert.Contains("HeadingMarker", rendered);
        Assert.Contains("InlineCodeMarker", rendered);
        Assert.Contains("ListMarkerOne", rendered);
        Assert.Contains("FenceBodyMarker", rendered);
        Assert.Contains("LinkMarker", rendered);
    }

    private static string ExtractRenderedText(MarkdownScrollViewer viewer)
    {
        var sb = new StringBuilder();
        foreach (var visual in viewer.GetVisualDescendants())
        {
            var type = visual.GetType();
            if (type.Name == "CTextBlock")
            {
                if (type.GetProperty("Content")?.GetValue(visual) is System.Collections.IEnumerable items)
                {
                    foreach (var item in items)
                    {
                        if (item != null)
                        {
                            AppendContentItemText(item, sb);
                        }
                    }
                }
            }

            if (visual is TextBlock textBlock)
            {
                sb.Append(textBlock.Text);
            }

            if (visual is TextEditor editor)
            {
                sb.Append(editor.Document?.Text);
            }
        }

        return sb.ToString();
    }

    private static void AppendContentItemText(object item, StringBuilder sb)
    {
        var type = item.GetType();
        sb.Append(type.GetProperty("Text")?.GetValue(item) as string);

        foreach (var prop in type.GetProperties().Where(p => p.Name is "Content" or "Inlines"))
        {
            if (prop.GetValue(item) is System.Collections.IEnumerable children)
            {
                foreach (var child in children)
                {
                    if (child != null)
                    {
                        AppendContentItemText(child, sb);
                    }
                }
            }
        }
    }
}
