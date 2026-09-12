using Avalonia.Controls;
using Avalonia.Input;

namespace GenHub.Features.Tools.ModBuilder.Views;

/// <summary>
/// Bundle pack editor dialog for managing bundle pack contents.
/// </summary>
public partial class BundlePackEditorDialog : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BundlePackEditorDialog"/> class.
    /// </summary>
    public BundlePackEditorDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Handles pointer pressed events on the title bar for dragging and maximizing.
    /// </summary>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2 && CanResize)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                BeginMoveDrag(e);
            }
        }
    }
}

