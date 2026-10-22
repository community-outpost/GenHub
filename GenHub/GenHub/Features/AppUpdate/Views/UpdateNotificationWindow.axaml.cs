using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GenHub.Common.Controls;
using GenHub.Features.AppUpdate.ViewModels;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace GenHub.Features.AppUpdate.Views;

/// <summary>
/// Window for displaying update notifications.
/// </summary>
public partial class UpdateNotificationWindow : GenHubWindow
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

    /// <inheritdoc/>
    protected override bool DisposeDataContextOnClose => true;

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
        if (DataContext is UpdateNotificationViewModel)
        {
            // Add any initialization logic here
            await Task.CompletedTask;
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
