using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Common.ViewModels.Dialogs;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Messages;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Info.ViewModels;
using GenHub.Features.Notifications.ViewModels;
using GenHub.Features.Online.ViewModels;
using GenHub.Features.Settings.ViewModels;
using GenHub.Features.Tools.ViewModels;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Common.ViewModels;

/// <summary>
/// Initializes a new instance of <see cref="MainViewModel"/> class.
/// </summary>
/// <param name="gameProfilesViewModel">Game profiles view model.</param>
/// <param name="downloadsBrowserViewModel">Downloads browser view model.</param>
/// <param name="toolsViewModel">Tools view model.</param>
/// <param name="settingsViewModel">Settings view model.</param>
/// <param name="notificationManager">Notification manager view model.</param>
/// <param name="configurationProvider">Configuration provider service.</param>
/// <param name="userSettingsService">User settings service for persistence operations.</param>
/// <param name="backgroundUpdateCoordinator">Coordinator for background update checking and scheduling.</param>
/// <param name="notificationService">Service for showing notifications.</param>
/// <param name="dialogService">Dialog service for showing message boxes.</param>
/// <param name="notificationFeedViewModel">Notification feed view model.</param>
/// <param name="infoViewModel">Info view model.</param>
/// <param name="onlineViewModel">Online view model.</param>
/// <param name="logger">Logger instance.</param>
/// <param name="localizationService">The optional localization service.</param>
[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "MainViewModel is the top-level composition ViewModel for tabs and services injected via dependency injection.")]
public partial class MainViewModel(
    GameProfileLauncherViewModel gameProfilesViewModel,
    DownloadsBrowserViewModel downloadsBrowserViewModel,
    ToolsViewModel toolsViewModel,
    SettingsViewModel settingsViewModel,
    NotificationManagerViewModel notificationManager,
    IConfigurationProviderService configurationProvider,
    IUserSettingsService userSettingsService,
    IBackgroundUpdateCoordinator backgroundUpdateCoordinator,
    INotificationService notificationService,
    IDialogService dialogService,
    NotificationFeedViewModel notificationFeedViewModel,
    InfoViewModel infoViewModel,
    OnlineViewModel onlineViewModel,
    ILogger<MainViewModel> logger,
    ILocalizationService? localizationService = null) : ObservableObject, IDisposable, IRecipient<NavigationMessage>
{
    private readonly CancellationTokenSource _initializationCts = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class for design-time support.
    /// </summary>
#pragma warning disable CS8625
    [Obsolete("Use DI constructor for runtime. This is only for XAML tools.")]
    public MainViewModel()
        : this(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null)
    {
    }
#pragma warning restore CS8625

    /// <summary>
    /// Gets the info view model.
    /// </summary>
    public InfoViewModel InfoViewModel { get; } = infoViewModel;

    /// <summary>
    /// Gets the online view model.
    /// </summary>
    public OnlineViewModel OnlineViewModel { get; } = onlineViewModel;

    /// <summary>
    /// Gets a value indicating whether the Online tab is enabled via feature flag.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Bound as an instance property from MainView.axaml; making it static would break the binding.")]
    public bool IsOnlineEnabled => OnlineConstants.IsOnlineEnabled;

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
    public DownloadsBrowserViewModel DownloadsBrowserViewModel { get; } = downloadsBrowserViewModel;

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
    /// Gets the collection of detected game installations.
    /// </summary>
    public ObservableCollection<string> GameInstallations { get; } = [];

    /// <summary>
    /// Gets the available navigation tabs.
    /// </summary>
    public IReadOnlyList<NavigationTab> AvailableTabs { get; } =
    [
        NavigationTab.GameProfiles,
        NavigationTab.Downloads,
        NavigationTab.Tools,
        NavigationTab.Online,
        NavigationTab.Info,
        NavigationTab.Settings,
    ];

    /// <summary>
    /// Gets the current tab's ViewModel for ContentControl binding.
    /// </summary>
    public object CurrentTabViewModel => SelectedTab switch
    {
        NavigationTab.GameProfiles => GameProfilesViewModel,
        NavigationTab.Downloads => DownloadsBrowserViewModel,
        NavigationTab.Tools => ToolsViewModel,
        NavigationTab.Settings => SettingsViewModel,
        NavigationTab.Info => InfoViewModel,
        NavigationTab.Online => OnlineViewModel,
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
        NavigationTab.Tools => "Tools",
        NavigationTab.Settings => "Settings",
        NavigationTab.Info => "Info",
        NavigationTab.Online => "Online",
        _ => tab.ToString(),
    };

    /// <inheritdoc/>
    public void Receive(NavigationMessage message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            SelectTab(message.Tab);
        }
        else
        {
            Dispatcher.UIThread.Post(() => SelectTab(message.Tab));
        }
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
        await DownloadsBrowserViewModel.InitializeAsync();
        await ToolsViewModel.InitializeAsync();
        await InfoViewModel.InitializeAsync();
        if (OnlineConstants.IsOnlineEnabled)
        {
            OnlineViewModel.Initialize();
        }

        logger?.LogInformation("MainViewModel initialized");

        // Ensure the initial tab's activation logic runs (triggers lazy loading)
        OnSelectedTabChanged(SelectedTab);

        await backgroundUpdateCoordinator.InitializeAsync(_initializationCts.Token);

        CheckForQuickStart();
        CheckPostUpdateAnnouncement();
    }

    /// <summary>
    /// Disposes of managed resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _initializationCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ignore if already disposed
        }

        _initializationCts.Dispose();
        WeakReferenceMessenger.Default.UnregisterAll(this);
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

            if (tab == NavigationTab.Online && !OnlineConstants.IsOnlineEnabled)
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

    private void RegisterMessages()
    {
        if (!WeakReferenceMessenger.Default.IsRegistered<NavigationMessage>(this))
        {
            WeakReferenceMessenger.Default.RegisterAll(this);
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
                        Text = localizationService?.GetString("GettingStarted.Action.OpenQuickstart") ?? "Open Quickstart",
                        Style = NotificationActionStyle.Primary,
                        Action = () =>
                        {
                            SelectTab(NavigationTab.Info);

                            // Programmatic navigation to the quickstart section
                            InfoViewModel.OpenSection("quickstart");
                        },
                    },
                    new DialogAction
                    {
                        Text = localizationService?.GetString("Common.Button.Close") ?? "Close",
                        Style = NotificationActionStyle.Secondary,
                    },
                };

                var content = localizationService?.GetString("GettingStarted.Content") ?? """
                **Welcome to GenHub!**

                Your modern, community-focused command center for **C&C: Generals & Zero Hour** is ready. The **Quickstart Guide** will help you get started with:

                *   Managing profiles
                *   Setting up downloads
                *   Adding your own mods and content
                """;

                var title = localizationService?.GetString("GettingStarted.Title") ?? "Getting Started";

                var result = await dialogService.ShowMessageAsync(
                    title,
                    content,
                    actions,
                    showDoNotAskAgain: true);

                if (result.DoNotAskAgain)
                {
                    userSettingsService.Update(s => s.HasSeenQuickStart = true);
                    _ = userSettingsService.SaveAsync(_initializationCts.Token);
                }
            });
        }
    }

    private void CheckPostUpdateAnnouncement()
    {
        var currentVersion = typeof(MainViewModel).Assembly.GetName().Version?.ToString(3);
        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            return;
        }

        var settings = userSettingsService.Get();
        var lastSeen = settings.LastSeenAppVersion;

        if (string.IsNullOrWhiteSpace(lastSeen))
        {
            userSettingsService.Update(s => s.LastSeenAppVersion = currentVersion);
            _ = userSettingsService.SaveAsync(_initializationCts.Token);
            return;
        }

        if (Version.TryParse(currentVersion, out var parsedCurrent) &&
            Version.TryParse(lastSeen, out var parsedLastSeen) &&
            parsedCurrent > parsedLastSeen)
        {
            userSettingsService.Update(s => s.LastSeenAppVersion = currentVersion);
            _ = userSettingsService.SaveAsync(_initializationCts.Token);

            // Changelogs describe official releases only, so CI artifact builds from
            // development branches and pull requests update the version stamp silently.
            if (AppConstants.IsCiBuild)
            {
                return;
            }

            var title = localizationService?.GetString("AppUpdate.PostUpdate.Title") ?? AppUpdateConstants.PostUpdateNotificationTitle;
            var message = localizationService?.GetString("AppUpdate.PostUpdate.Message", currentVersion) ??
                          string.Format(AppUpdateConstants.PostUpdateNotificationFormat, currentVersion);
            var viewChangelogText = localizationService?.GetString("AppUpdate.Action.ViewChangelog") ?? AppUpdateConstants.ViewChangelogAction;

            notificationService.Show(new NotificationMessage(
                NotificationType.Success,
                title,
                message,
                autoDismissMilliseconds: NotificationDurations.VeryLong,
                actions:
                [
                    new NotificationAction(
                        viewChangelogText,
                        () =>
                        {
                            SelectTab(NavigationTab.Info);
                            InfoViewModel.OpenSection(InfoConstants.SectionChangelogs);
                        },
                        NotificationActionStyle.Primary,
                        dismissOnExecute: true),
                ],
                isPersistent: false,
                showInBadge: true));
        }
        else if (!string.Equals(currentVersion, lastSeen, StringComparison.OrdinalIgnoreCase))
        {
            userSettingsService.Update(s => s.LastSeenAppVersion = currentVersion);
            _ = userSettingsService.SaveAsync(_initializationCts.Token);
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

            _ = userSettingsService.SaveAsync(CancellationToken.None);
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

        SettingsViewModel.IsViewVisible = value == NavigationTab.Settings;

        if (value == NavigationTab.GameProfiles)
        {
            GameProfilesViewModel.OnTabActivated();
        }
        else if (value == NavigationTab.Downloads)
        {
            _ = DownloadsBrowserViewModel.OnTabActivatedAsync();
        }
        else if (value == NavigationTab.Tools)
        {
            ToolsViewModel.OnTabActivated();
            ToolsViewModel.IsPaneOpen = true;
        }
        else if (value == NavigationTab.Info)
        {
            InfoViewModel.IsPaneOpen = true;
        }
        else if (value == NavigationTab.Online && OnlineConstants.IsOnlineEnabled)
        {
            _ = OnlineViewModel.RefreshNetworksAsync(_initializationCts.Token);
        }

        SaveSelectedTab(value);
    }

    /// <summary>
    /// Copies the application version to the clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopyVersionToClipboard()
    {
        try
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var mainWindow = lifetime?.MainWindow;
            var topLevel = mainWindow is not null ? TopLevel.GetTopLevel(mainWindow) : null;

            if (topLevel?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(AppConstants.FullDisplayVersion);
                notificationService.ShowSuccess("Copied", "Version copied to clipboard.", 3000);
            }
            else
            {
                notificationService.ShowError("Error", "Clipboard not available.", 3000);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to copy version to clipboard");
            notificationService.ShowError("Error", "Failed to copy version to clipboard.", 3000);
        }
    }
}
