using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Common.ViewModels.Dialogs;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Messages;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.AppUpdate.ViewModels;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Info.ViewModels;
using GenHub.Features.Notifications.ViewModels;
using GenHub.Features.Settings.ViewModels;
using GenHub.Features.Tools.ViewModels;
using Microsoft.Extensions.Logging;
using Velopack;

namespace GenHub.Common.ViewModels;

/// <summary>
/// Initializes a new instance of <see cref="MainViewModel"/> class.
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
    IConfigurationProviderService configurationProvider,
    IUserSettingsService userSettingsService,
    IVelopackUpdateManager velopackUpdateManager,
    INotificationService notificationService,
    IDialogService dialogService,
    NotificationFeedViewModel notificationFeedViewModel,
    InfoViewModel infoViewModel,
    ILogger<MainViewModel> logger) : ObservableObject, IDisposable, IRecipient<NavigationMessage>, IRecipient<UpdateSettingsChangedMessage>
{
    private readonly CancellationTokenSource _initializationCts = new();
    private Timer? _periodicUpdateTimer;
    private string? _lastNotifiedUpdateIdentity;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class for design-time support.
    /// </summary>
    [Obsolete("Use DI constructor for runtime. This is only for XAML tools.")]
    public MainViewModel()
        : this(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!)
    {
    }

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
        NavigationTab.Info,
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

    /// <inheritdoc/>
    public void Receive(UpdateSettingsChangedMessage message)
    {
        RestartPeriodicUpdateTimer(message.AutoCheckForUpdatesPeriodically, message.PeriodicUpdateCheckIntervalMinutes);
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
        await InfoViewModel.InitializeAsync();
        logger?.LogInformation("MainViewModel initialized");

        var settings = userSettingsService.Get();
        if (settings.AutoCheckForUpdatesOnStartup)
        {
            // Start background check with cancellation support
            _ = CheckForUpdatesInBackgroundAsync(_initializationCts.Token);
        }

        // Initialize periodic update timer
        RestartPeriodicUpdateTimer(settings.AutoCheckForUpdatesPeriodically, settings.PeriodicUpdateCheckIntervalMinutes);

        CheckForQuickStart();
    }

    /// <summary>
    /// Disposes of managed resources.
    /// </summary>
    public void Dispose()
    {
        _periodicUpdateTimer?.Dispose();
        _initializationCts?.Cancel();
        _initializationCts?.Dispose();
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
        if (!WeakReferenceMessenger.Default.IsRegistered<NavigationMessage>(this))
        {
            WeakReferenceMessenger.Default.RegisterAll(this);
        }
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

            // 1. check for subscribed pr artifacts
            if (settings.SubscribedPrNumber.HasValue)
            {
                await CheckSubscribedPrUpdateAsync(settings.SubscribedPrNumber.Value, settings, cancellationToken);
                return;
            }

            // 2. check for subscribed branch artifacts
            if (!string.IsNullOrWhiteSpace(settings.SubscribedBranch))
            {
                await CheckSubscribedBranchUpdateAsync(settings.SubscribedBranch, settings, cancellationToken);
                return;
            }

            // 3. check for standard github releases
            await CheckStandardReleaseUpdateAsync(settings, cancellationToken);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Exception in CheckForUpdatesAsync");
        }
    }

    private async Task CheckSubscribedPrUpdateAsync(int prNumber, UserSettings settings, CancellationToken cancellationToken)
    {
        logger?.LogDebug("User subscribed to PR #{PrNumber}, checking for artifact updates", prNumber);
        velopackUpdateManager.SubscribedPrNumber = prNumber;
        velopackUpdateManager.SubscribedBranch = null;

        var artifactUpdate = await velopackUpdateManager.CheckForArtifactUpdatesAsync(cancellationToken);
        if (artifactUpdate != null)
        {
            var currentVersionBase = UpdateNotificationViewModel.CurrentAppVersion.Split('+')[0];
            var artifactVersionBase = artifactUpdate.Version.Split('+')[0];

            if (AppUpdateVersionHelper.IsArtifactVersionNewer(artifactVersionBase, currentVersionBase) &&
                !string.Equals(artifactVersionBase, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
            {
                var updateIdentity = $"pr:{prNumber}:{artifactVersionBase}";
                if (string.Equals(_lastNotifiedUpdateIdentity, updateIdentity, StringComparison.Ordinal))
                {
                    logger?.LogDebug("Update notification already shown for {Identity}, skipping duplicate notification", updateIdentity);
                    return;
                }

                _lastNotifiedUpdateIdentity = updateIdentity;
                logger?.LogInformation("PR #{PrNumber} update available: {Version}", prNumber, artifactUpdate.DisplayVersion);
                notificationService.Show(new NotificationMessage(
                    NotificationType.Info,
                    AppUpdateConstants.PrUpdateAvailableNotificationTitle,
                    string.Format(AppUpdateConstants.PrUpdateNotificationFormat, artifactUpdate.DisplayVersion, prNumber),
                    autoDismissMilliseconds: null,
                    actions:
                    [
                        new NotificationAction(
                            AppUpdateConstants.UpdateAction,
                            () => _ = PerformOneClickUpdateAsync(artifactUpdate, null, null),
                            NotificationActionStyle.Primary,
                            dismissOnExecute: true),
                    ],
                    isPersistent: true,
                    showInBadge: true));
            }

            return;
        }

        if (velopackUpdateManager.IsPrMergedOrClosed)
        {
            await CheckPrMergedFallbackUpdateAsync(prNumber, settings, cancellationToken);
        }
    }

    private async Task CheckPrMergedFallbackUpdateAsync(int prNumber, UserSettings settings, CancellationToken cancellationToken)
    {
        logger?.LogInformation("Subscribed PR #{PrNumber} is merged or closed. Checking development/release fallback", prNumber);
        var currentVersionBase = UpdateNotificationViewModel.CurrentAppVersion.Split('+')[0];

        velopackUpdateManager.SubscribedPrNumber = null;
        velopackUpdateManager.SubscribedBranch = AppUpdateConstants.DevelopmentBranch;

        var devArtifact = await velopackUpdateManager.CheckForArtifactUpdatesAsync(cancellationToken);
        if (devArtifact != null)
        {
            var devVersionBase = devArtifact.Version.Split('+')[0];
            if (AppUpdateVersionHelper.IsArtifactVersionNewer(devVersionBase, currentVersionBase) &&
                !string.Equals(devVersionBase, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
            {
                var updateIdentity = $"pr-fallback:{prNumber}:dev:{devVersionBase}";
                if (string.Equals(_lastNotifiedUpdateIdentity, updateIdentity, StringComparison.Ordinal))
                {
                    logger?.LogDebug("Update notification already shown for {Identity}, skipping duplicate notification", updateIdentity);
                    return;
                }

                _lastNotifiedUpdateIdentity = updateIdentity;
                logger?.LogInformation("PR #{PrNumber} merged. Development fallback update available: {Version}", prNumber, devArtifact.DisplayVersion);
                notificationService.Show(new NotificationMessage(
                    NotificationType.Info,
                    AppUpdateConstants.PrMergedUpdateAvailableNotificationTitle,
                    string.Format(AppUpdateConstants.PrMergedUpdateNotificationFormat, devArtifact.DisplayVersion, prNumber),
                    autoDismissMilliseconds: null,
                    actions:
                    [
                        new NotificationAction(
                            AppUpdateConstants.UpdateAction,
                            () => _ = PerformOneClickUpdateWithSubscriptionClearAsync(devArtifact, null, null, prNumber, null),
                            NotificationActionStyle.Primary,
                            dismissOnExecute: true),
                    ],
                    isPersistent: true,
                    showInBadge: true));
                return;
            }
        }

        // Fallback to standard releases if dev artifact was not newer or unavailable
        velopackUpdateManager.SubscribedBranch = null;
        var releaseUpdate = await velopackUpdateManager.CheckForUpdatesAsync(cancellationToken);
        if (releaseUpdate != null)
        {
            var version = releaseUpdate.TargetFullRelease.Version.ToString();
            if (!string.Equals(version, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
            {
                var updateIdentity = $"pr-fallback:{prNumber}:release:{version}";
                if (string.Equals(_lastNotifiedUpdateIdentity, updateIdentity, StringComparison.Ordinal))
                {
                    logger?.LogDebug("Update notification already shown for {Identity}, skipping duplicate notification", updateIdentity);
                    return;
                }

                _lastNotifiedUpdateIdentity = updateIdentity;
                logger?.LogInformation("PR #{PrNumber} merged. Release fallback update available: {Version}", prNumber, version);
                notificationService.Show(new NotificationMessage(
                    NotificationType.Info,
                    AppUpdateConstants.PrMergedUpdateAvailableNotificationTitle,
                    string.Format(AppUpdateConstants.PrMergedReleaseNotificationFormat, version, prNumber),
                    autoDismissMilliseconds: null,
                    actions:
                    [
                        new NotificationAction(
                            AppUpdateConstants.UpdateAction,
                            () => _ = PerformOneClickUpdateWithSubscriptionClearAsync(null, releaseUpdate, null, prNumber, null),
                            NotificationActionStyle.Primary,
                            dismissOnExecute: true),
                    ],
                    isPersistent: true,
                    showInBadge: true));
            }
        }
    }

    private async Task CheckSubscribedBranchUpdateAsync(string branch, UserSettings settings, CancellationToken cancellationToken)
    {
        logger?.LogDebug("User subscribed to branch '{Branch}', checking for artifact updates", branch);
        velopackUpdateManager.SubscribedBranch = branch;
        velopackUpdateManager.SubscribedPrNumber = null;

        var artifactUpdate = await velopackUpdateManager.CheckForArtifactUpdatesAsync(cancellationToken);
        if (artifactUpdate != null)
        {
            var currentVersionBase = UpdateNotificationViewModel.CurrentAppVersion.Split('+')[0];
            var artifactVersionBase = artifactUpdate.Version.Split('+')[0];

            if (AppUpdateVersionHelper.IsArtifactVersionNewer(artifactVersionBase, currentVersionBase) &&
                !string.Equals(artifactVersionBase, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
            {
                var updateIdentity = $"branch:{branch}:{artifactVersionBase}";
                if (string.Equals(_lastNotifiedUpdateIdentity, updateIdentity, StringComparison.Ordinal))
                {
                    logger?.LogDebug("Update notification already shown for {Identity}, skipping duplicate notification", updateIdentity);
                    return;
                }

                _lastNotifiedUpdateIdentity = updateIdentity;
                logger?.LogInformation("Branch '{Branch}' update available: {Version}", branch, artifactUpdate.DisplayVersion);
                notificationService.Show(new NotificationMessage(
                    NotificationType.Info,
                    AppUpdateConstants.BranchUpdateAvailableNotificationTitle,
                    string.Format(AppUpdateConstants.BranchUpdateNotificationFormat, artifactUpdate.DisplayVersion, branch),
                    autoDismissMilliseconds: null,
                    actions:
                    [
                        new NotificationAction(
                            AppUpdateConstants.UpdateAction,
                            () => _ = PerformOneClickUpdateAsync(artifactUpdate, null, null),
                            NotificationActionStyle.Primary,
                            dismissOnExecute: true),
                    ],
                    isPersistent: true,
                    showInBadge: true));
            }

            return;
        }

        if (!string.Equals(branch, AppUpdateConstants.DevelopmentBranch, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(branch, AppUpdateConstants.MainBranch, StringComparison.OrdinalIgnoreCase))
        {
            await CheckStaleBranchFallbackUpdateAsync(branch, settings, cancellationToken);
        }
    }

    private async Task CheckStaleBranchFallbackUpdateAsync(string branch, UserSettings settings, CancellationToken cancellationToken)
    {
        logger?.LogInformation("Subscribed branch '{Branch}' has no artifacts. Checking development/release fallback", branch);
        var currentVersionBase = UpdateNotificationViewModel.CurrentAppVersion.Split('+')[0];

        velopackUpdateManager.SubscribedBranch = AppUpdateConstants.DevelopmentBranch;
        var devArtifact = await velopackUpdateManager.CheckForArtifactUpdatesAsync(cancellationToken);

        if (devArtifact != null)
        {
            var devVersionBase = devArtifact.Version.Split('+')[0];
            if (AppUpdateVersionHelper.IsArtifactVersionNewer(devVersionBase, currentVersionBase) &&
                !string.Equals(devVersionBase, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
            {
                var updateIdentity = $"branch-fallback:{branch}:dev:{devVersionBase}";
                if (string.Equals(_lastNotifiedUpdateIdentity, updateIdentity, StringComparison.Ordinal))
                {
                    logger?.LogDebug("Update notification already shown for {Identity}, skipping duplicate notification", updateIdentity);
                    return;
                }

                _lastNotifiedUpdateIdentity = updateIdentity;
                logger?.LogInformation("Branch '{Branch}' stale. Development fallback update available: {Version}", branch, devArtifact.DisplayVersion);
                notificationService.Show(new NotificationMessage(
                    NotificationType.Info,
                    AppUpdateConstants.BranchStaleUpdateAvailableNotificationTitle,
                    string.Format(AppUpdateConstants.BranchStaleUpdateNotificationFormat, devArtifact.DisplayVersion, branch),
                    autoDismissMilliseconds: null,
                    actions:
                    [
                        new NotificationAction(
                            AppUpdateConstants.UpdateAction,
                            () => _ = PerformOneClickUpdateWithSubscriptionClearAsync(devArtifact, null, null, null, branch),
                            NotificationActionStyle.Primary,
                            dismissOnExecute: true),
                    ],
                    isPersistent: true,
                    showInBadge: true));
                return;
            }
        }

        // Fallback to release
        velopackUpdateManager.SubscribedBranch = null;
        var releaseUpdate = await velopackUpdateManager.CheckForUpdatesAsync(cancellationToken);
        if (releaseUpdate != null)
        {
            var version = releaseUpdate.TargetFullRelease.Version.ToString();
            if (!string.Equals(version, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
            {
                var updateIdentity = $"branch-fallback:{branch}:release:{version}";
                if (string.Equals(_lastNotifiedUpdateIdentity, updateIdentity, StringComparison.Ordinal))
                {
                    logger?.LogDebug("Update notification already shown for {Identity}, skipping duplicate notification", updateIdentity);
                    return;
                }

                _lastNotifiedUpdateIdentity = updateIdentity;
                logger?.LogInformation("Branch '{Branch}' stale. Release fallback update available: {Version}", branch, version);
                notificationService.Show(new NotificationMessage(
                    NotificationType.Info,
                    AppUpdateConstants.BranchStaleUpdateAvailableNotificationTitle,
                    string.Format(AppUpdateConstants.BranchStaleReleaseNotificationFormat, version, branch),
                    autoDismissMilliseconds: null,
                    actions:
                    [
                        new NotificationAction(
                            AppUpdateConstants.UpdateAction,
                            () => _ = PerformOneClickUpdateWithSubscriptionClearAsync(null, releaseUpdate, null, null, branch),
                            NotificationActionStyle.Primary,
                            dismissOnExecute: true),
                    ],
                    isPersistent: true,
                    showInBadge: true));
            }
        }
    }

    private async Task CheckStandardReleaseUpdateAsync(UserSettings settings, CancellationToken cancellationToken)
    {
        velopackUpdateManager.SubscribedPrNumber = null;
        velopackUpdateManager.SubscribedBranch = null;

        var updateInfo = await velopackUpdateManager.CheckForUpdatesAsync(cancellationToken);
        if (updateInfo != null)
        {
            var version = updateInfo.TargetFullRelease.Version.ToString();
            if (!string.Equals(version, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
            {
                var updateIdentity = $"release:{version}";
                if (string.Equals(_lastNotifiedUpdateIdentity, updateIdentity, StringComparison.Ordinal))
                {
                    logger?.LogDebug("Update notification already shown for {Identity}, skipping duplicate notification", updateIdentity);
                    return;
                }

                _lastNotifiedUpdateIdentity = updateIdentity;
                logger?.LogInformation("GitHub release update available: {Version}", version);
                notificationService.Show(new NotificationMessage(
                    NotificationType.Info,
                    AppUpdateConstants.UpdateAvailableNotificationTitle,
                    string.Format(AppUpdateConstants.ReleaseUpdateNotificationFormat, version),
                    autoDismissMilliseconds: null,
                    actions:
                    [
                        new NotificationAction(
                            AppUpdateConstants.UpdateAction,
                            () => _ = PerformOneClickUpdateAsync(null, updateInfo, null),
                            NotificationActionStyle.Primary,
                            dismissOnExecute: true),
                    ],
                    isPersistent: true,
                    showInBadge: true));
            }

            return;
        }

        if (velopackUpdateManager.HasUpdateAvailableFromGitHub)
        {
            var githubVersion = velopackUpdateManager.LatestVersionFromGitHub;
            if (!string.IsNullOrWhiteSpace(githubVersion) &&
                !string.Equals(githubVersion, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
            {
                var updateIdentity = $"github:{githubVersion}";
                if (string.Equals(_lastNotifiedUpdateIdentity, updateIdentity, StringComparison.Ordinal))
                {
                    logger?.LogDebug("Update notification already shown for {Identity}, skipping duplicate notification", updateIdentity);
                    return;
                }

                _lastNotifiedUpdateIdentity = updateIdentity;
                logger?.LogInformation("GitHub API release update available: {Version}", githubVersion);
                notificationService.Show(new NotificationMessage(
                    NotificationType.Info,
                    AppUpdateConstants.UpdateAvailableNotificationTitle,
                    string.Format(AppUpdateConstants.ReleaseUpdateNotificationFormat, githubVersion),
                    autoDismissMilliseconds: null,
                    actions:
                    [
                        new NotificationAction(
                            AppUpdateConstants.UpdateAction,
                            () => _ = PerformOneClickUpdateAsync(null, null, githubVersion),
                            NotificationActionStyle.Primary,
                            dismissOnExecute: true),
                    ],
                    isPersistent: true,
                    showInBadge: true));
            }
        }
    }

    private async Task PerformOneClickUpdateWithSubscriptionClearAsync(
        ArtifactUpdateInfo? artifactUpdate,
        UpdateInfo? updateInfo,
        string? githubVersion,
        int? clearedPrNumber,
        string? clearedBranch)
    {
        try
        {
            userSettingsService.Update(settings =>
            {
                if (clearedPrNumber.HasValue && settings.SubscribedPrNumber == clearedPrNumber.Value)
                {
                    settings.SubscribedPrNumber = null;
                }

                if (!string.IsNullOrEmpty(clearedBranch) &&
                    string.Equals(settings.SubscribedBranch, clearedBranch, StringComparison.OrdinalIgnoreCase))
                {
                    settings.SubscribedBranch = null;
                }
            });
            await userSettingsService.SaveAsync();
            logger?.LogInformation(
                "Cleared stale subscription (PR: {PrNumber}, Branch: {Branch}) before applying fallback update",
                clearedPrNumber,
                clearedBranch);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to clear stale subscription settings before applying fallback update");
        }

        await PerformOneClickUpdateAsync(artifactUpdate, updateInfo, githubVersion);
    }

    private async Task PerformOneClickUpdateAsync(
        ArtifactUpdateInfo? artifactUpdate,
        UpdateInfo? updateInfo,
        string? githubVersion)
    {
        var progressNotificationId = Guid.NewGuid();

        // show the progress notification immediately
        notificationService.Show(new NotificationMessage(
            NotificationType.Info,
            AppUpdateConstants.UpdatingAppNotificationTitle,
            AppUpdateConstants.UpdateStartingMessage,
            autoDismissMilliseconds: null,
            isPersistent: false,
            showInBadge: false)
        {
            Id = progressNotificationId,
        });

        var progress = new Progress<UpdateProgress>(p =>
        {
            string statusText;
            if (!string.IsNullOrWhiteSpace(p.Message))
            {
                statusText = p.Message;
            }
            else if (!string.IsNullOrWhiteSpace(p.Status))
            {
                statusText = p.Status;
            }
            else
            {
                statusText = $"{p.PercentComplete}%";
            }

            notificationService.Update(
                progressNotificationId,
                statusText,
                AppUpdateConstants.UpdatingAppNotificationTitle);
        });

        try
        {
            if (artifactUpdate != null)
            {
                logger?.LogInformation("Starting one-click artifact install: {Version}", artifactUpdate.DisplayVersion);
                await velopackUpdateManager.InstallArtifactAsync(artifactUpdate, progress, _initializationCts.Token);
                notificationService.Update(
                    progressNotificationId,
                    AppUpdateConstants.UpdateCompleteRestartingMessage,
                    AppUpdateConstants.UpdatingAppNotificationTitle);
            }
            else if (updateInfo != null)
            {
                logger?.LogInformation("Starting one-click release update: {Version}", updateInfo.TargetFullRelease.Version);
                await velopackUpdateManager.DownloadUpdatesAsync(updateInfo, progress, _initializationCts.Token);
                notificationService.Update(
                    progressNotificationId,
                    AppUpdateConstants.UpdateDownloadedRestartingMessage,
                    AppUpdateConstants.UpdatingAppNotificationTitle);
                velopackUpdateManager.ApplyUpdatesAndRestart(updateInfo);
            }
            else if (!string.IsNullOrWhiteSpace(githubVersion))
            {
                logger?.LogInformation("Opening update window for GitHub API update: {Version}", githubVersion);
                notificationService.Dismiss(progressNotificationId);
                OpenUpdateSettings();
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to install update");
            notificationService.Dismiss(progressNotificationId);
            notificationService.ShowError(
                AppUpdateConstants.UpdateFailedNotificationTitle,
                string.Format(AppUpdateConstants.UpdateFailedNotificationFormat, ex.Message),
                autoDismissMs: NotificationConstants.DefaultAutoDismissMs);
        }
    }

    private void OpenUpdateSettings()
    {
        SelectTab(NavigationTab.Settings);
        if (Dispatcher.UIThread.CheckAccess())
        {
            SettingsViewModel.OpenUpdateWindowCommand.Execute(null);
        }
        else
        {
            Dispatcher.UIThread.Post(() => SettingsViewModel.OpenUpdateWindowCommand.Execute(null));
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

    private void RestartPeriodicUpdateTimer(bool enabled, int intervalMinutes)
    {
        _periodicUpdateTimer?.Dispose();
        _periodicUpdateTimer = null;

        if (!enabled || intervalMinutes <= 0)
        {
            return;
        }

        var clampedInterval = Math.Clamp(
            intervalMinutes,
            AppUpdateConstants.MinPeriodicUpdateCheckIntervalMinutes,
            AppUpdateConstants.MaxPeriodicUpdateCheckIntervalMinutes);

        var interval = TimeSpan.FromMinutes(clampedInterval);
        logger?.LogDebug("Starting periodic update check timer with interval: {Interval}", interval);

        _periodicUpdateTimer = new Timer(
            OnPeriodicUpdateTimerCallback,
            null,
            interval,
            interval);
    }

    private void OnPeriodicUpdateTimerCallback(object? state)
    {
        if (_initializationCts.IsCancellationRequested)
        {
            return;
        }

        logger?.LogDebug("Periodic update check timer triggered");
        _ = CheckForUpdatesInBackgroundAsync(_initializationCts.Token);
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
