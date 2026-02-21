using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Common.ViewModels.Dialogs;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Messages;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.GameReplays.ViewModels;
using GenHub.Features.Info.ViewModels;
using GenHub.Features.Notifications.ViewModels;
using GenHub.Features.Settings.ViewModels;
using GenHub.Features.Tools.ViewModels;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.ViewModels;

/// <summary>
/// Initializes a new instance of <see cref="MainViewModel"/> class.
/// </summary>
/// <param name="gameProfilesViewModel">Game profiles view model.</param>
/// <param name="downloadsViewModel">Downloads view model.</param>
/// <param name="toolsViewModel">Tools view model.</param>
/// <param name="settingsViewModel">Settings view model.</param>
/// <param name="notificationManager">Notification manager view model.</param>
/// <param name="gameReplaysViewModel">GameReplays view model.</param>
/// <param name="configurationProvider">Configuration provider service.</param>
/// <param name="userSettingsService">User settings service for persistence operations.</param>
/// <param name="velopackUpdateManager">The Velopack update manager for checking updates.</param>
/// <param name="notificationService">Service for showing notifications.</param>
/// <param name="dialogService">Dialog service for showing message boxes.</param>
/// <param name="notificationFeedViewModel">Notification feed view model.</param>
/// <param name="infoViewModel">Info view model.</param>
/// <param name="logger">Logger instance.</param>
public partial class MainViewModel(
    GameProfileLauncherViewModel gameProfilesViewModel,
    DownloadsViewModel downloadsViewModel,
    ToolsViewModel toolsViewModel,
    SettingsViewModel settingsViewModel,
    NotificationManagerViewModel notificationManager,
    GameReplaysViewModel gameReplaysViewModel,
    IConfigurationProviderService configurationProvider,
    IUserSettingsService userSettingsService,
    IVelopackUpdateManager velopackUpdateManager,
    INotificationService notificationService,
    IDialogService dialogService,
    NotificationFeedViewModel notificationFeedViewModel,
    InfoViewModel infoViewModel,
    ILogger<MainViewModel> logger) : ObservableObject, IDisposable, IRecipient<NavigationMessage>
{
    private readonly CancellationTokenSource _initializationCts = new();

    /// <summary>
    /// Gets the collection of detected game installations.
    /// </summary>
    public ObservableCollection<string> GameInstallations { get; } = [];

    /// <summary>
    /// Gets the info view model.
    /// </summary>
    public InfoViewModel InfoViewModel { get; } = infoViewModel;

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

    /// <summary>
    /// Gets the GameReplays view model.
    /// </summary>
    public GameReplaysViewModel GameReplaysViewModel { get; } = gameReplaysViewModel;

    /// <summary>
    /// Gets the available navigation tabs.
    /// </summary>
    public NavigationTab[] AvailableTabs { get; } =
    [
        NavigationTab.GameProfiles,
        NavigationTab.Downloads,
        NavigationTab.GameReplays,
        NavigationTab.Tools,
        NavigationTab.Settings,
        NavigationTab.Info,
    ];

    /// <summary>
    /// Gets the current tab's ViewModel for ContentControl binding.
    /// </summary>
    public object CurrentTabViewModel => SelectedTab switch
    {
        NavigationTab.GameProfiles => GameProfilesViewModel,
        NavigationTab.Downloads => DownloadsViewModel,
        NavigationTab.GameReplays => GameReplaysViewModel,
        NavigationTab.Tools => ToolsViewModel,
        NavigationTab.Settings => SettingsViewModel,
        NavigationTab.Info => InfoViewModel,
        _ => GameProfilesViewModel,
    };

    [ObservableProperty]
    private NavigationTab _selectedTab = LoadInitialTab(configurationProvider, logger);

    /// <summary>
    /// Gets the display name for a navigation tab.
    /// </summary>
    /// <param name="tab">The navigation tab.</param>
    /// <returns>The display name.</returns>
    public static string GetTabDisplayName(NavigationTab tab) => tab switch
    {
        NavigationTab.GameProfiles => "Game Profiles",
        NavigationTab.Downloads => "Downloads",
        NavigationTab.GameReplays => "Game Replays",
        NavigationTab.Tools => "Tools",
        NavigationTab.Settings => "Settings",
        NavigationTab.Info => "Info",
        _ => tab.ToString(),
    };

    /// <inheritdoc/>
    public void Receive(NavigationMessage message)
    {
        Dispatcher.UIThread.Post(() => SelectTab(message.Tab));
    }

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
        RegisterMessages();
        await GameProfilesViewModel.InitializeAsync();
        await DownloadsViewModel.InitializeAsync();
        await ToolsViewModel.InitializeAsync();
        await GameReplaysViewModel.InitializeAsync();
        await InfoViewModel.InitializeAsync();
        logger?.LogInformation("MainViewModel initialized");

        // Start background check with cancellation support
        _ = CheckForUpdatesInBackgroundAsync(_initializationCts.Token);

        CheckForQuickStart();
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

    // Register for messages
    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register(this);
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
                                new NotificationAction(
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

    private void CheckForQuickStart()
    {
        var settings = userSettingsService.Get();
        if (!settings.HasSeenQuickStart)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                var actions = new[]
                {
                    new DialogAction
                    {
                        Text = "Open Quickstart",
                        Style = NotificationActionStyle.Primary, // Switched to Primary (Purple)
                        Action = () =>
                        {
                             SelectTab(NavigationTab.Info);

                             // Programmatic navigation to the quickstart section
                             InfoViewModel.OpenSection("quickstart");
                        },
                    },
                    new DialogAction
                    {
                        Text = "Close",
                        Style = NotificationActionStyle.Secondary,
                    },
                };

                var content = """
                **Welcome to GenHub!**

                Your modern, community-focused command center for **C&C: Generals & Zero Hour** is ready. The **Quickstart Guide** will help you get started with:

                *   Managing profiles
                *   Setting up downloads
                *   Adding your own mods and content
                """;

                var result = await dialogService.ShowMessageAsync(
                    "Getting Started",
                    content,
                    actions,
                    showDoNotAskAgain: true);

                if (result.DoNotAskAgain)
                {
                    userSettingsService.Update(s => s.HasSeenQuickStart = true);
                    _ = userSettingsService.SaveAsync();
                }
            });
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

        // Log the current tab view model type
        var viewModelType = CurrentTabViewModel?.GetType().Name ?? "null";
        logger?.LogInformation("Switching to tab {Tab}. ViewModel Type: {ViewModelType}", value, viewModelType);

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
        else if (value == NavigationTab.GameReplays)
        {
            _ = GameReplaysViewModel.OnTabActivatedAsync();
        }
        else if (value == NavigationTab.Tools)
        {
            ToolsViewModel.IsPaneOpen = true;
        }
        else if (value == NavigationTab.Info)
        {
            InfoViewModel.IsPaneOpen = true;
        }

        SaveSelectedTab(value);
    }
}
