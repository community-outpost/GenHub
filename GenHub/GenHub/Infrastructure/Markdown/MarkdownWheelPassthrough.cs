using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using System.Linq;
using MarkdownScrollViewer = global::Markdown.Avalonia.MarkdownScrollViewer;

namespace GenHub.Infrastructure.Markdown;

/// <summary>
/// Lets the mouse wheel over a <see cref="MarkdownScrollViewer"/> reach the scroller that contains it.
/// </summary>
/// <remarks>
/// The viewer wraps its content in its own vertically scrolling <see cref="ScrollViewer"/>. Inside an outer scroller,
/// fractional layout on high-DPI screens can leave that inner viewer a sliver taller than its viewport, so it takes
/// the wheel and the page does not scroll. Turning off the inner vertical scrolling hands the wheel to the outer scroller.
/// </remarks>
public static class MarkdownWheelPassthrough
{
    /// <summary>
    /// Identifies the IsEnabled attached property.
    /// </summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<MarkdownScrollViewer, bool>("IsEnabled", typeof(MarkdownWheelPassthrough));

    static MarkdownWheelPassthrough()
    {
        IsEnabledProperty.Changed.AddClassHandler<MarkdownScrollViewer>((viewer, args) =>
        {
            viewer.AttachedToVisualTree -= OnAttachedToVisualTree;
            if (args.NewValue is true)
            {
                viewer.AttachedToVisualTree += OnAttachedToVisualTree;
                DisableInnerScrolling(viewer);
            }
        });
    }

    /// <summary>Gets whether the wheel passes through the Markdown viewer.</summary>
    /// <param name="element">The Markdown viewer.</param>
    /// <returns><c>true</c> when the wheel passes through.</returns>
    public static bool GetIsEnabled(MarkdownScrollViewer element) => element.GetValue(IsEnabledProperty);

    /// <summary>Sets whether the wheel passes through the Markdown viewer.</summary>
    /// <param name="element">The Markdown viewer.</param>
    /// <param name="value">Whether the wheel passes through.</param>
    public static void SetIsEnabled(MarkdownScrollViewer element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is MarkdownScrollViewer viewer)
        {
            DisableInnerScrolling(viewer);
        }
    }

    private static void DisableInnerScrolling(MarkdownScrollViewer viewer)
    {
        foreach (var inner in viewer.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.FindAncestorOfType<MarkdownScrollViewer>() == viewer))
        {
            inner.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        }
    }
}
