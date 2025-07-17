using Avalonia;
using Avalonia.Controls;
using Avalonia.Diagnostics;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GenHub;
using GenHub.Features.AppUpdate.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace GenHub.Features.AppUpdate.Views;

/// <summary>
/// Window for displaying update notifications.
/// </summary>
public partial class UpdateNotificationWindow : Window
{
    private readonly ILogger<UpdateNotificationWindow>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateNotificationWindow"/> class.
    /// </summary>
    public UpdateNotificationWindow()
    {
        _logger = AppLocator.GetServiceOrDefault<ILogger<UpdateNotificationWindow>>();

        InitializeComponent();

        try
        {
            // Set up the DataContext with proper DI resolution
            DataContext = AppLocator.GetServiceOrDefault<UpdateNotificationViewModel>();
            _logger?.LogInformation("UpdateNotificationWindow initialized with ViewModel");
        }
        catch (System.Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize UpdateNotificationWindow ViewModel");
        }
    }

    /// <summary>
    /// Shows the update notification window as a dialog.
    /// </summary>
    /// <param name="parent">The parent window.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task ShowAsync(Window parent)
    {
        var window = new UpdateNotificationWindow();
        await window.ShowDialog(parent);
    }

    /// <summary>
    /// Performs asynchronous initialization logic for the window.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        if (DataContext is UpdateNotificationViewModel viewModel)
        {
            // Add any initialization logic here
            await Task.CompletedTask;
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Handles the close button click event.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The event args.</param>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Handles pointer pressed event for the title bar to enable window dragging.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The pointer event args.</param>
    private void TitleBar_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }
}
