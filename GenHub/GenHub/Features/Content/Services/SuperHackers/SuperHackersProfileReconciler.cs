using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Notifications;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Reconciliation;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.SuperHackers;

/// <summary>
/// Service for reconciling profiles when SuperHackers updates are detected.
/// </summary>
public class SuperHackersProfileReconciler(
    ILogger<SuperHackersProfileReconciler> logger,
    ISuperHackersUpdateService updateService,
    IContentManifestPool manifestPool,
    IContentOrchestrator contentOrchestrator,
    IContentReconciliationService reconciliationService,
    INotificationService notificationService,
    IDialogService dialogService,
    IUserSettingsService userSettingsService,
    IGameProfileManager profileManager) : ISuperHackersProfileReconciler, IPublisherReconciler
{
    /// <inheritdoc/>
    public string PublisherType => PublisherTypeConstants.TheSuperHackers;

    /// <inheritdoc/>
    public async Task<OperationResult<PublisherReconciliationResult>> CheckAndReconcileIfNeededAsync(
        string triggeringProfileId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation(
                "[SH Reconciler] Checking for SuperHackers updates (triggered by profile: {ProfileId})",
                triggeringProfileId);

            // Step 1: Check for updates
            var updateResult = await updateService.CheckForUpdatesAsync(cancellationToken);

            if (!updateResult.Success)
            {
                logger.LogWarning(
                    "[SH Reconciler] Update check failed: {Error}",
                    updateResult.FirstError);
                return OperationResult<PublisherReconciliationResult>.CreateFailure(
                    $"Failed to check for SuperHackers updates: {updateResult.FirstError}");
            }

            if (!updateResult.IsUpdateAvailable)
            {
                logger.LogInformation(
                    "[SH Reconciler] No update available. Current version: {Version}",
                    updateResult.CurrentVersion);
                return OperationResult<PublisherReconciliationResult>.CreateSuccess(PublisherReconciliationResult.None);
            }

            logger.LogInformation(
                "[SH Reconciler] Update available! Current: {CurrentVersion}, Latest: {LatestVersion}",
                updateResult.CurrentVersion,
                updateResult.LatestVersion);

            // Check if this specific version is skipped
            var settings = userSettingsService.Get();
            if (settings.IsVersionSkipped(PublisherTypeConstants.TheSuperHackers, updateResult.LatestVersion ?? string.Empty))
            {
                logger.LogInformation("[SH Reconciler] User opted to skip version {Version}. Skipping.", updateResult.LatestVersion);
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
                "SuperHackers Update",
                $"Installing SuperHackers {updateResult.LatestVersion}. Please wait...",
                autoDismissMilliseconds: null,
                isPersistent: true)
            {
                Id = progressNotificationId,
            };
            notificationService.Show(progressNotification);

            try
            {
                // Find existing installed manifests
                var oldManifests = await FindSuperHackersManifestsAsync(cancellationToken);

                logger.LogInformation(
                    "[SH Reconciler] Found {Count} existing SuperHackers manifests to replace",
                    oldManifests.Count);

                // Acquire new content
                var acquireResult = await AcquireLatestVersionAsync(oldManifests, progressNotificationId, cancellationToken);
                if (!acquireResult.Success)
                {
                    notificationService.ShowError(
                        "SuperHackers Update Failed",
                        $"Failed to download update: {acquireResult.FirstError}",
                        NotificationDurations.Critical);

                    return OperationResult<PublisherReconciliationResult>.CreateFailure(
                        $"Failed to acquire new SuperHackers version: {acquireResult.FirstError}");
                }

                var newManifests = acquireResult.Data!;

                notificationService.Update(
                    progressNotificationId,
                    "Applying update to profiles...",
                    "SuperHackers Update");

                // Update profiles based on strategy
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
                        reconciliationService,
                        notificationService,
                        logger,
                        "SuperHackers",
                        "[SH Reconciler]"),
                    cancellationToken);

                if (!updateOutcome.Proceed)
                {
                    return OperationResult<PublisherReconciliationResult>.CreateFailure(updateOutcome.Error ?? "Update strategy execution failed");
                }

                var profilesUpdated = updateOutcome.ProfilesUpdated;
                var anyFailure = updateOutcome.AnyFailure;
                shouldDeleteOldVersions = updateOutcome.ShouldDeleteOldVersions;

                // Run garbage collection only if old versions were deleted AND no failures occurred
                if (shouldDeleteOldVersions && !anyFailure)
                {
                    await reconciliationService.ScheduleGarbageCollectionAsync(false, cancellationToken);
                }
                else if (shouldDeleteOldVersions && anyFailure)
                {
                    logger.LogWarning("[SH Reconciler] Skipping scheduled GC due to partial update failure to avoid deleting referenced content.");
                }

                notificationService.ShowSuccess(
                    "SuperHackers Updated",
                    $"Successfully updated to version {updateResult.LatestVersion}. {profilesUpdated} profiles {(strategy == UpdateStrategy.CreateNewProfile ? "created" : "updated")}.",
                    NotificationDurations.Long);

                logger.LogInformation(
                    "[SH Reconciler] Reconciliation complete. Processed {ProfileCount} profiles with strategy {Strategy}",
                    profilesUpdated,
                    strategy);

                return OperationResult<PublisherReconciliationResult>.CreateSuccess(PublisherReconciliationResult.Success(
                    strategy,
                    updateOutcome.TargetProfileId ?? triggeringProfileId,
                    profilesUpdated));
            }
            finally
            {
                notificationService.Dismiss(progressNotificationId);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("[SH Reconciler] Reconciliation cancelled");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[SH Reconciler] Reconciliation failed unexpectedly");
            notificationService.ShowError(
                "SuperHackers Update Error",
                $"An error occurred during update: {ex.Message}",
                NotificationDurations.Critical);
            return OperationResult<PublisherReconciliationResult>.CreateFailure($"Reconciliation failed: {ex.Message}");
        }
    }

    private static Dictionary<string, string> BuildManifestMapping(
        IReadOnlyList<ContentManifest> oldManifests,
        IReadOnlyList<ContentManifest> newManifests)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var oldManifest in oldManifests)
        {
            var newManifest = newManifests
                .Where(n =>
                    n.ContentType == oldManifest.ContentType &&
                    MatchesByVariant(oldManifest.Id.Value, n.Id.Value))
                .OrderByDescending(n => GameVersionHelper.ParseVersionToInt(n.Version))
                .FirstOrDefault();

            if (newManifest != null)
            {
                mapping[oldManifest.Id.Value] = newManifest.Id.Value;
            }
        }

        return mapping;
    }

    private static bool MatchesByVariant(string oldId, string newId)
    {
        var oldVariant = ExtractVariant(oldId);
        var newVariant = ExtractVariant(newId);
        return string.Equals(oldVariant, newVariant, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractVariant(string manifestId)
    {
        if (string.IsNullOrEmpty(manifestId)) return null;

        var parts = manifestId.Split('.');
        if (parts.Length == 0) return null;

        var lastPart = parts[^1];

        // Exact matches for known suffixes
        if (lastPart.Equals(SuperHackersConstants.GeneralsSuffix, StringComparison.OrdinalIgnoreCase))
            return SuperHackersConstants.GeneralsSuffix;

        if (lastPart.Equals(SuperHackersConstants.ZeroHourSuffix, StringComparison.OrdinalIgnoreCase))
            return SuperHackersConstants.ZeroHourSuffix;

        return parts.Length > 1 ? parts[^1] : null;
    }

    private async Task<List<ContentManifest>> FindSuperHackersManifestsAsync(
        CancellationToken cancellationToken)
    {
        var manifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
        if (!manifestsResult.Success || manifestsResult.Data == null)
        {
            return [];
        }

        return [.. manifestsResult.Data
            .Where(m =>
                m.Publisher?.PublisherType?.Equals(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) == true)];
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

            notificationService.Update(
                notificationId,
                message,
                "SuperHackers Update");
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

            var acquireOp = await contentOrchestrator.AcquireContentAsync(result, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!acquireOp.Success)
            {
                logger.LogError(
                    "[SH:Reconciler] Failed to acquire content {ContentId}: {Error}",
                    result.Id,
                    acquireOp.FirstError);

                return OperationResult<bool>.CreateFailure(
                    $"Failed to acquire SuperHackers content {result.Id}: {acquireOp.FirstError}");
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
            // NOTE: The query retrieves all GameClients for TheSuperHackers without filtering by the
            // triggering profile's GameType (Generals vs Zero Hour). As a result, both Generals and Zero Hour
            // game client updates are downloaded and stored concurrently during reconciliation.
            // Having both installed is currently fine and intended so they stay updated and get replaced by updates anyway.
            // If single-client selective downloads are desired in the future, filter query.TargetGame by the profile's GameType.
            var query = new ContentSearchQuery
            {
                ProviderName = PublisherTypeConstants.TheSuperHackers,
                ContentType = ContentType.GameClient,
            };

            var searchResult = await contentOrchestrator.SearchAsync(query, cancellationToken);

            // Layers beneath the orchestrator still report cancellation as a failed result, so a
            // failure raised while shutting down must not be surfaced as a real acquisition error.
            cancellationToken.ThrowIfCancellationRequested();

            if (!searchResult.Success || searchResult.Data == null || !searchResult.Data.Any())
            {
                return OperationResult<List<ContentManifest>>.CreateFailure(
                   "No SuperHackers content found from provider");
            }

            var items = searchResult.Data.ToList();
            var acquireResult = await AcquireItemsAsync(items, progressNotificationId, cancellationToken);
            if (!acquireResult.Success)
            {
                return OperationResult<List<ContentManifest>>.CreateFailure(acquireResult.FirstError ?? "Failed to acquire content");
            }

            var allManifests = await FindSuperHackersManifestsAsync(cancellationToken);
            var oldIds = oldManifests.Select(m => m.Id.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var newManifests = allManifests
                .Where(m => !oldIds.Contains(m.Id.Value))
                .ToList();

            if (newManifests.Count == 0)
            {
                return OperationResult<List<ContentManifest>>.CreateFailure(
                    "Acquisition completed but no new SuperHackers manifests were found");
            }

            return OperationResult<List<ContentManifest>>.CreateSuccess(newManifests);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[SH Reconciler] Failed to acquire latest version");
            return OperationResult<List<ContentManifest>>.CreateFailure($"Failed to acquire latest version: {ex.Message}");
        }
    }

    private async Task<(bool ShouldProceed, UpdateStrategy Strategy, bool ShouldDeleteOldVersions)> PromptUserForUpdateStrategyAsync(
        UserSettings settings,
        ContentUpdateCheckResult updateResult)
    {
        var subscription = settings.GetSubscription(PublisherTypeConstants.TheSuperHackers);
        var strategy = subscription?.PreferredUpdateStrategy ?? settings.PreferredUpdateStrategy ?? UpdateStrategy.ReplaceCurrent;
        var autoUpdate = subscription?.AutoUpdateEnabled == true;
        var shouldDeleteOldVersions = subscription?.DeleteOldVersions ?? true;

        if (autoUpdate)
        {
            return (true, strategy, shouldDeleteOldVersions);
        }

        var dialogResult = await dialogService.ShowUpdateOptionDialogAsync(
            "SuperHackers Update Available",
            $"A new version of **The Super Hackers** is available ({updateResult.LatestVersion}).\n\nHow do you want to apply this update?",
            shouldDeleteOldVersions);

        if (dialogResult == null)
        {
            return (false, strategy, shouldDeleteOldVersions);
        }

        if (dialogResult.Action == "Skip")
        {
            logger.LogInformation("[SH Reconciler] User skipped version {Version}.", updateResult.LatestVersion);

            if (dialogResult.IsDoNotAskAgain)
            {
                await userSettingsService.TryUpdateAndSaveAsync(s =>
                {
                    s.SkipVersion(PublisherTypeConstants.TheSuperHackers, updateResult.LatestVersion ?? string.Empty);
                    return true;
                });
            }

            return (false, strategy, shouldDeleteOldVersions);
        }

        strategy = dialogResult.Strategy;
        shouldDeleteOldVersions = dialogResult.DeleteOldVersions;

        if (dialogResult.IsDoNotAskAgain)
        {
            logger.LogInformation("[SH Reconciler] Saving user preference for SuperHackers updates");
            await userSettingsService.TryUpdateAndSaveAsync(s =>
            {
                s.SetAutoUpdatePreference(PublisherTypeConstants.TheSuperHackers, true);
                var sub = s.GetSubscription(PublisherTypeConstants.TheSuperHackers);
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
