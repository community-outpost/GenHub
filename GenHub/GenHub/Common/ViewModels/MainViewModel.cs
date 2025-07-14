using Avalonia.Controls;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.Models;
using GenHub.Features.AppUpdate.Views;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.ViewModels;

/// <summary>
/// Shell ViewModel for the main launcher view.
/// </summary>
public partial class MainViewModel(ILogger<MainViewModel>? logger = null) : ObservableObject
{
    private readonly ILogger<MainViewModel>? _logger = logger;

    [ObservableProperty]
    private NavigationTab _selectedTab = NavigationTab.GameProfiles;

    /// <summary>
    /// Gets the available navigation tabs.
    /// </summary>
    public NavigationTab[] AvailableTabs { get; } =
    {
        NavigationTab.GameProfiles,
        NavigationTab.Downloads,
        NavigationTab.Settings,
    };

    /// <summary>
    /// Gets the display name for a navigation tab.
    /// </summary>
    /// <param name="tab">The navigation tab.</param>
    /// <returns>The display name.</returns>
    public static string GetTabDisplayName(NavigationTab tab) => tab switch
    {
        NavigationTab.GameProfiles => "Game Profiles",
        NavigationTab.Downloads => "Downloads",
        NavigationTab.Settings => "Settings",
        _ => tab.ToString(),
    };

    /// <summary>
    /// Performs asynchronous startup work.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        _logger?.LogInformation("MainViewModel initialized");
        await Task.CompletedTask;
    }

    private static Window? GetMainWindow()
    {
        return Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime dt
            ? dt.MainWindow
            : null;
    }

    /// <summary>
    /// Switches to the specified navigation tab.
    /// </summary>
    /// <param name="tab">The tab to navigate to.</param>
    [RelayCommand]
    private void SelectTab(NavigationTab tab) =>
        SelectedTab = tab;

    /// <summary>
    /// Shows the update notification dialog.
    /// </summary>
    [RelayCommand]
    private async Task ShowUpdateDialogAsync()
    {
        try
        {
            _logger?.LogInformation("ShowUpdateDialogCommand executed");

            var mainWindow = GetMainWindow();
            if (mainWindow is not null)
            {
                _logger?.LogInformation("Opening update notification window");

                var updateWindow = new UpdateNotificationWindow
                {
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                };

                await updateWindow.ShowDialog(mainWindow);

                _logger?.LogInformation("Update notification window closed");
            }
            else
            {
                _logger?.LogWarning("Could not find main window to show update dialog");
            }
        }
        catch (System.Exception ex)
        {
            _logger?.LogError(ex, "Failed to show update notification window");
        }
    }
}
