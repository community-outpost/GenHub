using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GitHub;
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
/// <param name="gitHubTokenStorage">Optional GitHub token storage for checking token availability.</param>
public class BackgroundUpdateCoordinator(
    IVelopackUpdateManager velopackUpdateManager,
    IUserSettingsService userSettingsService,
    INotificationService notificationService,
    ILogger<BackgroundUpdateCoordinator> logger,
    IGitHubTokenStorage? gitHubTokenStorage = null) : IBackgroundUpdateCoordinator, IRecipient<UpdateSettingsChangedMessage>
{
    private readonly CancellationTokenSource _cts = new();
    private readonly object _lifecycleLock = new();
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private Timer? _periodicUpdateTimer;
    private string? _lastNotifiedUpdateIdentity;
    private bool _disposed;

    /// <inheritdoc/>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        RegisterMessages();

        var settings = userSettingsService.Get();
        if (settings.AutoCheckForUpdatesOnStartup && TryGetLifetimeToken(out var lifetimeToken))
        {
            _ = CheckForUpdatesOnStartupAsync(lifetimeToken, cancellationToken);
        }

        RestartPeriodicUpdateTimer(settings.AutoCheckForUpdatesPeriodically, settings.PeriodicUpdateCheckIntervalMinutes);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetLifetimeToken(out var lifetimeToken))
        {
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken, cancellationToken);
        var effectiveToken = linkedCts.Token;

        logger?.LogDebug("Starting background update check");

        try
        {
            await _checkLock.WaitAsync(effectiveToken);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            var settings = userSettingsService.Get();

            // 1. check for subscribed pr artifacts
            if (settings.SubscribedPrNumber.HasValue)
            {
                await CheckSubscribedPrUpdateAsync(settings.SubscribedPrNumber.Value, settings, effectiveToken);
                return;
            }

            // 2. check for subscribed branch artifacts
            if (!string.IsNullOrWhiteSpace(settings.SubscribedBranch))
            {
                await CheckSubscribedBranchUpdateAsync(settings.SubscribedBranch, settings, effectiveToken);
                return;
            }

            // 3. check for standard github releases
            await CheckStandardReleaseUpdateAsync(settings, effectiveToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Exception in CheckForUpdatesAsync");
        }
        finally
        {
            var currentSettings = userSettingsService.Get();
            velopackUpdateManager.SubscribedPrNumber = currentSettings.SubscribedPrNumber;
            velopackUpdateManager.SubscribedBranch = currentSettings.SubscribedBranch;

            try
            {
                _checkLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // Coordinator was disposed during check
            }
        }
    }

    /// <inheritdoc/>
    public void Receive(UpdateSettingsChangedMessage message)
    {
        RestartPeriodicUpdateTimer(message.AutoCheckForUpdatesPeriodically, message.PeriodicUpdateCheckIntervalMinutes);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes resources used by the coordinator.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources; false if finalizing.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                // Cancel before publishing the disposed state so every observer of shutdown sees a cancelled token.
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Ignore if another Dispose call already completed.
            }
        }

        Timer? timerToDispose = null;
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            timerToDispose = _periodicUpdateTimer;
            _periodicUpdateTimer = null;
        }

        if (disposing)
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);
            timerToDispose?.Dispose();
            _cts.Dispose();
        }
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
        if (gitHubTokenStorage != null && !gitHubTokenStorage.HasToken())
        {
            logger?.LogDebug("No GitHub token configured; skipping background PR artifact check for #{PrNumber}", prNumber);
            return;
        }

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
                var updateIdentity = $"{AppUpdateConstants.PrDedupePrefix}{prNumber}:{artifactVersionBase}";
                if (string.Equals(_lastNotifiedUpdateIdentity, updateIdentity, StringComparison.Ordinal))
                {
                    logger?.LogDebug(AppUpdateConstants.NotificationAlreadyShownLogFormat, updateIdentity);
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

        if (await TryNotifyPrMergedDevFallbackAsync(prNumber, settings, currentVersionBase, cancellationToken))
        {
            return;
        }

        if (await TryNotifyPrMergedReleaseFallbackAsync(prNumber, settings, cancellationToken))
        {
            return;
        }

        TryNotifyPrMergedGitHubFallback(prNumber, settings);
    }

    private async Task<bool> TryNotifyPrMergedDevFallbackAsync(
        int prNumber,
        UserSettings settings,
        string currentVersionBase,
        CancellationToken cancellationToken)
    {
        velopackUpdateManager.SubscribedPrNumber = null;
        velopackUpdateManager.SubscribedBranch = AppUpdateConstants.DevelopmentBranch;

        var devArtifact = await velopackUpdateManager.CheckForArtifactUpdatesAsync(cancellationToken);
        if (devArtifact == null)
        {
            return false;
        }

        var devVersionBase = devArtifact.Version.Split('+')[0];
        if (!AppUpdateVersionHelper.IsArtifactVersionNewer(devVersionBase, currentVersionBase, allowCrossChannel: true) ||
            string.Equals(devVersionBase, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var updateIdentity = $"{AppUpdateConstants.PrFallbackDedupePrefix}{prNumber}:dev:{devVersionBase}";
        if (string.Equals(_lastNotifiedUpdateIdentity, updateIdentity, StringComparison.Ordinal))
        {
            logger?.LogDebug(AppUpdateConstants.NotificationAlreadyShownLogFormat, updateIdentity);
            return true;
        }

        _lastNotifiedUpdateIdentity = updateIdentity;
        logger?.LogInformation("PR #{PrNumber} merged or closed. Development fallback update available: {Version}", prNumber, devArtifact.DisplayVersion);
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
        return true;
    }

    private async Task<bool> TryNotifyPrMergedReleaseFallbackAsync(
        int prNumber,
        UserSettings settings,
        CancellationToken cancellationToken)
    {
        velopackUpdateManager.SubscribedBranch = null;
        var releaseUpdate = await velopackUpdateManager.CheckForUpdatesAsync(cancellationToken);
        if (releaseUpdate == null)
        {
            return false;
        }

        var version = releaseUpdate.TargetFullRelease.Version.ToString();
        if (string.Equals(version, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var updateIdentity = $"{AppUpdateConstants.PrFallbackDedupePrefix}{prNumber}:release:{version}";
        if (string.Equals(_lastNotifiedUpdateIdentity, updateIdentity, StringComparison.Ordinal))
        {
            logger?.LogDebug(AppUpdateConstants.NotificationAlreadyShownLogFormat, updateIdentity);
            return true;
        }

        _lastNotifiedUpdateIdentity = updateIdentity;
        logger?.LogInformation("PR #{PrNumber} merged or closed. Release fallback update available: {Version}", prNumber, version);
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
        return true;
    }

    private void TryNotifyPrMergedGitHubFallback(int prNumber, UserSettings settings)
    {
        if (!velopackUpdateManager.HasUpdateAvailableFromGitHub)
        {
            return;
        }

        var githubVersion = velopackUpdateManager.LatestVersionFromGitHub;
        if (string.IsNullOrWhiteSpace(githubVersion) ||
            string.Equals(githubVersion, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var updateIdentity = $"{AppUpdateConstants.PrFallbackDedupePrefix}{prNumber}:github:{githubVersion}";
        if (string.Equals(_lastNotifiedUpdateIdentity, updateIdentity, StringComparison.Ordinal))
        {
            logger?.LogDebug(AppUpdateConstants.NotificationAlreadyShownLogFormat, updateIdentity);
            return;
        }

        _lastNotifiedUpdateIdentity = updateIdentity;
        logger?.LogInformation("PR #{PrNumber} merged or closed. GitHub API release fallback available: {Version}", prNumber, githubVersion);
        notificationService.Show(new NotificationMessage(
            NotificationType.Info,
            AppUpdateConstants.PrMergedUpdateAvailableNotificationTitle,
            string.Format(AppUpdateConstants.PrMergedReleaseNotificationFormat, githubVersion, prNumber),
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

    private async Task CheckSubscribedBranchUpdateAsync(string branch, UserSettings settings, CancellationToken cancellationToken)
    {
        if (gitHubTokenStorage != null && !gitHubTokenStorage.HasToken())
        {
            if (string.Equals(branch, AppUpdateConstants.MainBranch, StringComparison.OrdinalIgnoreCase))
            {
                logger?.LogDebug("No GitHub token configured for main branch; checking standard releases instead");
                await CheckStandardReleaseUpdateAsync(settings, cancellationToken);
                return;
            }

            logger?.LogDebug("No GitHub token configured; skipping background branch artifact check for '{Branch}'", branch);
            return;
        }

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
                var updateIdentity = $"{AppUpdateConstants.BranchDedupePrefix}{branch}:{artifactVersionBase}";
                if (string.Equals(_lastNotifiedUpdateIdentity, updateIdentity, StringComparison.Ordinal))
                {
                    logger?.LogDebug(AppUpdateConstants.NotificationAlreadyShownLogFormat, updateIdentity);
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
            return;
        }

        if (string.Equals(branch, AppUpdateConstants.MainBranch, StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogDebug(AppUpdateConstants.MainBranchReleaseFallbackLogMessage);
            await CheckStandardReleaseUpdateAsync(settings, cancellationToken);
        }
    }

    private async Task CheckStaleBranchFallbackUpdateAsync(string branch, UserSettings settings, CancellationToken cancellationToken)
    {
        logger?.LogInformation("Subscribed branch '{Branch}' has no artifacts. Checking development/release fallback", branch);
        var currentVersionBase = UpdateNotificationViewModel.CurrentAppVersion.Split('+')[0];

        if (await TryNotifyBranchStaleDevFallbackAsync(branch, settings, currentVersionBase, cancellationToken))
        {
            return;
        }

        if (await TryNotifyBranchStaleReleaseFallbackAsync(branch, settings, cancellationToken))
        {
            return;
        }

        TryNotifyBranchStaleGitHubFallback(branch, settings);
    }

    private async Task<bool> TryNotifyBranchStaleDevFallbackAsync(
        string branch,
        UserSettings settings,
        string currentVersionBase,
        CancellationToken cancellationToken)
    {
        velopackUpdateManager.SubscribedBranch = AppUpdateConstants.DevelopmentBranch;
        var devArtifact = await velopackUpdateManager.CheckForArtifactUpdatesAsync(cancellationToken);
        if (devArtifact == null)
        {
            return false;
        }

        var devVersionBase = devArtifact.Version.Split('+')[0];
        if (!AppUpdateVersionHelper.IsArtifactVersionNewer(devVersionBase, currentVersionBase, allowCrossChannel: true) ||
            string.Equals(devVersionBase, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var updateIdentity = $"{AppUpdateConstants.BranchFallbackDedupePrefix}{branch}:dev:{devVersionBase}";
        if (string.Equals(_lastNotifiedUpdateIdentity, updateIdentity, StringComparison.Ordinal))
        {
            logger?.LogDebug(AppUpdateConstants.NotificationAlreadyShownLogFormat, updateIdentity);
            return true;
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
        return true;
    }

    private async Task<bool> TryNotifyBranchStaleReleaseFallbackAsync(
        string branch,
        UserSettings settings,
        CancellationToken cancellationToken)
    {
        velopackUpdateManager.SubscribedBranch = null;
        var releaseUpdate = await velopackUpdateManager.CheckForUpdatesAsync(cancellationToken);
        if (releaseUpdate == null)
        {
            return false;
        }

        var version = releaseUpdate.TargetFullRelease.Version.ToString();
        if (string.Equals(version, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var updateIdentity = $"{AppUpdateConstants.BranchFallbackDedupePrefix}{branch}:release:{version}";
        if (string.Equals(_lastNotifiedUpdateIdentity, updateIdentity, StringComparison.Ordinal))
        {
            logger?.LogDebug(AppUpdateConstants.NotificationAlreadyShownLogFormat, updateIdentity);
            return true;
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
        return true;
    }

    private void TryNotifyBranchStaleGitHubFallback(string branch, UserSettings settings)
    {
        if (!velopackUpdateManager.HasUpdateAvailableFromGitHub)
        {
            return;
        }

        var githubVersion = velopackUpdateManager.LatestVersionFromGitHub;
        if (string.IsNullOrWhiteSpace(githubVersion) ||
            string.Equals(githubVersion, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var updateIdentity = $"{AppUpdateConstants.BranchFallbackDedupePrefix}{branch}:github:{githubVersion}";
        if (string.Equals(_lastNotifiedUpdateIdentity, updateIdentity, StringComparison.Ordinal))
        {
            logger?.LogDebug(AppUpdateConstants.NotificationAlreadyShownLogFormat, updateIdentity);
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
                    () => _ = PerformOneClickUpdateAsync(null, null, githubVersion),
                    NotificationActionStyle.Primary,
                    dismissOnExecute: true),
            ],
            isPersistent: true,
            showInBadge: true));
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
                var updateIdentity = $"{AppUpdateConstants.ReleaseDedupePrefix}{version}";
                if (string.Equals(_lastNotifiedUpdateIdentity, updateIdentity, StringComparison.Ordinal))
                {
                    logger?.LogDebug(AppUpdateConstants.NotificationAlreadyShownLogFormat, updateIdentity);
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
                var updateIdentity = $"{AppUpdateConstants.GitHubFallbackDedupePrefix}{githubVersion}";
                if (string.Equals(_lastNotifiedUpdateIdentity, updateIdentity, StringComparison.Ordinal))
                {
                    logger?.LogDebug(AppUpdateConstants.NotificationAlreadyShownLogFormat, updateIdentity);
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
        await PerformOneClickUpdateAsync(
            artifactUpdate,
            updateInfo,
            githubVersion,
            clearedPrNumber,
            clearedBranch);
    }

    private async Task PerformOneClickUpdateAsync(
        ArtifactUpdateInfo? artifactUpdate,
        UpdateInfo? updateInfo,
        string? githubVersion,
        int? clearedPrNumber = null,
        string? clearedBranch = null)
    {
        if (!TryGetLifetimeToken(out var lifetimeToken))
        {
            logger?.LogDebug("Skipping update installation because the coordinator is disposed");
            return;
        }

        var progressNotificationId = Guid.NewGuid();

        try
        {
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

            if (artifactUpdate != null)
            {
                logger?.LogInformation("Starting one-click artifact install: {Version}", artifactUpdate.DisplayVersion);
                await velopackUpdateManager.InstallArtifactAsync(artifactUpdate, progress, lifetimeToken);
                await ClearStaleSubscriptionAsync(clearedPrNumber, clearedBranch, lifetimeToken);
                notificationService.Update(
                    progressNotificationId,
                    AppUpdateConstants.UpdateCompleteRestartingMessage,
                    AppUpdateConstants.UpdatingAppNotificationTitle);
            }
            else if (updateInfo != null)
            {
                logger?.LogInformation("Starting one-click release update: {Version}", updateInfo.TargetFullRelease.Version);
                await velopackUpdateManager.DownloadUpdatesAsync(updateInfo, progress, lifetimeToken);
                await ClearStaleSubscriptionAsync(clearedPrNumber, clearedBranch, lifetimeToken);
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
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
            notificationService.Dismiss(progressNotificationId);
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

    private async Task ClearStaleSubscriptionAsync(
        int? clearedPrNumber,
        string? clearedBranch,
        CancellationToken cancellationToken)
    {
        if (!clearedPrNumber.HasValue && string.IsNullOrEmpty(clearedBranch))
        {
            return;
        }

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
            await userSettingsService.SaveAsync(cancellationToken);
            logger?.LogInformation(
                "Cleared stale subscription (PR: {PrNumber}, Branch: {Branch}) after applying fallback update",
                clearedPrNumber,
                clearedBranch);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to clear stale subscription settings after fallback update");
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

    private async Task CheckForUpdatesOnStartupAsync(CancellationToken lifetimeToken, CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken, cancellationToken);
        await CheckForUpdatesInBackgroundAsync(linkedCts.Token);
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
        Timer? timerToDispose = null;
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            timerToDispose = _periodicUpdateTimer;
            _periodicUpdateTimer = null;

            if (enabled && intervalMinutes > 0)
            {
                var clampedInterval = Math.Clamp(
                    intervalMinutes,
                    AppUpdateConstants.MinPeriodicUpdateCheckIntervalMinutes,
                    AppUpdateConstants.MaxPeriodicUpdateCheckIntervalMinutes);

                var interval = TimeSpan.FromMinutes(clampedInterval);
                logger?.LogDebug("Starting periodic update check timer with interval: {Interval}", interval);

                _periodicUpdateTimer = new Timer(
                    OnPeriodicUpdateTimerCallback,
                    _cts.Token,
                    interval,
                    interval);
            }
        }

        timerToDispose?.Dispose();
    }

    private void OnPeriodicUpdateTimerCallback(object? state)
    {
        if (state is not CancellationToken lifetimeToken || lifetimeToken.IsCancellationRequested)
        {
            return;
        }

        logger?.LogDebug("Periodic update check timer triggered");
        _ = CheckForUpdatesInBackgroundAsync(lifetimeToken);
    }

    private bool TryGetLifetimeToken(out CancellationToken lifetimeToken)
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                lifetimeToken = default;
                return false;
            }

            lifetimeToken = _cts.Token;
            return true;
        }
    }
}
