using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using GenHub.Features.Downloads.ViewModels;
using System;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// Code-behind for ContentDetailView.
/// </summary>
public partial class ContentDetailView : UserControl
{
    private Point? resizeStartPoint;
    private double initialModalWidth;
    private double initialModalHeight;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentDetailView"/> class.
    /// </summary>
    public ContentDetailView()
    {
        InitializeComponent();

        var resizeGrip = this.FindControl<Border>("VideoModalResizeGrip");
        if (resizeGrip != null)
        {
            resizeGrip.PointerPressed += OnResizeGripPointerPressed;
            resizeGrip.PointerMoved += OnResizeGripPointerMoved;
            resizeGrip.PointerReleased += OnResizeGripPointerReleased;
            resizeGrip.PointerCaptureLost += OnResizeGripPointerCaptureLost;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape && DataContext is ContentDetailViewModel vm && vm.IsVideoPlayerOpen)
        {
            if (vm.IsVideoPlayerFullscreen)
            {
                vm.IsVideoPlayerFullscreen = false;
            }
            else
            {
                vm.CloseVideoPlayerCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    private void OnResizeGripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ContentDetailViewModel vm && !vm.IsVideoPlayerFullscreen)
        {
            resizeStartPoint = e.GetPosition(this);
            initialModalWidth = vm.VideoModalWidth;
            initialModalHeight = vm.VideoModalHeight;
            if (sender is InputElement grip)
            {
                e.Pointer.Capture(grip);
            }

            e.Handled = true;
        }
    }

    private void OnResizeGripPointerMoved(object? sender, PointerEventArgs e)
    {
        if (resizeStartPoint.HasValue && DataContext is ContentDetailViewModel vm && !vm.IsVideoPlayerFullscreen)
        {
            var currentPoint = e.GetPosition(this);
            var deltaX = currentPoint.X - resizeStartPoint.Value.X;
            var deltaY = currentPoint.Y - resizeStartPoint.Value.Y;

            var maxWidth = Math.Max(0, Bounds.Width - 48);
            var maxHeight = Math.Max(0, Bounds.Height - 48);
            var minWidth = Math.Min(560, maxWidth);
            var minHeight = Math.Min(380, maxHeight);

            vm.VideoModalWidth = Math.Clamp(initialModalWidth + deltaX, minWidth, maxWidth);
            vm.VideoModalHeight = Math.Clamp(initialModalHeight + deltaY, minHeight, maxHeight);
            e.Handled = true;
        }
    }

    private void OnResizeGripPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndResize(e.Pointer);
        e.Handled = true;
    }

    private void OnResizeGripPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndResize(null);
    }

    private void EndResize(IPointer? pointer)
    {
        resizeStartPoint = null;
        pointer?.Capture(null);
    }
}
