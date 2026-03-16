using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
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
    public async Task<OperationResult<bool>> CheckAndReconcileIfNeededAsync(
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
                return OperationResult<bool>.CreateFailure(
                    $"Failed to check for SuperHackers updates: {updateResult.FirstError}");
            }

            if (!updateResult.IsUpdateAvailable)
            {
                logger.LogInformation(
                    "[SH Reconciler] No update available. Current version: {Version}",
                    updateResult.CurrentVersion);
                return OperationResult<bool>.CreateSuccess(false);
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
                return OperationResult<bool>.CreateSuccess(false);
            }

            // Determine strategy
            var subscription = settings.GetSubscription(PublisherTypeConstants.TheSuperHackers);
            UpdateStrategy strategy = subscription?.PreferredUpdateStrategy ?? settings.PreferredUpdateStrategy ?? UpdateStrategy.ReplaceCurrent;
            bool autoUpdate = subscription?.AutoUpdateEnabled == true;
            bool shouldDeleteOldVersions = subscription?.DeleteOldVersions ?? true;

            if (!autoUpdate)
            {
                var dialogResult = await dialogService.ShowUpdateOptionDialogAsync(
                    "SuperHackers Update Available",
                    $"A new version of **The Super Hackers** is available ({updateResult.LatestVersion}).\n\nHow do you want to apply this update?");

                if (dialogResult == null) return OperationResult<bool>.CreateSuccess(false);

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

                    return OperationResult<bool>.CreateSuccess(false);
                }

                strategy = dialogResult.Strategy;

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
                        }

                        return true;
                    });
                }
            }

            // Notify user that update is being installed
            notificationService.ShowInfo(
                "SuperHackers Update Found",
                $"Installing SuperHackers {updateResult.LatestVersion}. Please wait...",
                NotificationDurations.VeryLong);

            // Find existing installed manifests
            var oldManifests = await FindSuperHackersManifestsAsync(cancellationToken);

            logger.LogInformation(
                "[SH Reconciler] Found {Count} existing SuperHackers manifests to replace",
                oldManifests.Count);

            // Acquire new content
            var acquireResult = await AcquireLatestVersionAsync(oldManifests, cancellationToken);
            if (!acquireResult.Success)
            {
                notificationService.ShowError(
                    "SuperHackers Update Failed",
                    $"Failed to download update: {acquireResult.FirstError}",
                    NotificationDurations.Critical);

                return OperationResult<bool>.CreateFailure(
                    $"Failed to acquire new SuperHackers version: {acquireResult.FirstError}");
            }

            var newManifests = acquireResult.Data!;

            // Update profiles based on strategy
            int profilesUpdated = 0;
            bool anyFailure = false;

            if (strategy == UpdateStrategy.CreateNewProfile)
            {
                // keep old versions when creating new profiles
                shouldDeleteOldVersions = false;

                var createResult = await CreateNewProfilesForUpdateAsync(oldManifests, newManifests, updateResult.LatestVersion ?? "Unknown", cancellationToken);
                if (createResult.Success)
                {
                    profilesUpdated = createResult.Data;
                }
                else
                {
                    anyFailure = true;
                    notificationService.ShowWarning("SuperHackers Update Partial", $"Failed to create some new profiles: {createResult.FirstError}");
                }
            }
            else
            {
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
                        notificationService.ShowWarning("SuperHackers Update Partial", $"{bulkUpdateResult.Data.FailedProfilesCount} profiles could not be updated.", NotificationDurations.VeryLong);
                    }
                }
                else
                {
                    anyFailure = true;
                    notificationService.ShowWarning("SuperHackers Update Partial", $"Some profiles could not be updated: {bulkUpdateResult.FirstError}", NotificationDurations.VeryLong);
                    return OperationResult<bool>.CreateFailure($"Bulk update failed: {bulkUpdateResult.FirstError}");
                }
            }

            // Run garbage collection only if old versions were deleted AND no failures occurred
            // If some profiles failed, GC could delete files they still rely on.
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

            return OperationResult<bool>.CreateSuccess(true);
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
            return OperationResult<bool>.CreateFailure($"Reconciliation failed: {ex.Message}");
        }
    }

    private static Dictionary<string, string> BuildManifestMapping(
        List<ContentManifest> oldManifests,
        List<ContentManifest> newManifests)
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

    private async Task<OperationResult<List<ContentManifest>>> AcquireLatestVersionAsync(
        List<ContentManifest> oldManifests,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = new ContentSearchQuery
            {
                ProviderName = PublisherTypeConstants.TheSuperHackers,
                ContentType = ContentType.GameClient,
            };

            var searchResult = await contentOrchestrator.SearchAsync(query, cancellationToken);

            if (!searchResult.Success || searchResult.Data == null || !searchResult.Data.Any())
            {
                 return OperationResult<List<ContentManifest>>.CreateFailure(
                    "No SuperHackers content found from provider");
            }

            foreach (var result in searchResult.Data)
            {
                var acquireOp = await contentOrchestrator.AcquireContentAsync(result, progress: null, cancellationToken);
                if (!acquireOp.Success)
                {
                    logger.LogError(
                        "[SH:Reconciler] Failed to acquire content {ContentId}: {Error}",
                        result.Id,
                        acquireOp.FirstError);

                    return OperationResult<List<ContentManifest>>.CreateFailure(
                        $"Failed to acquire SuperHackers content {result.Id}: {acquireOp.FirstError}");
                }
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
        catch (Exception ex)
        {
            logger.LogError(ex, "[SH Reconciler] Failed to acquire latest version");
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
            // Check if profile is relevant (uses any Old SH manifest)
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
                   GameClient = profile.GameClient, // Default to old, override below if mapping exists
                };

                // Update GameClient if mapped
                if (profile.GameClient != null && manifestMapping.TryGetValue(profile.GameClient.Id, out var newClientId))
                {
                    var matchedManifest = newManifests.FirstOrDefault(m => m.Id.Value == newClientId);
                    if (matchedManifest != null)
                    {
                        cloneRequest.GameClient = new Core.Models.GameClients.GameClient
                        {
                            Id = matchedManifest.Id.Value,
                            Name = matchedManifest.Name,
                            Version = matchedManifest.Version ?? string.Empty,
                            GameType = matchedManifest.TargetGame,
                            SourceType = matchedManifest.ContentType,
                            PublisherType = matchedManifest.Publisher?.PublisherType,
                            InstallationId = profile.GameClient.InstallationId,
                        };
                    }
                }
                else if (profile.GameClient != null)
                {
                    logger.LogDebug("No manifest mapping found for GameClient '{ClientId}' in profile '{ProfileName}'. Preserving existing client.", profile.GameClient.Id, profile.Name);
                }

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
                    logger.LogInformation("[SH Reconciler] Created new profile '{Name}' for update", cloneRequest.Name);
                }
                else
                {
                    logger.LogError("[SH Reconciler] Failed to create new profile for update: {Error}", createResult.FirstError);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[SH Reconciler] Error creating profile for update");
            }
        }

        return OperationResult<int>.CreateSuccess(createdCount);
    }
}
