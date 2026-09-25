using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WndEditor;
using GenHub.Features.Tools.WndEditor.ViewModels;
using System;
using System.Collections.Generic;

namespace GenHub.Features.Tools.WndEditor.Views;

/// <summary>
/// View for the WND editor tool.
/// </summary>
public partial class WndEditorView : UserControl
{
    private bool _panning;
    private Point _panStartPoint;
    private Vector _panStartOffset;
    private WndEditorViewModel? _framingViewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="WndEditorView"/> class.
    /// </summary>
    public WndEditorView()
    {
        InitializeComponent();
        Focusable = true;
        DataContextChanged += OnDataContextChanged;
        AddHandler(DragDrop.DragOverEvent, OnCanvasDragOver);
        AddHandler(DragDrop.DropEvent, OnCanvasDrop);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (_framingViewModel != null)
        {
            _framingViewModel.CanvasFramingRequested -= OnCanvasFramingRequested;
            _framingViewModel = null;
        }

        if (DataContext is WndEditorViewModel viewModel)
        {
            _framingViewModel = viewModel;
            _framingViewModel.CanvasFramingRequested += OnCanvasFramingRequested;
        }
    }

    private void OnCanvasFramingRequested(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (DataContext is not WndEditorViewModel viewModel || CanvasScrollViewer == null)
        {
            return;
        }

        var offset = viewModel.CanvasContentOffset;
        Dispatcher.UIThread.Post(
            () => CanvasScrollViewer.Offset = offset,
            DispatcherPriority.Loaded);
    }

    private void OnCanvasItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();
        if (DataContext is not WndEditorViewModel viewModel || CanvasHost == null)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (viewModel.IsPanMode || point.Properties.IsMiddleButtonPressed)
        {
            TryBeginPan(e);
            return;
        }

        if (sender is Control control
            && control.DataContext is WndCanvasItemViewModel item
            && point.Properties.IsLeftButtonPressed)
        {
            e.Pointer.Capture(CanvasHost);
            viewModel.BeginCanvasDrag(item, e.GetPosition(CanvasHost));
            e.Handled = true;
        }
    }

    private void OnResizeHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not WndEditorViewModel viewModel || CanvasHost == null)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (viewModel.IsPanMode || point.Properties.IsMiddleButtonPressed)
        {
            TryBeginPan(e);
            return;
        }

        if (sender is Control control
            && control.DataContext is WndCanvasItemViewModel item
            && point.Properties.IsLeftButtonPressed
            && control.Tag is string tagStr
            && Enum.TryParse<WndResizeDirection>(tagStr, out var direction))
        {
            e.Pointer.Capture(CanvasHost);
            viewModel.BeginCanvasResize(item, direction, e.GetPosition(CanvasHost));
            e.Handled = true;
        }
    }

    private void OnCanvasHostPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();
        if (DataContext is not WndEditorViewModel viewModel || CanvasHost == null)
        {
            return;
        }

        if (viewModel.IsPanMode || e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
        {
            TryBeginPan(e);
            return;
        }

        if (Equals(e.Source, CanvasHost))
        {
            viewModel.SelectCanvasItem(null);
        }
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not WndEditorViewModel viewModel || CanvasHost == null)
        {
            return;
        }

        if (_panning && CanvasScrollViewer != null)
        {
            UpdatePan(e.GetPosition(CanvasScrollViewer));
            return;
        }

        viewModel.UpdateCanvasDrag(e.GetPosition(CanvasHost));
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        if (_panning)
        {
            _panning = false;
            return;
        }

        if (DataContext is WndEditorViewModel viewModel)
        {
            viewModel.EndCanvasDrag();
        }
    }

    private void OnCanvasWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not WndEditorViewModel viewModel || CanvasScrollViewer == null)
        {
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Delta.Y.Equals(0))
        {
            return;
        }

        e.Handled = true;
        var oldZoom = viewModel.Zoom;
        var newZoom = e.Delta.Y > 0
            ? oldZoom * WndConstants.Editor.ZoomStepFactor
            : oldZoom / WndConstants.Editor.ZoomStepFactor;
        newZoom = Math.Clamp(newZoom, WndConstants.Editor.MinZoom, WndConstants.Editor.MaxZoom);
        if (newZoom.Equals(oldZoom))
        {
            return;
        }

        var scroller = CanvasScrollViewer;
        var viewportPoint = e.GetPosition(scroller);
        var scale = newZoom / oldZoom;
        var contentX = scroller.Offset.X + viewportPoint.X;
        var contentY = scroller.Offset.Y + viewportPoint.Y;
        viewModel.Zoom = newZoom;
        Dispatcher.UIThread.Post(
            () => scroller.Offset = new Vector(
                (contentX * scale) - viewportPoint.X,
                (contentY * scale) - viewportPoint.Y),
            DispatcherPriority.Loaded);
    }

    private void TryBeginPan(PointerPressedEventArgs e)
    {
        if (CanvasHost == null || CanvasScrollViewer == null)
        {
            return;
        }

        e.Pointer.Capture(CanvasHost);
        _panning = true;
        _panStartPoint = e.GetPosition(CanvasScrollViewer);
        _panStartOffset = CanvasScrollViewer.Offset;
        e.Handled = true;
    }

    private void UpdatePan(Point viewportPoint)
    {
        if (CanvasScrollViewer == null)
        {
            return;
        }

        var deltaX = viewportPoint.X - _panStartPoint.X;
        var deltaY = viewportPoint.Y - _panStartPoint.Y;
        CanvasScrollViewer.Offset = new Vector(_panStartOffset.X - deltaX, _panStartOffset.Y - deltaY);
    }

    private static void OnCanvasDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files) || e.Data.Contains(DataFormats.Text))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private static List<string> ExtractDroppedFilePaths(DragEventArgs e)
    {
        var paths = new List<string>();
        var files = e.Data.GetFiles();
        if (files == null)
        {
            return paths;
        }

        foreach (var file in files)
        {
            var localPath = file?.Path?.LocalPath;
            if (!string.IsNullOrEmpty(localPath))
            {
                paths.Add(localPath);
            }
        }

        return paths;
    }

    private async void OnCanvasDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not WndEditorViewModel viewModel)
        {
            return;
        }

        var dropPos = CanvasHost != null ? e.GetPosition(CanvasHost) : (Point?)null;

        try
        {
            if (e.Data.Contains(DataFormats.Files))
            {
                var paths = ExtractDroppedFilePaths(e);
                if (paths.Count > 0)
                {
                    e.Handled = true;
                    await viewModel.ApplyDroppedFilesAsync(paths, dropPos);
                }
            }
            else if (e.Data.Contains(DataFormats.Text))
            {
                var text = e.Data.GetText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    e.Handled = true;
                    viewModel.ApplyDroppedImageName(text.Trim(), dropPos);
                }
            }
        }
        catch (Exception ex)
        {
            viewModel.NotifyError("Drop Failed", $"Failed to process dropped item: {ex.Message}");
        }
    }
}
