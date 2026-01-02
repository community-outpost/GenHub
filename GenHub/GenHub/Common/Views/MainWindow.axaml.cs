using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace GenHub.Common.Views;

/// <summary>
/// Main application window for GenHub.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Handles pointer pressed events on the title bar for dragging.
    /// </summary>
    /// <param name="sender">The sender object.</param>
    /// <param name="e">The pointer event arguments.</param>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2)
            {
                MaximizeButton_Click(sender, new Avalonia.Interactivity.RoutedEventArgs());
            }
            else
            {
                BeginMoveDrag(e);
            }
        }
    }

    /// <summary>
    /// Handles the minimize button click.
    /// </summary>
    private void MinimizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// Handles the maximize/restore button click.
    /// </summary>
    private void MaximizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    /// <summary>
    /// Handles the close button click.
    /// </summary>
    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
