using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.Common;
using GenHub.Features.Tools.TextureEditor.ViewModels;
using System;
using System.Diagnostics.CodeAnalysis;

namespace GenHub.Features.Tools.TextureEditor.Views;

// Canvas QOL roadmap. Keep every item standardized with the WND editor canvas:
// shared behavior belongs in GenHub.Common.Editors, and per-editor behavior
// follows the WndEditorView and WndEditorViewModel patterns. Copy, cut, paste,
// and duplicate already ship through EditorToolViewModelBase, and slice move
// and resize already flow through Begin, Update, and End on the view model.
// - Undo and redo through slice snapshots in the view model (WndEditAction parity).
// - Ctrl axis-lock drag parity with the WND canvas.
// - Arrow and WASD 1px nudge (needs a focusable canvas host like WndEditorView).
// - Rubber-band multi-select (selection is single-select today).

/// <summary>
/// Code-behind for TextureEditorView.
/// </summary>
public partial class TextureEditorView : UserControl
{
    private readonly ItemsControl? _overlay;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextureEditorView"/> class.
    /// </summary>
    public TextureEditorView()
    {
        InitializeComponent();

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        _overlay = this.Find<ItemsControl>("SliceOverlay");
        if (_overlay is not null)
        {
            _overlay.PointerPressed += OnOverlayPointerPressed;
            _overlay.PointerMoved += OnOverlayPointerMoved;
            _overlay.PointerReleased += OnOverlayPointerReleased;
            _overlay.PointerCaptureLost += OnOverlayPointerCaptureLost;
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
            if (visual is Control { DataContext: TextureSliceViewModel slice })
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

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not TextureEditorViewModel viewModel)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        var focused = topLevel?.FocusManager?.GetFocusedElement();
        if (focused is TextBox or NumericUpDown)
        {
            return;
        }

        if (TryHandleUndoRedoKey(viewModel, e))
        {
            return;
        }

        if (focused is ListBox or ListBoxItem)
        {
            return;
        }

        HandleNavigationOrEditKey(viewModel, e);
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private bool TryHandleUndoRedoKey(TextureEditorViewModel viewModel, KeyEventArgs e)
    {
        var modifiers = e.KeyModifiers;
        var hasCommandModifier = (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        var isShift = (modifiers & KeyModifiers.Shift) != 0;

        if (!hasCommandModifier || (modifiers & ~(KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Shift)) != 0)
        {
            return false;
        }

        if (e.Key == Key.Z && !isShift && viewModel.UndoCommand.CanExecute(null))
        {
            viewModel.UndoCommand.Execute(null);
            e.Handled = true;
            return true;
        }

        if (((e.Key == Key.Y && !isShift) || (e.Key == Key.Z && isShift)) && viewModel.RedoCommand.CanExecute(null))
        {
            viewModel.RedoCommand.Execute(null);
            e.Handled = true;
            return true;
        }

        return false;
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private void HandleNavigationOrEditKey(TextureEditorViewModel viewModel, KeyEventArgs e)
    {
        if (viewModel.SelectedSlice is null)
        {
            return;
        }

        bool isShift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        int step = isShift ? EditorConstants.KeyboardNudgeStepLarge : EditorConstants.KeyboardNudgeStep;
        switch (e.Key)
        {
            case Key.Left:
                viewModel.NudgeSelectedSlice(-step, 0);
                e.Handled = true;
                break;
            case Key.Right:
                viewModel.NudgeSelectedSlice(step, 0);
                e.Handled = true;
                break;
            case Key.Up:
                viewModel.NudgeSelectedSlice(0, -step);
                e.Handled = true;
                break;
            case Key.Down:
                viewModel.NudgeSelectedSlice(0, step);
                e.Handled = true;
                break;
            case Key.Delete:
                if (viewModel.DeleteCommand.CanExecute(null))
                {
                    viewModel.DeleteCommand.Execute(null);
                    e.Handled = true;
                }

                break;
        }
    }

    private void OnSliceItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: TextureSliceViewModel slice } && DataContext is TextureEditorViewModel viewModel)
        {
            viewModel.SelectedSlice = slice;
        }
    }

    private void OnPickerEditRequested(object? sender, GenHub.Core.Models.Tools.TextureEditor.MappedImageDefinition definition)
    {
        if (DataContext is TextureEditorViewModel viewModel)
        {
            viewModel.LoadRegistryEntry(definition, explicitOpen: true);
        }
    }

    private void OnResizeHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();
        if (DataContext is not TextureEditorViewModel viewModel || _overlay is null)
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
            && control.DataContext is TextureSliceViewModel slice
            && point.Properties.IsLeftButtonPressed
            && control.Tag is string tagStr
            && Enum.TryParse<CanvasResizeDirection>(tagStr, out var direction))
        {
            e.Pointer.Capture(_overlay);
            viewModel.BeginSliceResize(slice, direction, e.GetPosition(_overlay));
            e.Handled = true;
        }
    }

    private void OnOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();
        if (DataContext is not TextureEditorViewModel viewModel || _overlay is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (viewModel.IsPanMode || point.Properties.IsMiddleButtonPressed)
        {
            // Pan gestures bubble to the shared EditorCanvasControl.
            return;
        }

        var slice = FindSlice(e.Source as Visual);
        if (slice is null)
        {
            if (point.Properties.IsLeftButtonPressed)
            {
                viewModel.SelectedSlice = null;
            }

            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            e.Pointer.Capture(_overlay);
            viewModel.BeginSliceDrag(slice, e.GetPosition(_overlay));
            e.Handled = true;
        }
        else if (point.Properties.IsRightButtonPressed)
        {
            viewModel.SelectedSlice = slice;
        }
    }

    private void OnOverlayPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not TextureEditorViewModel viewModel || _overlay is null)
        {
            return;
        }

        viewModel.UpdateSliceDrag(e.GetPosition(_overlay));
    }

    private void OnOverlayPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not TextureEditorViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsPanMode || e.InitialPressMouseButton == MouseButton.Middle)
        {
            // The shared EditorCanvasControl owns pan captures.
            return;
        }

        e.Pointer.Capture(null);
        viewModel.EndSliceDrag();
    }

    private void OnOverlayPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (DataContext is TextureEditorViewModel viewModel)
        {
            viewModel.EndSliceDrag();
        }
    }
}
