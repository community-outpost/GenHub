using Avalonia.Controls;
using Avalonia.Input;
using GenHub.Features.Tools.ModBuilder.ViewModels;

namespace GenHub.Features.Tools.ModBuilder.Views;

/// <summary>
/// Dialog for editing ModBuilder configuration (bundle items and packs).
/// </summary>
public partial class ConfigEditorDialog : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigEditorDialog"/> class.
    /// </summary>
    public ConfigEditorDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigEditorDialog"/> class with a ViewModel.
    /// </summary>
    /// <param name="viewModel">The ViewModel for this dialog.</param>
    public ConfigEditorDialog(ConfigEditorViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
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

