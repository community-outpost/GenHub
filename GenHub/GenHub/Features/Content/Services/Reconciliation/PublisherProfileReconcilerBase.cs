using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Notifications;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.Reconciliation;

/// <summary>
/// Shared update-check, acquisition, reconciliation, and cleanup flow for publisher
/// profile reconcilers. Publishers differ only in <see cref="PublisherReconcilerText"/>
/// and manifest matching, which stays virtual.
/// </summary>
public abstract class PublisherProfileReconcilerBase(
    ILogger logger,
    PublisherContentServices contentServices,
    PublisherInteractionServices interactionServices,
    IGameProfileManager profileManager,
    PublisherReconcilerText text,
    ITelemetryService? telemetryService = null) : IPublisherReconciler
{
    /// <inheritdoc/>
    public string PublisherType => text.PublisherType;

    /// <inheritdoc/>
    public async Task<OperationResult<PublisherReconciliationResult>> CheckAndReconcileIfNeededAsync(
        string triggeringProfileId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation(
                "{Prefix} Checking for {Publisher} updates (triggered by profile: {ProfileId})",
                text.LogPrefix,
                text.PublisherDisplayName,
                triggeringProfileId);

            // Step 1: Check for updates
            var updateResult = await contentServices.UpdateService.CheckForUpdatesAsync(cancellationToken);

            if (!updateResult.Success)
            {
                logger.LogWarning(
                    "{Prefix} Update check failed: {Error}",
                    text.LogPrefix,
                    updateResult.FirstError);
                return OperationResult<PublisherReconciliationResult>.CreateFailure(
                    $"Failed to check for {text.PublisherDisplayName} updates: {updateResult.FirstError}");
            }

            if (!updateResult.IsUpdateAvailable)
            {
                logger.LogInformation(
                    "{Prefix} No update available. Current version: {Version}",
                    text.LogPrefix,
                    updateResult.CurrentVersion);
                return OperationResult<PublisherReconciliationResult>.CreateSuccess(PublisherReconciliationResult.None);
            }

            logger.LogInformation(
                "{Prefix} Update available! Current: {CurrentVersion}, Latest: {LatestVersion}",
                text.LogPrefix,
                updateResult.CurrentVersion,
                updateResult.LatestVersion);

            // Check if this specific version is skipped
            var settings = interactionServices.UserSettingsService.Get();
            if (settings.IsVersionSkipped(text.PublisherType, updateResult.LatestVersion ?? string.Empty))
            {
                logger.LogInformation("{Prefix} User opted to skip version {Version}. Skipping.", text.LogPrefix, updateResult.LatestVersion);
                return OperationResult<PublisherReconciliationResult>.CreateSuccess(PublisherReconciliationResult.None);
            }

            // Determine strategy
            var promptResult = await PromptUserForUpdateStrategyAsync(settings, updateResult);
            if (!promptResult.ShouldProceed)
            {
                return OperationResult<PublisherReconciliationResult>.CreateSuccess(PublisherReconciliationResult.None);
            }

            var strategy = promptResult.Strategy;
            var shouldDeleteOldVersions = promptResult.ShouldDeleteOldVersions;

            var progressNotificationId = Guid.NewGuid();
            var progressNotification = new NotificationMessage(
                NotificationType.Info,
                interactionServices.LocalizationService.GetLocalizedString(text.ProgressTitleKey, text.ProgressTitleFallback),
                interactionServices.LocalizationService.GetLocalizedString(text.ProgressBodyKey, text.ProgressBodyFallback, text.ContextDisplayName, updateResult.LatestVersion),
                autoDismissMilliseconds: null,
                isPersistent: true)
            {
                Id = progressNotificationId,
            };
            interactionServices.NotificationService.Show(progressNotification);

            try
            {
                // Step 3: Find all currently installed manifests for this publisher
                var oldManifestsResult = await FindPublisherManifestsAsync(cancellationToken);
                if (!oldManifestsResult.Success || oldManifestsResult.Data == null)
                {
                    var loadError = oldManifestsResult.FirstError ?? "Failed to load manifests from pool.";

                    return FailUpdate(
                        updateResult,
                        strategy,
                        interactionServices.LocalizationService.GetLocalizedString(text.ErrorTitleKey, text.ErrorTitleFallback),
                        interactionServices.LocalizationService.GetLocalizedString(
                            "Content.Notification.ManifestLoadFailed.Message",
                            $"Failed to load installed {text.ContextDisplayName} manifests: {loadError}",
                            text.ContextDisplayName,
                            loadError),
                        $"Failed to load installed {text.ContextDisplayName} manifests: {loadError}",
                        loadError);
                }

                var oldManifests = oldManifestsResult.Data;
                if (oldManifests.Count == 0)
                {
                    logger.LogWarning("{Prefix} No existing {Publisher} manifests found in pool", text.LogPrefix, text.PublisherDisplayName);
                }

                logger.LogDebug(
                    "{Prefix} Found {Count} existing {Publisher} manifests to replace",
                    text.LogPrefix,
                    oldManifests.Count,
                    text.PublisherDisplayName);

                // Step 4: Download and acquire new content
                var acquireResult = await AcquireLatestVersionAsync(oldManifests, progressNotificationId, cancellationToken);
                if (!acquireResult.Success)
                {
                    return FailUpdate(
                        updateResult,
                        strategy,
                        interactionServices.LocalizationService.GetLocalizedString(text.AcquireFailedTitleKey, text.AcquireFailedTitleFallback),
                        interactionServices.LocalizationService.GetLocalizedString("Content.Notification.DownloadUpdateFailed.Message", $"Failed to download update: {acquireResult.FirstError}", acquireResult.FirstError),
                        string.Format(text.AcquireFailedFormat, text.ContextDisplayName, acquireResult.FirstError),
                        acquireResult.FirstError,
                        oldManifests.FirstOrDefault()?.Id.Value);
                }

                var newManifests = acquireResult.Data;
                logger.LogInformation(
                    "{Prefix} Successfully acquired {Count} new manifests",
                    text.LogPrefix,
                    newManifests.Count);

                interactionServices.NotificationService.Update(
                    progressNotificationId,
                    interactionServices.LocalizationService.GetLocalizedString("Content.Notification.ApplyingUpdate.Message", "Applying update to profiles..."),
                    interactionServices.LocalizationService.GetLocalizedString(text.ProgressTitleKey, text.ProgressTitleFallback));

                // Step 5: Update affected profiles based on strategy
                var manifestMapping = BuildManifestMapping(oldManifests, newManifests);
                var updateOutcome = await PublisherReconcilerHelper.ApplyUpdateStrategyAsync(
                    new UpdateStrategyExecutionArgs(
                        strategy,
                        oldManifests,
                        newManifests,
                        manifestMapping,
                        updateResult.LatestVersion ?? "Unknown",
                        shouldDeleteOldVersions,
                        triggeringProfileId),
                    new PublisherReconciliationContext(
                        profileManager,
                        contentServices.ReconciliationService,
                        interactionServices.NotificationService,
                        logger,
                        text.ContextDisplayName,
                        text.LogPrefix,
                        interactionServices.LocalizationService),
                    cancellationToken);

                if (!updateOutcome.Proceed)
                {
                    telemetryService?.TrackEvent(TelemetryConstants.Events.ContentUpdateFailed, new Dictionary<string, object?>
                    {
                        [TelemetryConstants.Properties.PublisherId] = text.PublisherType,
                        [TelemetryConstants.Properties.ContentName] = text.TelemetryContentName,
                        [TelemetryConstants.Properties.ContentId] = newManifests.FirstOrDefault()?.Id.Value ?? string.Empty,
                        [TelemetryConstants.Properties.Author] = text.PublisherDisplayName,
                        [TelemetryConstants.Properties.FromVersion] = updateResult.CurrentVersion ?? string.Empty,
                        [TelemetryConstants.Properties.ToVersion] = updateResult.LatestVersion ?? string.Empty,
                        [TelemetryConstants.Properties.Strategy] = strategy.ToString(),
                        [TelemetryConstants.Properties.ErrorMessage] = updateOutcome.Error ?? "Update strategy execution failed",
                    });

                    return OperationResult<PublisherReconciliationResult>.CreateFailure(updateOutcome.Error ?? "Update strategy execution failed");
                }

                var profilesUpdated = updateOutcome.ProfilesUpdated;
                var anyFailure = updateOutcome.AnyFailure;
                shouldDeleteOldVersions = updateOutcome.ShouldDeleteOldVersions;

                // Step 6: Run garbage collection (only if old versions were deleted AND no failures occurred)
                if (shouldDeleteOldVersions && !anyFailure)
                {
                    await contentServices.ReconciliationService.ScheduleGarbageCollectionAsync(false, cancellationToken);
                }
                else if (shouldDeleteOldVersions)
                {
                    // Reaching here with deletion requested implies a partial failure; otherwise GC would have run above.
                    logger.LogWarning("{Prefix} Skipping scheduled GC due to partial update failure to avoid deleting referenced content.", text.LogPrefix);
                }

                telemetryService?.TrackEvent(TelemetryConstants.Events.ContentUpdateApplied, new Dictionary<string, object?>
                {
                    [TelemetryConstants.Properties.PublisherId] = text.PublisherType,
                    [TelemetryConstants.Properties.ContentName] = text.TelemetryContentName,
                    [TelemetryConstants.Properties.ContentId] = newManifests.FirstOrDefault()?.Id.Value ?? string.Empty,
                    [TelemetryConstants.Properties.Author] = text.PublisherDisplayName,
                    [TelemetryConstants.Properties.FromVersion] = updateResult.CurrentVersion ?? string.Empty,
                    [TelemetryConstants.Properties.ToVersion] = updateResult.LatestVersion ?? string.Empty,
                    [TelemetryConstants.Properties.Strategy] = strategy.ToString(),
                    [TelemetryConstants.Properties.ProfilesUpdated] = profilesUpdated,
                    [TelemetryConstants.Properties.Success] = !anyFailure,
                });

                // Step 7: Show completion notification. Partial failures warn instead of
                // claiming success, mirroring GeneralsOnlineProfileReconciler.
                ShowCompletionNotification(strategy, updateResult.LatestVersion, profilesUpdated, anyFailure);

                logger.LogInformation(
                    "{Prefix} Reconciliation complete. Processed {ProfileCount} profiles with strategy {Strategy}",
                    text.LogPrefix,
                    profilesUpdated,
                    strategy);

                // Partial failures intentionally stay successful: the warning toast above
                // and the telemetry event record the partial state, while success lets the
                // launcher reload and run the updated target profile. This mirrors
                // GeneralsOnlineProfileReconciler, which also returns success here.
                return OperationResult<PublisherReconciliationResult>.CreateSuccess(PublisherReconciliationResult.Success(
                    strategy,
                    updateOutcome.TargetProfileId ?? triggeringProfileId,
                    profilesUpdated));
            }
            finally
            {
                interactionServices.NotificationService.Dismiss(progressNotificationId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "{Prefix} Reconciliation failed unexpectedly", text.LogPrefix);
            interactionServices.NotificationService.ShowError(
                interactionServices.LocalizationService.GetLocalizedString(text.ErrorTitleKey, text.ErrorTitleFallback),
                interactionServices.LocalizationService.GetLocalizedString("Content.Notification.UpdateError.Message", $"An error occurred during update: {ex.Message}", ex.Message),
                NotificationDurations.Critical);
            return OperationResult<PublisherReconciliationResult>.CreateFailure($"Reconciliation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Finds the replacement manifest for an old manifest. The default matches by content
    /// type and target game; publishers with per-variant manifests override this.
    /// </summary>
    /// <param name="oldManifest">The old manifest being replaced.</param>
    /// <param name="newManifests">The newly acquired manifests.</param>
    /// <returns>The replacement manifest, or null when none matches.</returns>
    protected virtual ContentManifest? FindReplacementManifest(
        ContentManifest oldManifest,
        IReadOnlyList<ContentManifest> newManifests) =>
        newManifests.FirstOrDefault(n =>
            n.ContentType == oldManifest.ContentType &&
            (oldManifest.TargetGame == GameType.Unknown || n.TargetGame == GameType.Unknown || n.TargetGame == oldManifest.TargetGame));

    private Dictionary<string, string> BuildManifestMapping(
        IReadOnlyList<ContentManifest> oldManifests,
        IReadOnlyList<ContentManifest> newManifests)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var oldManifest in oldManifests)
        {
            var newManifest = FindReplacementManifest(oldManifest, newManifests);

            if (newManifest != null)
            {
                mapping[oldManifest.Id.Value] = newManifest.Id.Value;
            }
        }

        return mapping;
    }

    private void ShowCompletionNotification(UpdateStrategy strategy, string? latestVersion, int profilesUpdated, bool anyFailure)
    {
        if (anyFailure)
        {
            interactionServices.NotificationService.ShowWarning(
                interactionServices.LocalizationService.GetLocalizedString(text.PartialUpdatedTitleKey, text.PartialUpdatedTitleFallback),
                interactionServices.LocalizationService.GetLocalizedString("Content.Notification.PublisherUpdatedPartial.Message", $"Updated to {latestVersion}, but some profiles or components had issues.", latestVersion),
                NotificationDurations.VeryLong);
            return;
        }

        interactionServices.NotificationService.ShowSuccess(
            interactionServices.LocalizationService.GetLocalizedString(text.UpdatedTitleKey, text.UpdatedTitleFallback),
            interactionServices.LocalizationService.GetLocalizedString("Content.Notification.PublisherUpdated.Message", $"Successfully updated to version {latestVersion}. {profilesUpdated} profiles {(strategy == UpdateStrategy.CreateNewProfile ? "created" : "updated")}.", latestVersion, profilesUpdated, strategy == UpdateStrategy.CreateNewProfile ? interactionServices.LocalizationService.GetLocalizedString("Content.Notification.ProfilesCreated.Word", "created") : interactionServices.LocalizationService.GetLocalizedString("Content.Notification.ProfilesUpdated.Word", "updated")),
            NotificationDurations.Long);
    }

    private OperationResult<PublisherReconciliationResult> FailUpdate(
        ContentUpdateCheckResult updateResult,
        UpdateStrategy strategy,
        string title,
        string message,
        string failureMessage,
        string? error,
        string? contentId = null)
    {
        telemetryService?.TrackEvent(TelemetryConstants.Events.ContentUpdateFailed, new Dictionary<string, object?>
        {
            [TelemetryConstants.Properties.PublisherId] = text.PublisherType,
            [TelemetryConstants.Properties.ContentName] = text.TelemetryContentName,
            [TelemetryConstants.Properties.ContentId] = contentId ?? string.Empty,
            [TelemetryConstants.Properties.Author] = text.PublisherDisplayName,
            [TelemetryConstants.Properties.FromVersion] = updateResult.CurrentVersion ?? string.Empty,
            [TelemetryConstants.Properties.ToVersion] = updateResult.LatestVersion ?? string.Empty,
            [TelemetryConstants.Properties.Strategy] = strategy.ToString(),
            [TelemetryConstants.Properties.ErrorMessage] = error,
        });

        interactionServices.NotificationService.ShowError(title, message, NotificationDurations.Critical);

        return OperationResult<PublisherReconciliationResult>.CreateFailure(failureMessage);
    }

    private async Task<OperationResult<List<ContentManifest>>> FindPublisherManifestsAsync(
        CancellationToken cancellationToken)
    {
        var manifestsResult = await contentServices.ManifestPool.GetAllManifestsAsync(cancellationToken);
        if (!manifestsResult.Success || manifestsResult.Data == null)
        {
            return OperationResult<List<ContentManifest>>.CreateFailure(
                manifestsResult.FirstError ?? "Failed to load manifests from pool.");
        }

        return OperationResult<List<ContentManifest>>.CreateSuccess([.. manifestsResult.Data
            .Where(m =>
                m.Publisher?.PublisherType?.Equals(text.PublisherType, StringComparison.OrdinalIgnoreCase) == true)]);
    }

    private IProgress<ContentAcquisitionProgress>? CreateAcquisitionProgress(
        Guid? progressNotificationId,
        string itemName,
        int currentItemIndex,
        int totalItems)
    {
        if (!progressNotificationId.HasValue)
        {
            return null;
        }

        var notificationId = progressNotificationId.Value;
        var lastNotificationTimestamp = Stopwatch.GetTimestamp();

        return new Progress<ContentAcquisitionProgress>(p =>
        {
            var elapsedMs = Stopwatch.GetElapsedTime(lastNotificationTimestamp).TotalMilliseconds;
            if (elapsedMs < ManifestConstants.NotificationUpdateThrottleMs && p.ProgressPercentage < 100)
            {
                return;
            }

            lastNotificationTimestamp = Stopwatch.GetTimestamp();
            var status = p.FormatProgressStatus();
            var message = totalItems > 1
                ? $"[{currentItemIndex}/{totalItems}] {itemName}: {status}"
                : $"{itemName}: {status}";

            interactionServices.NotificationService.Update(
                notificationId,
                message,
                interactionServices.LocalizationService.GetLocalizedString(text.ProgressTitleKey, text.ProgressTitleFallback));
        });
    }

    private async Task<OperationResult<bool>> AcquireItemsAsync(
        IReadOnlyList<ContentSearchResult> items,
        Guid? progressNotificationId,
        CancellationToken cancellationToken)
    {
        int totalItems = items.Count;
        int currentItemIndex = 0;

        foreach (var result in items)
        {
            currentItemIndex++;
            var progress = CreateAcquisitionProgress(
                progressNotificationId,
                result.Name,
                currentItemIndex,
                totalItems);

            var acquireOp = await contentServices.ContentOrchestrator.AcquireContentAsync(result, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!acquireOp.Success)
            {
                logger.LogError(
                    "{Prefix} Failed to acquire content {ContentId}: {Error}",
                    text.LogPrefix,
                    result.Id,
                    acquireOp.FirstError);

                return OperationResult<bool>.CreateFailure(
                    string.Format(text.AcquireItemFailedFormat, text.PublisherDisplayName, result.Id, acquireOp.FirstError));
            }
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private async Task<OperationResult<List<ContentManifest>>> AcquireLatestVersionAsync(
        IReadOnlyList<ContentManifest> oldManifests,
        Guid? progressNotificationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = new ContentSearchQuery
            {
                ProviderName = text.PublisherType,
                ContentType = ContentType.GameClient,
            };

            var searchResult = await contentServices.ContentOrchestrator.SearchAsync(query, cancellationToken);

            // Layers beneath the orchestrator still report cancellation as a failed result,
            // so a failure raised while shutting down must not be surfaced as a real error.
            cancellationToken.ThrowIfCancellationRequested();

            if (!searchResult.Success || searchResult.Data == null || !searchResult.Data.Any())
            {
                return OperationResult<List<ContentManifest>>.CreateFailure(text.NoContentMessage);
            }

            var items = searchResult.Data.ToList();
            var acquireResult = await AcquireItemsAsync(items, progressNotificationId, cancellationToken);
            if (!acquireResult.Success)
            {
                return OperationResult<List<ContentManifest>>.CreateFailure(acquireResult.FirstError ?? "Failed to acquire content");
            }

            var allManifestsResult = await FindPublisherManifestsAsync(cancellationToken);
            if (!allManifestsResult.Success || allManifestsResult.Data == null)
            {
                return OperationResult<List<ContentManifest>>.CreateFailure(
                    allManifestsResult.FirstError ?? "Failed to load manifests from pool.");
            }

            var allManifests = allManifestsResult.Data;
            var oldIds = oldManifests.Select(m => m.Id.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var newManifests = allManifests
                .Where(m => !oldIds.Contains(m.Id.Value))
                .ToList();

            if (newManifests.Count == 0)
            {
                return OperationResult<List<ContentManifest>>.CreateFailure(text.AcquireNoneMessage);
            }

            return OperationResult<List<ContentManifest>>.CreateSuccess(newManifests);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Prefix} Failed to acquire latest version", text.LogPrefix);
            return OperationResult<List<ContentManifest>>.CreateFailure($"Failed to acquire latest version: {ex.Message}");
        }
    }

    private async Task<(bool ShouldProceed, UpdateStrategy Strategy, bool ShouldDeleteOldVersions)> PromptUserForUpdateStrategyAsync(
        UserSettings settings,
        ContentUpdateCheckResult updateResult)
    {
        var subscription = settings.GetSubscription(text.PublisherType);
        var strategy = subscription?.PreferredUpdateStrategy ?? settings.PreferredUpdateStrategy ?? UpdateStrategy.ReplaceCurrent;
        var autoUpdate = subscription?.AutoUpdateEnabled == true;
        var shouldDeleteOldVersions = subscription?.DeleteOldVersions ?? true;

        if (autoUpdate)
        {
            return (true, strategy, shouldDeleteOldVersions);
        }

        var dialogResult = await interactionServices.DialogService.ShowUpdateOptionDialogAsync(
            interactionServices.LocalizationService.GetLocalizedString(text.PromptTitleKey, text.PromptTitleFallback),
            interactionServices.LocalizationService.GetLocalizedString(text.PromptBodyKey, string.Format(text.PromptBodyFormat, updateResult.LatestVersion), updateResult.LatestVersion),
            shouldDeleteOldVersions);

        if (dialogResult == null)
        {
            return (false, strategy, shouldDeleteOldVersions);
        }

        if (dialogResult.Action == "Skip")
        {
            logger.LogInformation("{Prefix} User skipped version {Version}.", text.LogPrefix, updateResult.LatestVersion);

            if (dialogResult.IsDoNotAskAgain)
            {
                await interactionServices.UserSettingsService.TryUpdateAndSaveAsync(s =>
                {
                    s.SkipVersion(text.PublisherType, updateResult.LatestVersion ?? string.Empty);
                    return true;
                });
            }

            return (false, strategy, shouldDeleteOldVersions);
        }

        strategy = dialogResult.Strategy;
        shouldDeleteOldVersions = dialogResult.DeleteOldVersions;

        if (dialogResult.IsDoNotAskAgain)
        {
            logger.LogInformation("{Prefix} Saving user preference for {Publisher} updates", text.LogPrefix, text.PublisherDisplayName);
            await interactionServices.UserSettingsService.TryUpdateAndSaveAsync(s =>
            {
                s.SetAutoUpdatePreference(text.PublisherType, true);
                var sub = s.GetSubscription(text.PublisherType);
                if (sub != null)
                {
                    sub.PreferredUpdateStrategy = strategy;
                    sub.DeleteOldVersions = shouldDeleteOldVersions;
                }

                return true;
            });
        }

        return (true, strategy, shouldDeleteOldVersions);
    }
}
