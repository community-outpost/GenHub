using Avalonia.Controls;
using Avalonia.Input;

namespace GenHub.Features.Tools.Views.Dialogs;

/// <summary>
/// Generic window for hosting tool dialogs.
/// </summary>
public partial class ToolDialogWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolDialogWindow"/> class.
    /// </summary>
    public ToolDialogWindow()
    {
        InitializeComponent();
    }

    private void OnDragRegionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}
