using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Features.AppUpdate.Views;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Settings.ViewModels;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace GenHub.Common.ViewModels;

/// <summary>
/// Main view model for the application.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ILogger<MainViewModel>? _logger;
    private readonly IGameInstallationDetectionOrchestrator _gameInstallationDetectionOrchestrator;
    private readonly IConfigurationProviderService _configurationProvider;
    private readonly IUserSettingsService _userSettingsService;

    [ObservableProperty]
    private NavigationTab _selectedTab = NavigationTab.GameProfiles;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
    /// <param name="gameProfilesViewModel">Game profiles view model.</param>
    /// <param name="downloadsViewModel">Downloads view model.</param>
    /// <param name="settingsViewModel">Settings view model.</param>
    /// <param name="gameInstallationDetectionOrchestrator">Game installation orchestrator.</param>
    /// <param name="configurationProvider">Configuration provider service.</param>
    /// <param name="userSettingsService">User settings service for persistence operations.</param>
    /// <param name="logger">Logger instance.</param>
    public MainViewModel(
        GameProfileLauncherViewModel gameProfilesViewModel,
        DownloadsViewModel downloadsViewModel,
        SettingsViewModel settingsViewModel,
        IGameInstallationDetectionOrchestrator gameInstallationDetectionOrchestrator,
        IConfigurationProviderService configurationProvider,
        IUserSettingsService userSettingsService,
        ILogger<MainViewModel>? logger = null)
    {
        GameProfilesViewModel = gameProfilesViewModel;
        DownloadsViewModel = downloadsViewModel;
        SettingsViewModel = settingsViewModel;
        _gameInstallationDetectionOrchestrator = gameInstallationDetectionOrchestrator;
        _configurationProvider = configurationProvider;
        _userSettingsService = userSettingsService;
        _logger = logger;

        // Load initial settings using unified configuration
        try
        {
            _selectedTab = _configurationProvider.GetLastSelectedTab();
            _logger?.LogDebug($"Initial settings loaded, selected tab: {_selectedTab}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load initial settings");
            _selectedTab = NavigationTab.GameProfiles;
        }

        // Tab change handled by ObservableProperty partial method
    }

    /// <summary>
    /// Gets a value indicating whether an update is available (dummy implementation for UI binding).
    /// </summary>
    public static bool HasUpdateAvailable => false;

    /// <summary>
    /// Gets the game profiles view model.
    /// </summary>
    public GameProfileLauncherViewModel GameProfilesViewModel { get; }

    /// <summary>
    /// Gets the downloads view model.
    /// </summary>
    public DownloadsViewModel DownloadsViewModel { get; }

    /// <summary>
    /// Gets the settings view model.
    /// </summary>
    public SettingsViewModel SettingsViewModel { get; }

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
        _ => GameProfilesViewModel,
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

    private void SaveSelectedTab(NavigationTab selectedTab)
    {
        try
        {
            _userSettingsService.Update(settings =>
            {
                settings.LastSelectedTab = selectedTab;
            });
            _logger?.LogDebug($"Updated last selected tab to: {selectedTab}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to update selected tab setting");
        }
    }

    partial void OnSelectedTabChanged(NavigationTab value)
    {
        OnPropertyChanged(nameof(CurrentTabViewModel));

        // Notify SettingsViewModel when it becomes visible/invisible
        SettingsViewModel.IsViewVisible = value == NavigationTab.Settings;

        SaveSelectedTab(value);
    }

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
