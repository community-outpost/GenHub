using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using GenHub.Core.Helpers;
using GenHub.Infrastructure.Markdown;
using Markdown.Avalonia;
using Markdown.Avalonia.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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

    /// <summary>
    /// Verifies that a paragraph-wrapped README screenshot renders as an image inline
    /// instead of literal markdown when using the full plugins configuration from production views.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task Render_PWrappedScreenshot_RendersImageInlineAsync()
    {
        // Arrange
        const string readme = "## Screenshots\n<p float=\"left\">\n  <img src=\"https://example.test/shot.png\" width=\"1920\" />\n\n</p>\n";
        var viewer = CreateFullViewer();
        viewer.Markdown = MarkdownLinkFormatter.FormatLinks(readme);
        var window = new Window { Content = viewer, Width = 900, Height = 1200 };

        // Act
        window.Show();
        window.UpdateLayout();
        var rendered = await WaitForRenderedTextAsync(viewer, ["Screenshots", "$$Image$$"], TimeSpan.FromSeconds(15));
        window.Close();

        // Assert
        Assert.Contains("$$Image$$", rendered);
        Assert.DoesNotContain("![](", rendered);
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

    private static MarkdownScrollViewer CreateFullViewer()
    {
        var viewer = new MarkdownScrollViewer { Width = 900, Height = 1200 };
        viewer.Plugins = new global::Markdown.Avalonia.Full.MdAvPlugins
        {
            HyperlinkCommand = new SafeMarkdownHyperlinkCommand(),
            PathResolver = new StubPathResolver(),
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

    private sealed class StubPathResolver : IPathResolver
    {
        private static readonly byte[] PngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

        public string? AssetPathRoot { get; set; }

        public IEnumerable<string>? CallerAssemblyNames { get; set; }

        public Task<Stream?>? ResolveImageResource(string relativeOrAbsolutePath)
        {
            return Task.FromResult<Stream?>(new MemoryStream(PngBytes));
        }
    }
}
