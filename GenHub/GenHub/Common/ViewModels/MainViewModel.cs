using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Features.AppUpdate.Views;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Settings.ViewModels;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace GenHub.Common.ViewModels;

/// <summary>
/// Main view model for the application.
/// </summary>
public partial class MainViewModel(
    GameProfileLauncherViewModel gameProfilesViewModel,
    DownloadsViewModel downloadsViewModel,
    SettingsViewModel settingsViewModel,
    IGameInstallationDetectionOrchestrator gameInstallationDetectionOrchestrator,
    ILogger<MainViewModel>? logger = null
) : ObservableObject
{
    private readonly ILogger<MainViewModel>? _logger = logger;
    private readonly IGameInstallationDetectionOrchestrator _gameInstallationDetectionOrchestrator = gameInstallationDetectionOrchestrator;

    [ObservableProperty]
    private NavigationTab _selectedTab = NavigationTab.GameProfiles;

    /// <summary>
    /// Gets a value indicating whether an update is available (dummy implementation for UI binding).
    /// </summary>
    public static bool HasUpdateAvailable => false;

    /// <summary>
    /// Gets the Game Profiles tab ViewModel.
    /// </summary>
    public GameProfileLauncherViewModel GameProfilesViewModel => gameProfilesViewModel;

    /// <summary>
    /// Gets the Downloads tab ViewModel.
    /// </summary>
    public DownloadsViewModel DownloadsViewModel => downloadsViewModel;

    /// <summary>
    /// Gets the Settings tab ViewModel.
    /// </summary>
    public SettingsViewModel SettingsViewModel => settingsViewModel;

    /// <summary>
    /// Gets the collection of detected game installations.
    /// </summary>
    public ObservableCollection<string> GameInstallations { get; } = new();

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
    /// Gets the current tab's ViewModel for ContentControl binding.
    /// </summary>
    public object CurrentTabViewModel => SelectedTab switch
    {
        NavigationTab.GameProfiles => GameProfilesViewModel,
        NavigationTab.Downloads => DownloadsViewModel,
        NavigationTab.Settings => SettingsViewModel,
        _ => GameProfilesViewModel
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
    /// Performs asynchronous initialization for the shell and all tabs.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        await GameProfilesViewModel.InitializeAsync();
        await DownloadsViewModel.InitializeAsync();
        await SettingsViewModel.InitializeAsync();
        _logger?.LogInformation("MainViewModel initialized");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Scans for game installations.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task ScanForGamesAsync()
    {
        _logger?.LogInformation("Starting game installation scan");

        try
        {
            GameInstallations.Clear();

            var result = await _gameInstallationDetectionOrchestrator.DetectAllInstallationsAsync();

            if (result.Success)
            {
                foreach (var installation in result.Items)
                {
                    var installationString = installation?.ToString();
                    if (!string.IsNullOrEmpty(installationString))
                    {
                        GameInstallations.Add(installationString);
                    }
                }

                _logger?.LogInformation("Found {Count} game installations", result.Items.Count);
            }
            else
            {
                _logger?.LogWarning("Game installation scan failed: {Errors}", string.Join("; ", result.Errors));
            }
        }
        catch (System.Exception ex)
        {
            _logger?.LogError(ex, "Error occurred during game installation scan");
        }
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

    partial void OnSelectedTabChanged(NavigationTab value) =>
        OnPropertyChanged(nameof(CurrentTabViewModel));

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
