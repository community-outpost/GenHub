using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GenHub.Features.Tools.ModBuilder.ViewModels;

namespace GenHub.Features.Tools.ModBuilder.Views;

/// <summary>
/// Dialog window for the ModBuilder Game Asset and File Manager.
/// </summary>
public partial class FileManagerDialog : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileManagerDialog"/> class.
    /// </summary>
    public FileManagerDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileManagerDialog"/> class with the specified ViewModel.
    /// </summary>
    /// <param name="viewModel">The file manager view model.</param>
    public FileManagerDialog(FileManagerViewModel viewModel)
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

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

