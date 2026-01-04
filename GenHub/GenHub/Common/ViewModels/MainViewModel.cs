using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Notifications.ViewModels;
using GenHub.Features.Settings.ViewModels;
using GenHub.Features.Tools.ViewModels;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.ViewModels;

/// <summary>
/// Initializes a new instance of the <see cref="MainViewModel"/> class.
/// </summary>
/// <param name="gameProfilesViewModel">Game profiles view model.</param>
/// <param name="downloadsViewModel">Downloads view model.</param>
/// <param name="toolsViewModel">Tools view model.</param>
/// <param name="settingsViewModel">Settings view model.</param>
/// <param name="notificationManager">Notification manager view model.</param>
/// <param name="configurationProvider">Configuration provider service.</param>
/// <param name="userSettingsService">User settings service for persistence operations.</param>
/// <param name="velopackUpdateManager">The Velopack update manager for checking updates.</param>
/// <param name="notificationService">Service for showing notifications.</param>
/// <param name="notificationFeedViewModel">Notification feed view model.</param>
/// <param name="logger">Logger instance.</param>
public partial class MainViewModel(
    GameProfileLauncherViewModel gameProfilesViewModel,
    DownloadsViewModel downloadsViewModel,
    ToolsViewModel toolsViewModel,
    SettingsViewModel settingsViewModel,
    NotificationManagerViewModel notificationManager,
    IConfigurationProviderService configurationProvider,
    IUserSettingsService userSettingsService,
    IVelopackUpdateManager velopackUpdateManager,
    INotificationService notificationService,
    NotificationFeedViewModel notificationFeedViewModel,
    ILogger<MainViewModel> logger) : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _initializationCts = new();

    /// <summary>
    /// Gets the notification feed view model.
    /// </summary>
    public NotificationFeedViewModel NotificationFeed => notificationFeedViewModel;

    /// <summary>
    /// Gets the game profiles view model.
    /// </summary>
    public GameProfileLauncherViewModel GameProfilesViewModel { get; } = gameProfilesViewModel;

    /// <summary>
    /// Gets the downloads view model.
    /// </summary>
    public DownloadsViewModel DownloadsViewModel { get; } = downloadsViewModel;

    /// <summary>
    /// Gets the tools view model.
    /// </summary>
    public ToolsViewModel ToolsViewModel { get; } = toolsViewModel;

    /// <summary>
    /// Gets the settings view model.
    /// </summary>
    public SettingsViewModel SettingsViewModel { get; } = settingsViewModel;

    /// <summary>
    /// Gets the notification manager view model.
    /// </summary>
    public NotificationManagerViewModel NotificationManager { get; } = notificationManager;

    [ObservableProperty]
    private NavigationTab _selectedTab = LoadInitialTab(configurationProvider, logger);

    private static NavigationTab LoadInitialTab(IConfigurationProviderService configurationProvider, ILogger<MainViewModel>? logger)
    {
        try
        {
            var tab = configurationProvider.GetLastSelectedTab();
            if (tab == NavigationTab.Tools)
            {
                tab = NavigationTab.GameProfiles;
            }

            logger?.LogDebug("Initial settings loaded, selected tab: {Tab}", tab);
            return tab;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to load initial settings");
            return NavigationTab.GameProfiles;
        }
    }

    /// <summary>
    /// Gets the collection of detected game installations.
    /// </summary>
    public ObservableCollection<string> GameInstallations { get; } = [];

    /// <summary>
    /// Gets the available navigation tabs.
    /// </summary>
    public NavigationTab[] AvailableTabs { get; } =
    [
        NavigationTab.GameProfiles,
        NavigationTab.Downloads,
        NavigationTab.Settings,
    ];

    /// <summary>
    /// Gets the current tab's ViewModel for ContentControl binding.
    /// </summary>
    public object CurrentTabViewModel => SelectedTab switch
    {
        NavigationTab.GameProfiles => GameProfilesViewModel,
        NavigationTab.Downloads => DownloadsViewModel,
        NavigationTab.Tools => ToolsViewModel,
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
        NavigationTab.Tools => "Tools",
        NavigationTab.Settings => "Settings",
        _ => tab.ToString(),
    };

    /// <summary>
    /// Selects the specified navigation tab.
    /// </summary>
    /// <param name="tab">The navigation tab to select.</param>
    [RelayCommand]
    public void SelectTab(NavigationTab tab)
    {
        SelectedTab = tab;
    }

    /// <summary>
    /// Performs asynchronous initialization for the shell and all tabs.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        await GameProfilesViewModel.InitializeAsync();
        await DownloadsViewModel.InitializeAsync();
        await ToolsViewModel.InitializeAsync();
        logger?.LogInformation("MainViewModel initialized");

        // Start background check with cancellation support
        _ = CheckForUpdatesInBackgroundAsync(_initializationCts.Token);
    }

    /// <summary>
    /// Disposes of managed resources.
    /// </summary>
    public void Dispose()
    {
        _initializationCts?.Cancel();
        _initializationCts?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Checks for available updates using Velopack.
    /// </summary>
    private async Task CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        logger?.LogDebug("Starting background update check");

        try
        {
            var settings = userSettingsService.Get();

            // Push settings to update manager (important context for other components)
            if (settings.SubscribedPrNumber.HasValue)
            {
                velopackUpdateManager.SubscribedPrNumber = settings.SubscribedPrNumber;
            }

            // 1. Check for standard GitHub releases (Default)
            if (string.IsNullOrEmpty(settings.SubscribedBranch))
            {
                var updateInfo = await velopackUpdateManager.CheckForUpdatesAsync(cancellationToken);
                if (updateInfo != null)
                {
                    logger?.LogInformation("GitHub release update available: {Version}", updateInfo.TargetFullRelease.Version);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        notificationService.Show(new NotificationMessage(
                            NotificationType.Info,
                            "Update Available",
                            $"A new version ({updateInfo.TargetFullRelease.Version}) is available.",
                            null, // Persistent
                            actions:
                            [
                                new NotificationAction(
                                    "View Updates",
                                    () => { SettingsViewModel.OpenUpdateWindowCommand.Execute(null); },
                                    NotificationActionStyle.Primary,
                                    dismissOnExecute: true),
                            ]));
                    });
                    return;
                }
            }
            else
            {
                // 2. Check for Subscribed Branch Artifacts
                logger?.LogDebug("User subscribed to branch '{Branch}', checking for artifact updates", settings.SubscribedBranch);
                velopackUpdateManager.SubscribedBranch = settings.SubscribedBranch;
                velopackUpdateManager.SubscribedPrNumber = null; // Clear PR to avoid ambiguity

                var artifactUpdate = await velopackUpdateManager.CheckForArtifactUpdatesAsync(cancellationToken);

                if (artifactUpdate != null)
                {
                    var newVersionBase = artifactUpdate.Version.Split('+')[0];

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        notificationService.Show(new NotificationMessage(
                            NotificationType.Info,
                            "Branch Update Available",
                            $"A new build ({newVersionBase}) is available on branch '{settings.SubscribedBranch}'.",
                            null, // Persistent
                            actions:
                            [
                                new(
                                    "View Updates",
                                    () => { SettingsViewModel.OpenUpdateWindowCommand.Execute(null); },
                                    NotificationActionStyle.Primary,
                                    dismissOnExecute: true),
                            ]));
                    });
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Exception in CheckForUpdatesAsync");
        }
    }

    private async Task CheckForUpdatesInBackgroundAsync(CancellationToken ct)
    {
        try
        {
            await CheckForUpdatesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Unhandled exception in background update check");
        }
    }

    private void SaveSelectedTab(NavigationTab selectedTab)
    {
        try
        {
            userSettingsService.Update(settings =>
            {
                settings.LastSelectedTab = selectedTab;
            });

            _ = userSettingsService.SaveAsync();
            logger?.LogDebug("Updated last selected tab to: {Tab}", selectedTab);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to update selected tab setting");
        }
    }

    partial void OnSelectedTabChanged(NavigationTab value)
    {
        OnPropertyChanged(nameof(CurrentTabViewModel));

        // Notify SettingsViewModel when it becomes visible/invisible
        SettingsViewModel.IsViewVisible = value == NavigationTab.Settings;

        // Refresh Tabs when they become visible
        if (value == NavigationTab.GameProfiles)
        {
            GameProfilesViewModel.OnTabActivated();
        }
        else if (value == NavigationTab.Downloads)
        {
            _ = DownloadsViewModel.OnTabActivatedAsync();
        }

        SaveSelectedTab(value);
    }
}
