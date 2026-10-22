using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using GenHub.Core.Models.Tools.Common;
using GenHub.Features.Tools.WndEditor.ViewModels;
using System;
using System.Collections.Generic;

namespace GenHub.Features.Tools.WndEditor.Views;

/// <summary>
/// View for the WND editor tool.
/// </summary>
public partial class WndEditorView : UserControl
{
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
        if (DataContext is WndEditorViewModel viewModel)
        {
            EditorCanvas?.FrameTo(viewModel.CanvasContentOffset);
        }
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
            // Pan gestures bubble to the shared EditorCanvasControl.
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
            // Pan gestures bubble to the shared EditorCanvasControl.
            return;
        }

        if (sender is Control control
            && control.DataContext is WndCanvasItemViewModel item
            && point.Properties.IsLeftButtonPressed
            && control.Tag is string tagStr
            && Enum.TryParse<CanvasResizeDirection>(tagStr, out var direction))
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
            // Pan gestures bubble to the shared EditorCanvasControl.
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

        viewModel.UpdateCanvasDrag(e.GetPosition(CanvasHost));
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not WndEditorViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsPanMode || e.InitialPressMouseButton == MouseButton.Middle)
        {
            // The shared EditorCanvasControl owns pan captures.
            return;
        }

        e.Pointer.Capture(null);
        viewModel.EndCanvasDrag();
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
            viewModel.NotifyDropError(ex);
        }
    }
}
