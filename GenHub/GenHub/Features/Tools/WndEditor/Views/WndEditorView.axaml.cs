using Avalonia.Controls;
using Avalonia.Input;
using GenHub.Features.Tools.WndEditor.ViewModels;

namespace GenHub.Features.Tools.WndEditor.Views;

/// <summary>
/// View for the WND editor tool.
/// </summary>
public partial class WndEditorView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndEditorView"/> class.
    /// </summary>
    public WndEditorView()
    {
        InitializeComponent();
    }

    private void OnCanvasItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not WndEditorViewModel viewModel || CanvasHost == null)
        {
            return;
        }

        if (sender is Control control
            && control.DataContext is WndCanvasItemViewModel item
            && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            viewModel.BeginCanvasDrag(item, e.GetPosition(CanvasHost));
            e.Handled = true;
        }
    }

    private void OnCanvasHostPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not WndEditorViewModel viewModel || CanvasHost == null)
        {
            return;
        }

        if (Equals(e.Source, CanvasHost))
        {
            viewModel.SelectCanvasItem(null);
        }
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is WndEditorViewModel viewModel && CanvasHost != null)
        {
            viewModel.UpdateCanvasDrag(e.GetPosition(CanvasHost));
        }
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is WndEditorViewModel viewModel)
        {
            viewModel.EndCanvasDrag();
        }
    }
}
