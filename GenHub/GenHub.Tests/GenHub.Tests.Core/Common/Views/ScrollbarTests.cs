using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace GenHub.Tests.Core.Common.Views;

/// <summary>
/// Verifies scrollbar thumb dragging behavior and prevents regression of inverted/upward drag drift.
/// </summary>
public class ScrollbarTests
{
    /// <summary>
    /// Verifies that dragging the vertical scrollbar thumb downward increases the value,
    /// dragging upward decreases the value, and horizontal movement does not cause drift.
    /// </summary>
    [AvaloniaFact]
    public void VerticalScrollBar_PointerDrag_MovesValueCorrectly()
    {
        var scrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Height = 200,
            Minimum = 0,
            Maximum = 100,
            Value = 50,
            ViewportSize = 20,
        };

        var window = new Window
        {
            Width = 200,
            Height = 300,
            Content = scrollBar,
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            AssertScrollBarThumbDrag(
                scrollBar,
                window,
                forwardDelta: new Point(0, 20),
                orthogonalDelta: new Point(10, 0),
                backwardDelta: new Point(-10, -10),
                forwardDirectionName: "down",
                orthogonalDirectionName: "horizontal",
                backwardDirectionName: "up");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that dragging the horizontal scrollbar thumb to the right increases the value,
    /// dragging to the left decreases the value, and vertical movement does not cause drift.
    /// </summary>
    [AvaloniaFact]
    public void HorizontalScrollBar_PointerDrag_MovesValueCorrectly()
    {
        var scrollBar = new ScrollBar
        {
            Orientation = Orientation.Horizontal,
            Width = 200,
            Minimum = 0,
            Maximum = 100,
            Value = 50,
            ViewportSize = 20,
        };

        var window = new Window
        {
            Width = 300,
            Height = 200,
            Content = scrollBar,
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            AssertScrollBarThumbDrag(
                scrollBar,
                window,
                forwardDelta: new Point(20, 0),
                orthogonalDelta: new Point(0, 10),
                backwardDelta: new Point(-10, -10),
                forwardDirectionName: "right",
                orthogonalDirectionName: "vertical",
                backwardDirectionName: "left");
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertScrollBarThumbDrag(
        ScrollBar scrollBar,
        Window window,
        Point forwardDelta,
        Point orthogonalDelta,
        Point backwardDelta,
        string forwardDirectionName,
        string orthogonalDirectionName,
        string backwardDirectionName)
    {
        var thumb = scrollBar.GetVisualDescendants().OfType<Thumb>().FirstOrDefault();
        Assert.NotNull(thumb);

        var initialValue = scrollBar.Value;

        // Get center of the thumb in window coordinates
        var thumbPointOnWindow = thumb.TranslatePoint(
            new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2),
            window);
        Assert.True(thumbPointOnWindow.HasValue);

        var startPoint = thumbPointOnWindow.Value;

        // Simulate pointer down on the thumb
        window.MouseDown(startPoint, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // 1. Drag forward along primary axis -> value must increase
        var currentPoint = startPoint + forwardDelta;
        window.MouseMove(currentPoint);
        Dispatcher.UIThread.RunJobs();

        Assert.True(
            scrollBar.Value > initialValue,
            $"Expected value to increase when dragging {forwardDirectionName}, but initial was {initialValue} and current is {scrollBar.Value}");

        var draggedForwardValue = scrollBar.Value;

        // 2. Drag orthogonally -> value should remain stable, not drift to 0
        currentPoint += orthogonalDelta;
        window.MouseMove(currentPoint);
        Dispatcher.UIThread.RunJobs();

        Assert.True(
            scrollBar.Value >= draggedForwardValue - 0.5,
            $"Expected value to remain stable during {orthogonalDirectionName} wiggle, but was {scrollBar.Value}");

        // 3. Drag backward -> value must decrease from draggedForwardValue
        currentPoint += backwardDelta;
        window.MouseMove(currentPoint);
        Dispatcher.UIThread.RunJobs();

        Assert.True(
            scrollBar.Value < draggedForwardValue,
            $"Expected value to decrease when dragging {backwardDirectionName}, but previous was {draggedForwardValue} and current is {scrollBar.Value}");

        window.MouseUp(currentPoint, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
