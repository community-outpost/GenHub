using Avalonia.Controls;
using Avalonia.VisualTree;
using GenHub.Common.Editors;
using System.Linq;

namespace GenHub.Tests.Core.Features.Tools;

/// <summary>
/// Helpers for locating the shared editor canvas host in headless view tests.
/// </summary>
internal static class EditorCanvasTestHelper
{
    /// <summary>
    /// Finds the template scroll viewer inside the named shared canvas control.
    /// </summary>
    /// <param name="view">The editor view hosting the canvas.</param>
    /// <param name="canvasName">The canvas control name.</param>
    /// <returns>The canvas scroll viewer, or null when the template is not applied.</returns>
    internal static ScrollViewer? FindCanvasScroller(Control view, string canvasName = "EditorCanvas")
    {
        return view.FindControl<EditorCanvasControl>(canvasName)?
            .GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
    }
}
