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

            // 1. Drag downward by 20 pixels -> value must increase
            var currentPoint = startPoint + new Point(0, 20);
            window.MouseMove(currentPoint);
            Dispatcher.UIThread.RunJobs();

            Assert.True(
                scrollBar.Value > initialValue,
                $"Expected value to increase when dragging down, but initial was {initialValue} and current is {scrollBar.Value}");

            var draggedDownValue = scrollBar.Value;

            // 2. Drag diagonally / horizontally -> value should remain stable, not drift to 0
            currentPoint += new Point(10, 0);
            window.MouseMove(currentPoint);
            Dispatcher.UIThread.RunJobs();

            Assert.True(
                scrollBar.Value >= draggedDownValue - 0.5,
                $"Expected value to remain stable during horizontal wiggle, but was {scrollBar.Value}");

            // 3. Drag upward by 10 pixels -> value must decrease from draggedDownValue
            currentPoint += new Point(-10, -10);
            window.MouseMove(currentPoint);
            Dispatcher.UIThread.RunJobs();

            Assert.True(
                scrollBar.Value < draggedDownValue,
                $"Expected value to decrease when dragging up, but previous was {draggedDownValue} and current is {scrollBar.Value}");

            window.MouseUp(currentPoint, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
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

            // 1. Drag rightward by 20 pixels -> value must increase
            var currentPoint = startPoint + new Point(20, 0);
            window.MouseMove(currentPoint);
            Dispatcher.UIThread.RunJobs();

            Assert.True(
                scrollBar.Value > initialValue,
                $"Expected value to increase when dragging right, but initial was {initialValue} and current is {scrollBar.Value}");

            var draggedRightValue = scrollBar.Value;

            // 2. Drag vertically -> value should remain stable, not drift to 0
            currentPoint += new Point(0, 10);
            window.MouseMove(currentPoint);
            Dispatcher.UIThread.RunJobs();

            Assert.True(
                scrollBar.Value >= draggedRightValue - 0.5,
                $"Expected value to remain stable during vertical wiggle, but was {scrollBar.Value}");

            // 3. Drag leftward by 10 pixels -> value must decrease from draggedRightValue
            currentPoint += new Point(-10, -10);
            window.MouseMove(currentPoint);
            Dispatcher.UIThread.RunJobs();

            Assert.True(
                scrollBar.Value < draggedRightValue,
                $"Expected value to decrease when dragging left, but previous was {draggedRightValue} and current is {scrollBar.Value}");

            window.MouseUp(currentPoint, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            window.Close();
        }
    }
}
