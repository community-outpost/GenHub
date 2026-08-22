using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Messages;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.AppUpdate.ViewModels;
using GenHub.Features.AppUpdate.Views;
using Microsoft.Extensions.Logging;
using Velopack;

namespace GenHub.Features.AppUpdate.Services;

/// <summary>
/// Coordinates background app update checks, scheduled periodic checks, fallback discovery, and one-click installation.
/// </summary>
/// <param name="velopackUpdateManager">The Velopack update manager for checking updates.</param>
/// <param name="userSettingsService">User settings service for persistence operations.</param>
/// <param name="notificationService">Service for showing notifications.</param>
/// <param name="logger">Logger instance.</param>
public class BackgroundUpdateCoordinator(
    IVelopackUpdateManager velopackUpdateManager,
    IUserSettingsService userSettingsService,
    INotificationService notificationService,
    ILogger<BackgroundUpdateCoordinator> logger) : IBackgroundUpdateCoordinator, IRecipient<UpdateSettingsChangedMessage>
{
    private readonly CancellationTokenSource _cts = new();
    private Timer? _periodicUpdateTimer;
    private string? _lastNotifiedUpdateIdentity;

    /// <inheritdoc/>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        RegisterMessages();

        var settings = userSettingsService.Get();
        if (settings.AutoCheckForUpdatesOnStartup)
        {
            _ = CheckForUpdatesInBackgroundAsync(_cts.Token);
        }

        RestartPeriodicUpdateTimer(settings.AutoCheckForUpdatesPeriodically, settings.PeriodicUpdateCheckIntervalMinutes);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task CheckForUpdatesAsync(CancellationToken cancellationToken = default)
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

    /// <inheritdoc/>
    public void Receive(UpdateSettingsChangedMessage message)
    {
        RestartPeriodicUpdateTimer(message.AutoCheckForUpdatesPeriodically, message.PeriodicUpdateCheckIntervalMinutes);
    }

    private bool _disposed;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ignore if already disposed
        }

        _periodicUpdateTimer?.Dispose();
        _cts.Dispose();
        WeakReferenceMessenger.Default.UnregisterAll(this);
        GC.SuppressFinalize(this);
    }

    private void RegisterMessages()
    {
        if (!WeakReferenceMessenger.Default.IsRegistered<UpdateSettingsChangedMessage>(this))
        {
            WeakReferenceMessenger.Default.RegisterAll(this);
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
        else if (velopackUpdateManager.HasUpdateAvailableFromGitHub)
        {
            var githubVersion = velopackUpdateManager.LatestVersionFromGitHub;
            if (!string.IsNullOrWhiteSpace(githubVersion) &&
                !string.Equals(githubVersion, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
            {
                var updateIdentity = $"pr-fallback:{prNumber}:github:{githubVersion}";
                if (string.Equals(_lastNotifiedUpdateIdentity, updateIdentity, StringComparison.Ordinal))
                {
                    logger?.LogDebug("Update notification already shown for {Identity}, skipping duplicate notification", updateIdentity);
                    return;
                }

                _lastNotifiedUpdateIdentity = updateIdentity;
                logger?.LogInformation("PR #{PrNumber} merged. GitHub API release fallback available: {Version}", prNumber, githubVersion);
                notificationService.Show(new NotificationMessage(
                    NotificationType.Info,
                    AppUpdateConstants.PrMergedUpdateAvailableNotificationTitle,
                    string.Format(AppUpdateConstants.PrMergedReleaseNotificationFormat, githubVersion, prNumber),
                    autoDismissMilliseconds: null,
                    actions:
                    [
                        new NotificationAction(
                            AppUpdateConstants.UpdateAction,
                            () => _ = PerformOneClickUpdateWithSubscriptionClearAsync(null, null, githubVersion, prNumber, null),
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
        else if (velopackUpdateManager.HasUpdateAvailableFromGitHub)
        {
            var githubVersion = velopackUpdateManager.LatestVersionFromGitHub;
            if (!string.IsNullOrWhiteSpace(githubVersion) &&
                !string.Equals(githubVersion, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
            {
                var updateIdentity = $"branch-fallback:{branch}:github:{githubVersion}";
                if (string.Equals(_lastNotifiedUpdateIdentity, updateIdentity, StringComparison.Ordinal))
                {
                    logger?.LogDebug("Update notification already shown for {Identity}, skipping duplicate notification", updateIdentity);
                    return;
                }

                _lastNotifiedUpdateIdentity = updateIdentity;
                logger?.LogInformation("Branch '{Branch}' stale. GitHub API release fallback available: {Version}", branch, githubVersion);
                notificationService.Show(new NotificationMessage(
                    NotificationType.Info,
                    AppUpdateConstants.BranchStaleUpdateAvailableNotificationTitle,
                    string.Format(AppUpdateConstants.BranchStaleReleaseNotificationFormat, githubVersion, branch),
                    autoDismissMilliseconds: null,
                    actions:
                    [
                        new NotificationAction(
                            AppUpdateConstants.UpdateAction,
                            () => _ = PerformOneClickUpdateWithSubscriptionClearAsync(null, null, githubVersion, null, branch),
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
                await velopackUpdateManager.InstallArtifactAsync(artifactUpdate, progress, _cts.Token);
                notificationService.Update(
                    progressNotificationId,
                    AppUpdateConstants.UpdateCompleteRestartingMessage,
                    AppUpdateConstants.UpdatingAppNotificationTitle);
            }
            else if (updateInfo != null)
            {
                logger?.LogInformation("Starting one-click release update: {Version}", updateInfo.TargetFullRelease.Version);
                await velopackUpdateManager.DownloadUpdatesAsync(updateInfo, progress, _cts.Token);
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
        WeakReferenceMessenger.Default.Send(new NavigationMessage(NavigationTab.Settings));
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var updateWindow = new UpdateNotificationWindow();
                updateWindow.Show();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to open update window");
            }
        });
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
        if (_cts.IsCancellationRequested)
        {
            return;
        }

        logger?.LogDebug("Periodic update check timer triggered");
        _ = CheckForUpdatesInBackgroundAsync(_cts.Token);
    }
}
