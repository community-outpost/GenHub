using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using GenHub.Core.Constants;
using GenHub.Features.Tools.TextureEditor.ViewModels;
using System;

namespace GenHub.Features.Tools.TextureEditor.Views;

/// <summary>
/// Code-behind for TextureEditorView.
/// </summary>
public partial class TextureEditorView : UserControl
{
    private readonly ItemsControl? _overlay;
    private TextureSliceViewModel? _dragSlice;
    private Point _dragStart;
    private int _dragOriginLeft;
    private int _dragOriginTop;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextureEditorView"/> class.
    /// </summary>
    public TextureEditorView()
    {
        InitializeComponent();

        _overlay = this.Find<ItemsControl>("SliceOverlay");
        if (_overlay is not null)
        {
            _overlay.PointerPressed += OnOverlayPointerPressed;
            _overlay.PointerMoved += OnOverlayPointerMoved;
            _overlay.PointerReleased += OnOverlayPointerReleased;
            _overlay.PointerCaptureLost += OnOverlayPointerCaptureLost;
        }

        var scroll = this.Find<ScrollViewer>("CanvasScroll");
        if (scroll is not null)
        {
            scroll.PointerWheelChanged += OnCanvasWheelChanged;
        }

        var picker = this.Find<GenHub.Common.Controls.MappedImagePickerControl>("ImagePicker");
        if (picker is not null)
        {
            picker.EditRequested += OnPickerEditRequested;
        }
    }

    private static TextureSliceViewModel? FindSlice(Visual? visual)
    {
        while (visual is not null)
        {
            if (visual is Border { DataContext: TextureSliceViewModel slice })
            {
                return slice;
            }

            visual = visual.GetVisualParent();
        }

        return null;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnPickerEditRequested(object? sender, GenHub.Core.Models.Tools.TextureEditor.MappedImageDefinition definition)
    {
        if (DataContext is TextureEditorViewModel viewModel)
        {
            viewModel.LoadRegistryEntry(definition);
        }
    }

    private void OnOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not TextureEditorViewModel viewModel)
        {
            return;
        }

        var slice = FindSlice(e.Source as Visual);
        if (slice is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        viewModel.SelectedSlice = slice;
        _dragSlice = slice;
        _dragStart = e.GetPosition(this);
        _dragOriginLeft = slice.Left;
        _dragOriginTop = slice.Top;
        if (_overlay is not null)
        {
            e.Pointer.Capture(_overlay);
        }

        e.Handled = true;
    }

    private void OnOverlayPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSlice is null || DataContext is not TextureEditorViewModel viewModel)
        {
            return;
        }

        double zoom = Math.Max(EditorConstants.ZoomMin, viewModel.Zoom);
        var position = e.GetPosition(this);
        int deltaX = (int)Math.Round((position.X - _dragStart.X) / zoom);
        int deltaY = (int)Math.Round((position.Y - _dragStart.Y) / zoom);
        int width = _dragSlice.Width;
        int height = _dragSlice.Height;

        _dragSlice.Left = Math.Max(0, _dragOriginLeft + deltaX);
        _dragSlice.Top = Math.Max(0, _dragOriginTop + deltaY);
        _dragSlice.Right = _dragSlice.Left + width;
        _dragSlice.Bottom = _dragSlice.Top + height;
    }

    private void OnOverlayPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        _dragSlice = null;
    }

    private void OnOverlayPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _dragSlice = null;
    }

    private void OnCanvasWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not TextureEditorViewModel viewModel || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (e.Delta.Y > 0)
        {
            viewModel.ZoomInCommand.Execute(null);
        }
        else
        {
            viewModel.ZoomOutCommand.Execute(null);
        }

        e.Handled = true;
    }
}
