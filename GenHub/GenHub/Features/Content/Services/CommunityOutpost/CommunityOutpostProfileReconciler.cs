using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Service for reconciling profiles when Community Outpost updates are detected.
/// Handles the full update flow including user prompts, content acquisition,
/// profile reconciliation, and cleanup.
/// </summary>
public class CommunityOutpostProfileReconciler(
    ILogger<CommunityOutpostProfileReconciler> logger,
    ICommunityOutpostUpdateService updateService,
    IContentManifestPool manifestPool,
    IContentOrchestrator contentOrchestrator,
    IContentReconciliationService reconciliationService,
    INotificationService notificationService,
    IDialogService dialogService,
    IUserSettingsService userSettingsService,
    IGameProfileManager profileManager)
    : ICommunityOutpostProfileReconciler, IPublisherReconciler
{
    /// <inheritdoc/>
    public string PublisherType => CommunityOutpostConstants.PublisherType;

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> CheckAndReconcileIfNeededAsync(
        string triggeringProfileId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation(
                "[CO Reconciler] Checking for Community Outpost updates (triggered by profile: {ProfileId})",
                triggeringProfileId);

            // Step 1: Check for updates
            var updateResult = await updateService.CheckForUpdatesAsync(cancellationToken);

            if (!updateResult.Success)
            {
                logger.LogWarning(
                    "[CO Reconciler] Update check failed: {Error}",
                    updateResult.FirstError);
                return OperationResult<bool>.CreateFailure(
                    $"Failed to check for Community Outpost updates: {updateResult.FirstError}");
            }

            if (!updateResult.IsUpdateAvailable)
            {
                logger.LogInformation(
                    "[CO Reconciler] No update available. Current version: {Version}",
                    updateResult.CurrentVersion);
                return OperationResult<bool>.CreateSuccess(false);
            }

            logger.LogInformation(
                "[CO Reconciler] Update available! Current: {CurrentVersion}, Latest: {LatestVersion}",
                updateResult.CurrentVersion,
                updateResult.LatestVersion);

            // Check if this specific version is skipped
            var settings = userSettingsService.Get();
            if (settings.IsVersionSkipped(CommunityOutpostConstants.PublisherType, updateResult.LatestVersion ?? string.Empty))
            {
                logger.LogInformation("[CO Reconciler] User opted to skip version {Version}. Skipping.", updateResult.LatestVersion);
                return OperationResult<bool>.CreateSuccess(false);
            }

            // Determine strategy
            var subscription = settings.GetSubscription(CommunityOutpostConstants.PublisherType);
            UpdateStrategy strategy = subscription?.PreferredUpdateStrategy ?? settings.PreferredUpdateStrategy ?? UpdateStrategy.ReplaceCurrent;
            bool autoUpdate = subscription?.AutoUpdateEnabled == true;
            bool shouldDeleteOldVersions = subscription?.DeleteOldVersions ?? true;

            if (!autoUpdate)
            {
                var dialogResult = await dialogService.ShowUpdateOptionDialogAsync(
                    "Community Patch Update Available",
                    $"A new version of **Community Patch** is available (v{updateResult.LatestVersion}).\n\nHow do you want to apply this update?");

                if (dialogResult == null) return OperationResult<bool>.CreateSuccess(false);

                if (dialogResult.Action == "Skip")
                {
                    logger.LogInformation("[CO Reconciler] User skipped version {Version}.", updateResult.LatestVersion);

                    if (dialogResult.IsDoNotAskAgain)
                    {
                        await userSettingsService.TryUpdateAndSaveAsync(s =>
                        {
                            s.SkipVersion(CommunityOutpostConstants.PublisherType, updateResult.LatestVersion ?? string.Empty);
                            return true;
                        });
                    }

                    return OperationResult<bool>.CreateSuccess(false);
                }

                strategy = dialogResult.Strategy;

                if (dialogResult.IsDoNotAskAgain)
                {
                    logger.LogInformation("[CO Reconciler] Saving user preference for Community Patch updates");
                    await userSettingsService.TryUpdateAndSaveAsync(s =>
                    {
                        s.SetAutoUpdatePreference(CommunityOutpostConstants.PublisherType, true);
                        var sub = s.GetSubscription(CommunityOutpostConstants.PublisherType);
                        if (sub != null)
                        {
                            sub.PreferredUpdateStrategy = strategy;
                        }

                        return true;
                    });
                }
            }

            // Step 2: Notify user that update is being installed
            notificationService.ShowInfo(
                "Community Patch Update Found",
                $"Installing Community Patch {updateResult.LatestVersion}. Please wait...",
                NotificationDurations.VeryLong);

            // Step 3: Find all Community Outpost manifests currently installed
            var oldManifests = await FindCommunityOutpostManifestsAsync(cancellationToken);
            if (oldManifests.Count == 0)
            {
                logger.LogWarning("[CO Reconciler] No existing Community Outpost manifests found in pool");
            }

            logger.LogInformation(
                "[CO Reconciler] Found {Count} existing Community Outpost manifests to replace",
                oldManifests.Count);

            // Step 4: Download and acquire new content
            var acquireResult = await AcquireLatestVersionAsync(oldManifests, cancellationToken);
            if (!acquireResult.Success)
            {
                notificationService.ShowError(
                    "Community Patch Update Failed",
                    $"Failed to download update: {acquireResult.FirstError}",
                    NotificationDurations.Critical);

                return OperationResult<bool>.CreateFailure(
                    $"Failed to acquire new Community Patch version: {acquireResult.FirstError}");
            }

            var newManifests = acquireResult.Data!;
            logger.LogInformation(
                "[CO Reconciler] Successfully acquired {Count} new manifests",
                newManifests.Count);

            // Step 5: Update affected profiles based on strategy
            int profilesUpdated = 0;
            bool anyFailure = false;

            if (strategy == UpdateStrategy.CreateNewProfile)
            {
                // Force keep old versions if creating new profiles
                shouldDeleteOldVersions = false;

                var createResult = await CreateNewProfilesForUpdateAsync(oldManifests, newManifests, updateResult.LatestVersion ?? "Unknown", cancellationToken);
                if (createResult.Success)
                {
                    profilesUpdated = createResult.Data;
                }
                else
                {
                    anyFailure = true;
                    notificationService.ShowWarning("Community Patch Update Partial", $"Failed to create some new profiles: {createResult.FirstError}");
                }
            }
            else
            {
                // ReplaceCurrent
                var manifestMapping = BuildManifestMapping(oldManifests, newManifests);
                var bulkUpdateResult = await reconciliationService.OrchestrateBulkUpdateAsync(
                    manifestMapping,
                    shouldDeleteOldVersions,
                    cancellationToken);

                if (bulkUpdateResult.Success)
                {
                    profilesUpdated = bulkUpdateResult.Data.ProfilesUpdated;
                    if (bulkUpdateResult.Data.FailedProfilesCount > 0)
                    {
                        anyFailure = true;
                        notificationService.ShowWarning("Community Patch Update Partial", $"{bulkUpdateResult.Data.FailedProfilesCount} profiles could not be updated. Check logs for details.");
                    }
                }
                else
                {
                    anyFailure = true;
                    notificationService.ShowWarning("Community Patch Update Partial", $"Some profiles could not be updated: {bulkUpdateResult.FirstError}");
                    return OperationResult<bool>.CreateFailure($"Bulk update failed: {bulkUpdateResult.FirstError}");
                }
            }

            // Step 6: Run garbage collection (only if old versions were deleted AND no failures occurred)
            // If some profiles failed, GC could delete files they still rely on.
            if (shouldDeleteOldVersions && !anyFailure)
            {
                await reconciliationService.ScheduleGarbageCollectionAsync(false, cancellationToken);
            }
            else if (shouldDeleteOldVersions && anyFailure)
            {
                logger.LogWarning("[CO Reconciler] Skipping scheduled GC due to partial update failure to avoid deleting referenced content.");
            }

            // Step 7: Show success notification
            notificationService.ShowSuccess(
                "Community Patch Updated",
                $"Successfully updated to version {updateResult.LatestVersion}. {profilesUpdated} profiles {(strategy == UpdateStrategy.CreateNewProfile ? "created" : "updated")}.",
                NotificationDurations.Long);

            logger.LogInformation(
                "[CO Reconciler] Reconciliation complete. Processed {ProfileCount} profiles with strategy {Strategy}",
                profilesUpdated,
                strategy);

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("[CO Reconciler] Reconciliation cancelled");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[CO Reconciler] Reconciliation failed unexpectedly");
            notificationService.ShowError(
                "Community Patch Update Error",
                $"An error occurred during update: {ex.Message}",
                NotificationDurations.Critical);
            return OperationResult<bool>.CreateFailure($"Reconciliation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds a mapping from old manifest IDs to new manifest IDs.
    /// </summary>
    private static Dictionary<string, string> BuildManifestMapping(
        List<ContentManifest> oldManifests,
        List<ContentManifest> newManifests)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var oldManifest in oldManifests)
        {
            // Find corresponding new manifest by matching content type
            var newManifest = newManifests.FirstOrDefault(n =>
                n.ContentType == oldManifest.ContentType &&
                n.Publisher?.PublisherType == oldManifest.Publisher?.PublisherType);

            if (newManifest != null)
            {
                mapping[oldManifest.Id.Value] = newManifest.Id.Value;
            }
        }

        return mapping;
    }

    /// <summary>
    /// Finds all Community Outpost manifests currently in the manifest pool.
    /// </summary>
    private async Task<List<ContentManifest>> FindCommunityOutpostManifestsAsync(
        CancellationToken cancellationToken)
    {
        var manifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
        if (!manifestsResult.Success || manifestsResult.Data == null)
        {
            return [];
        }

        return [.. manifestsResult.Data
            .Where(m =>
                m.Publisher?.PublisherType?.Equals(CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase) == true)];
    }

    /// <summary>
    /// Acquires the latest Community Outpost version by searching and downloading.
    /// </summary>
    private async Task<OperationResult<List<ContentManifest>>> AcquireLatestVersionAsync(
        List<ContentManifest> oldManifests,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = new ContentSearchQuery
            {
                ProviderName = CommunityOutpostConstants.PublisherType,
            };

            var searchResult = await contentOrchestrator.SearchAsync(query, cancellationToken);

            if (!searchResult.Success || searchResult.Data == null || !searchResult.Data.Any())
            {
                return OperationResult<List<ContentManifest>>.CreateFailure(
                    "No Community Outpost content found from provider");
            }

            foreach (var result in searchResult.Data)
            {
                var acquireOp = await contentOrchestrator.AcquireContentAsync(result, progress: null, cancellationToken);
                if (!acquireOp.Success)
                {
                    logger.LogError(
                        "[CO:Reconciler] Failed to acquire content {ContentId}: {Error}",
                        result.Id,
                        acquireOp.FirstError);

                    return OperationResult<List<ContentManifest>>.CreateFailure(
                        $"Failed to acquire Community Patch content {result.Id}: {acquireOp.FirstError}");
                }
            }

            var allManifests = await FindCommunityOutpostManifestsAsync(cancellationToken);
            var oldIds = oldManifests.Select(m => m.Id.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var newManifests = allManifests
                .Where(m => !oldIds.Contains(m.Id.Value))
                .ToList();

            if (newManifests.Count == 0)
            {
                return OperationResult<List<ContentManifest>>.CreateFailure(
                    "Acquisition completed but no new Community Outpost manifests were found in the pool");
            }

            return OperationResult<List<ContentManifest>>.CreateSuccess(newManifests);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[CO Reconciler] Failed to acquire latest version");
            return OperationResult<List<ContentManifest>>.CreateFailure($"Failed to acquire latest version: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates new profiles for the update instead of replacing existing ones.
    /// </summary>
    private async Task<OperationResult<int>> CreateNewProfilesForUpdateAsync(
        List<ContentManifest> oldManifests,
        List<ContentManifest> newManifests,
        string newVersion,
        CancellationToken cancellationToken)
    {
        var oldIds = oldManifests.Select(m => m.Id.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var manifestMapping = BuildManifestMapping(oldManifests, newManifests);
        int createdCount = 0;

        var allProfiles = await profileManager.GetAllProfilesAsync(cancellationToken);
        if (!allProfiles.Success || allProfiles.Data == null) return OperationResult<int>.CreateSuccess(0);

        foreach (var profile in allProfiles.Data)
        {
            // Check if profile is relevant (uses any Old CO manifest)
            bool isRelevant = (profile.GameClient != null && oldIds.Contains(profile.GameClient.Id)) ||
                              (profile.EnabledContentIds?.Any(id => oldIds.Contains(id)) == true);

            if (!isRelevant) continue;

            try
            {
                // Clone the profile
                var cloneRequest = new Core.Models.GameProfile.CreateProfileRequest
                {
                   Name = $"{profile.Name} (v{newVersion})",
                   GameInstallationId = profile.GameInstallationId,
                   WorkspaceStrategy = profile.WorkspaceStrategy,
                   GameClient = profile.GameClient,
                };

                // Calculate new content IDs
                var newEnabledContent = new List<string>();
                if (profile.EnabledContentIds != null)
                {
                    foreach (var id in profile.EnabledContentIds)
                    {
                        if (manifestMapping.TryGetValue(id, out var newId))
                        {
                            newEnabledContent.Add(newId);
                        }
                        else
                        {
                            newEnabledContent.Add(id);
                        }
                    }
                }

                cloneRequest.EnabledContentIds = newEnabledContent;

                var createResult = await profileManager.CreateProfileAsync(cloneRequest, cancellationToken);
                if (createResult.Success)
                {
                    createdCount++;
                    logger.LogInformation("[CO Reconciler] Created new profile '{Name}' for update", cloneRequest.Name);
                }
                else
                {
                    logger.LogError("[CO Reconciler] Failed to create new profile for update: {Error}", createResult.FirstError);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[CO Reconciler] Error creating profile for update");
            }
        }

        return OperationResult<int>.CreateSuccess(createdCount);
    }
}
