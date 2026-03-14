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
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Service for reconciling profiles when GeneralsOnline updates are detected.
/// When an update is found, this service updates all profiles using GeneralsOnline,
/// removes old manifests and CAS content, and prepares profiles for the new version.
/// </summary>
public class GeneralsOnlineProfileReconciler(
    ILogger<GeneralsOnlineProfileReconciler> logger,
    IGeneralsOnlineUpdateService updateService,
    IContentManifestPool manifestPool,
    IContentOrchestrator contentOrchestrator,
    IContentReconciliationService reconciliationService,
    INotificationService notificationService,
    IDialogService dialogService,
    IUserSettingsService userSettingsService,
    IGameProfileManager profileManager)
    : IGeneralsOnlineProfileReconciler, IPublisherReconciler
{
    /// <inheritdoc/>
    public string PublisherType => GeneralsOnlineConstants.PublisherType;

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> CheckAndReconcileIfNeededAsync(
        string triggeringProfileId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation(
                "[GO Reconciler] Checking for GeneralsOnline updates (triggered by profile: {ProfileId})",
                triggeringProfileId);

            // Step 1: Check for updates
            var updateResult = await updateService.CheckForUpdatesAsync(cancellationToken);

            if (!updateResult.Success)
            {
                logger.LogWarning(
                    "[GO Reconciler] Update check failed: {Error}",
                    updateResult.FirstError);
                return OperationResult<bool>.CreateFailure(
                    $"Failed to check for GeneralsOnline updates: {updateResult.FirstError}");
            }

            if (!updateResult.IsUpdateAvailable)
            {
                logger.LogInformation(
                    "[GO Reconciler] No update available. Current version: {Version}",
                    updateResult.CurrentVersion);
                return OperationResult<bool>.CreateSuccess(false);
            }

            // Check if this specific version is skipped
            var settings = userSettingsService.Get();
            if (settings.IsVersionSkipped(GeneralsOnlineConstants.PublisherType, updateResult.LatestVersion ?? string.Empty))
            {
                logger.LogInformation("[GO Reconciler] User opted to skip version {Version}. Skipping.", updateResult.LatestVersion);
                return OperationResult<bool>.CreateSuccess(false);
            }

            // Determine strategy
            var subscription = settings.GetSubscription(GeneralsOnlineConstants.PublisherType);
            UpdateStrategy strategy = subscription?.PreferredUpdateStrategy ?? settings.PreferredUpdateStrategy ?? UpdateStrategy.ReplaceCurrent;
            bool autoUpdate = subscription?.AutoUpdateEnabled == true;

            // Prompt user if preference is not set (AutoUpdate is null/false or Strategy is null/implied)
            // But we only skip dialog if AutoUpdate is TRUE.
            if (!autoUpdate)
            {
                var dialogResult = await dialogService.ShowUpdateOptionDialogAsync(
                    "Generals Online Update Available",
                    $"A new version of **Generals Online** is available ({updateResult.LatestVersion}).\n\nHow do you want to apply this update?");

                if (dialogResult == null) return OperationResult<bool>.CreateSuccess(false);

                if (dialogResult.Action == "Skip")
                {
                    logger.LogInformation("[GO Reconciler] User skipped version {Version}.", updateResult.LatestVersion);

                    // Only permanently skip if user checked "Do not ask again"
                    if (dialogResult.IsDoNotAskAgain)
                    {
                        await userSettingsService.TryUpdateAndSaveAsync(s =>
                        {
                            s.SkipVersion(GeneralsOnlineConstants.PublisherType, updateResult.LatestVersion ?? string.Empty);
                            return true;
                        });
                    }

                    return OperationResult<bool>.CreateSuccess(false);
                }

                strategy = dialogResult.Strategy;

                if (dialogResult.IsDoNotAskAgain)
                {
                    logger.LogInformation("[GO Reconciler] Saving user preference for GeneralsOnline updates");
                    await userSettingsService.TryUpdateAndSaveAsync(s =>
                    {
                        var sub = s.GetOrCreateSubscription(GeneralsOnlineConstants.PublisherType, isSubscribed: true);
                        sub.AutoUpdateEnabled = true;
                        sub.PreferredUpdateStrategy = strategy;
                        return true;
                    });
                }
            }

            // Step 2: Notify user that update is being installed
            notificationService.ShowInfo(
                "GeneralsOnline Update Found",
                $"Installing GeneralsOnline {updateResult.LatestVersion}. Please wait...",
                NotificationDurations.VeryLong);

            // Step 3: Find all GeneralsOnline manifests currently installed
            var oldManifests = await FindGeneralsOnlineManifestsAsync(cancellationToken);
            if (oldManifests.Count == 0)
            {
                logger.LogWarning("[GO Reconciler] No existing GeneralsOnline manifests found in pool");
            }

            logger.LogInformation(
                "[GO Reconciler] Found {Count} existing GeneralsOnline manifests to replace",
                oldManifests.Count);

            // Step 4: Download and acquire new content
            var acquireResult = await AcquireLatestVersionAsync(oldManifests, cancellationToken);
            if (!acquireResult.Success)
            {
                notificationService.ShowError(
                    "GeneralsOnline Update Failed",
                    $"Failed to download update: {acquireResult.FirstError}",
                    NotificationDurations.Critical);

                return OperationResult<bool>.CreateFailure(
                    $"Failed to acquire new GeneralsOnline version: {acquireResult.FirstError}");
            }

            var newManifests = acquireResult.Data!;
            logger.LogInformation(
                "[GO Reconciler] Successfully acquired {Count} new manifests",
                newManifests.Count);

            // Step 5: Update affected profiles based on strategy
            int profilesUpdated = 0;
            bool anyFailure = false;

            // CRITICAL: If strategy is CreateNewProfile, we MUST keep old versions because existing profiles still use them.
            // If strategy is ReplaceCurrent, we delete if the subscription/user settings allow it.
            bool shouldDeleteOldVersions = (strategy != UpdateStrategy.CreateNewProfile) && (subscription?.DeleteOldVersions ?? true);

            if (strategy == UpdateStrategy.CreateNewProfile)
            {
                var createResult = await CreateNewProfilesForUpdateAsync(oldManifests, newManifests, updateResult.LatestVersion ?? "Unknown", cancellationToken);
                if (createResult.Success) profilesUpdated = createResult.Data;
                else notificationService.ShowWarning("GeneralsOnline Update Partial", $"Failed to create some new profiles: {createResult.FirstError}", NotificationDurations.VeryLong);
            }
            else
            {
                // ReplaceCurrent
                var manifestMapping = BuildManifestMapping(oldManifests, newManifests);

                // CRITICAL: Pass removeOld = false to prevent premature deletion
                // We'll handle deletion after MapPack enforcement succeeds
                var bulkUpdateResult = await reconciliationService.OrchestrateBulkUpdateAsync(
                    manifestMapping,
                    removeOld: false,
                    cancellationToken);

                if (bulkUpdateResult.Success)
                {
                    profilesUpdated = bulkUpdateResult.Data?.ProfilesUpdated ?? 0;
                    var failedCount = bulkUpdateResult.Data?.FailedProfilesCount ?? 0;
                    if (failedCount > 0)
                    {
                        anyFailure = true;
                        notificationService.ShowWarning("Generals Online Update Partial", $"{failedCount} profiles could not be updated.", NotificationDurations.VeryLong);
                    }
                }
                else
                {
                    anyFailure = true;
                    notificationService.ShowWarning("GeneralsOnline Update Partial", $"Some profiles could not be updated: {bulkUpdateResult.FirstError}", NotificationDurations.VeryLong);
                    return OperationResult<bool>.CreateFailure($"Bulk update failed: {bulkUpdateResult.FirstError}");
                }
            }

            // Step 5.5: Enforce MapPack dependency (add MapPack to profile if missing)
            // This applies to BOTH strategies (New profiles need it too, and existing ones need it)
            // But CreateNewProfilesForUpdateAsync handles it internally for new profiles.
            // Better to run it broadly just in case.
            var enforceResult = await EnforceMapPackDependencyAsync(newManifests, cancellationToken);
            if (!enforceResult.Success)
            {
                anyFailure = true;
                notificationService.ShowWarning("GeneralsOnline Update Partial", $"Failed to enforce MapPack dependency: {enforceResult.FirstError}", NotificationDurations.VeryLong);
                logger.LogWarning("[GO Reconciler] MapPack enforcement failed: {Error}. Skipping old manifest deletion.", enforceResult.FirstError);
            }

            // Step 6: Delete old manifests only if enforcement succeeded and deletion is enabled
            if (shouldDeleteOldVersions && !anyFailure)
            {
                logger.LogInformation("[GO Reconciler] Deleting old manifests after successful enforcement");
                var oldManifestIds = oldManifests.Select(m => m.Id).ToList();
                var removalResult = await reconciliationService.OrchestrateBulkRemovalAsync(oldManifestIds, cancellationToken);
                if (!removalResult.Success)
                {
                    logger.LogWarning("[GO Reconciler] Failed to remove old manifests: {Error}", removalResult.FirstError);
                    anyFailure = true;
                }
            }
            else if (shouldDeleteOldVersions && anyFailure)
            {
                logger.LogWarning("[GO Reconciler] Skipping old manifest deletion due to previous failures to preserve content integrity.");
            }

            // Step 7: Run garbage collection (only if old versions were deleted AND no failures occurred)
            // If some profiles failed, GC could delete files they still rely on.
            if (shouldDeleteOldVersions && !anyFailure)
            {
                await reconciliationService.ScheduleGarbageCollectionAsync(false, cancellationToken);
            }
            else if (shouldDeleteOldVersions && anyFailure)
            {
                logger.LogWarning("[GO Reconciler] Skipping scheduled GC due to partial update failure to avoid deleting referenced content.");
            }

            // Step 8: Show success notification
            notificationService.ShowSuccess(
                "GeneralsOnline Updated",
                $"Successfully updated to version {updateResult.LatestVersion}. {profilesUpdated} profiles {(strategy == UpdateStrategy.CreateNewProfile ? "created" : "updated")}.",
                NotificationDurations.Long);

            logger.LogInformation(
                "[GO Reconciler] Reconciliation complete. Processed {ProfileCount} profiles with strategy {Strategy}",
                profilesUpdated,
                strategy);

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("[GO Reconciler] Reconciliation cancelled");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[GO Reconciler] Reconciliation failed unexpectedly");
            notificationService.ShowError(
                "GeneralsOnline Update Error",
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
            // Find corresponding new manifest by matching variant
            var newManifest = newManifests
                .OrderByDescending(n => GameVersionHelper.GetGeneralsOnlineSortableVersion(n.Version))
                .FirstOrDefault(n =>
                    (n.ContentType == oldManifest.ContentType ||
                     (oldManifest.ContentType == Core.Models.Enums.ContentType.Mod && n.ContentType == Core.Models.Enums.ContentType.GameClient)) &&
                    MatchesByVariant(oldManifest, n));

            if (newManifest != null)
            {
                mapping[oldManifest.Id.Value] = newManifest.Id.Value;
            }
        }

        return mapping;
    }

    /// <summary>
    /// Checks if two manifests refer to the same variant (30hz, 60hz, or quickmatch-maps).
    /// </summary>
    private static bool MatchesByVariant(ContentManifest oldManifest, ContentManifest newManifest)
    {
        var oldVariant = ExtractVariant(oldManifest);
        var newVariant = ExtractVariant(newManifest);
        return string.Equals(oldVariant, newVariant, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts the variant suffix from a manifest ID.
    /// </summary>
    private static string? ExtractVariant(string manifestId)
    {
        var parts = manifestId.Split('.');
        if (parts.Length == 0) return null;

        var lastPart = parts[^1];

        if (lastPart.Equals(GeneralsOnlineConstants.Variant60HzSuffix, StringComparison.OrdinalIgnoreCase) ||
            lastPart.Equals(GeneralsOnlineConstants.QuickMatchMapPackSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return lastPart.ToLowerInvariant();
        }

        // Check for legacy ID formats
        if (lastPart.Equals("generalsonlinezh-60", StringComparison.OrdinalIgnoreCase))
        {
            return GeneralsOnlineConstants.Variant60HzSuffix;
        }

        if (lastPart.Equals("generalsonlinezh", StringComparison.OrdinalIgnoreCase))
        {
            return GeneralsOnlineConstants.Variant60HzSuffix;
        }

        // Check for map pack variations
        if (lastPart.Contains("quickmatchmaps", StringComparison.OrdinalIgnoreCase) ||
            lastPart.Contains("generalsonlinemaps", StringComparison.OrdinalIgnoreCase))
        {
            return GeneralsOnlineConstants.QuickMatchMapPackSuffix;
        }

        // Fallback for legacy ID formats or unrecognized patterns.
        // We take the last dot-separated segment as the variant.
        // While this could theoretically cause collisions (e.g. foo.bar.baz and qux.baz),
        // it provides necessary backward compatibility for earlier manifest ID schemes.
        return parts.Length > 1 ? lastPart.ToLowerInvariant() : null;
    }

    /// <summary>
    /// Extracts the variant suffix from a manifest, checking tags first for explicit variant detection.
    /// </summary>
    /// <param name="manifest">The manifest to extract variant from.</param>
    /// <returns>The variant suffix, or null if not detected.</returns>
    private static string? ExtractVariant(ContentManifest manifest)
    {
        // First, check for explicit variant tags in metadata (preferred method for new manifests)
        if (manifest.Metadata?.Tags != null)
        {
            foreach (var tag in manifest.Metadata.Tags)
            {
                if (tag.Equals(GeneralsOnlineVariantTags.Tag60Hz, StringComparison.OrdinalIgnoreCase))
                {
                    return GeneralsOnlineConstants.Variant60HzSuffix;
                }

                if (tag.Equals(GeneralsOnlineVariantTags.TagQuickMatchMaps, StringComparison.OrdinalIgnoreCase))
                {
                    return GeneralsOnlineConstants.QuickMatchMapPackSuffix;
                }
            }
        }

        // Fallback to explicit metadata if available (Check TargetGame for default variant association)
        if (manifest.TargetGame == GameType.ZeroHour && manifest.ContentType == ContentType.GameClient)
        {
            return GeneralsOnlineConstants.DefaultVariantSuffix;
        }

        // Fallback to ID-based detection for legacy manifests
        return ExtractVariant(manifest.Id.Value);
    }

    /// <summary>
    /// Finds all GeneralsOnline manifests currently in the manifest pool.
    /// </summary>
    private async Task<List<ContentManifest>> FindGeneralsOnlineManifestsAsync(
        CancellationToken cancellationToken)
    {
        var manifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
        if (!manifestsResult.Success || manifestsResult.Data == null)
        {
            return [];
        }

        return [.. manifestsResult.Data
            .Where(m =>
                !m.Id.Value.Contains(".local.", StringComparison.OrdinalIgnoreCase) && // Exclude local content
                (m.Publisher?.PublisherType?.Equals(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) == true ||
                  m.Id.Value.Contains(".generalsonline.", StringComparison.OrdinalIgnoreCase) ||
                  (m.Name?.Contains("GeneralsOnline", StringComparison.OrdinalIgnoreCase) == true)))];
    }

    /// <summary>
    /// Acquires the latest GeneralsOnline version by searching and downloading.
    /// </summary>
    private async Task<OperationResult<List<ContentManifest>>> AcquireLatestVersionAsync(
        List<ContentManifest> oldManifests,
        CancellationToken cancellationToken)
    {
        try
        {
            // Search for Game Client
            var clientQuery = new ContentSearchQuery
            {
                ProviderName = GeneralsOnlineConstants.PublisherType,
                ContentType = ContentType.GameClient,
                TargetGame = GameType.ZeroHour,
            };

            var clientResult = await contentOrchestrator.SearchAsync(clientQuery, cancellationToken);

            // Search for Map Packs (required dependency)
            var mapPackQuery = new ContentSearchQuery
            {
                ProviderName = GeneralsOnlineConstants.PublisherType,
                ContentType = ContentType.MapPack,
                TargetGame = GameType.ZeroHour,
            };

            var mapPackResult = await contentOrchestrator.SearchAsync(mapPackQuery, cancellationToken);

            var allResults = new List<ContentSearchResult>();

            if (clientResult.Success && clientResult.Data != null)
            {
                allResults.AddRange(clientResult.Data);
            }

            if (mapPackResult.Success && mapPackResult.Data != null)
            {
                allResults.AddRange(mapPackResult.Data);
            }

            if (allResults.Count == 0)
            {
                return OperationResult<List<ContentManifest>>.CreateFailure(
                    "No GeneralsOnline content found from provider");
            }

            foreach (var result in allResults)
            {
                var acquireOp = await contentOrchestrator.AcquireContentAsync(result, progress: null, cancellationToken);
                if (!acquireOp.Success)
                {
                    logger.LogError(
                        "[GO:Reconciler] Failed to acquire content {ContentId}: {Error}",
                        result.Id,
                        acquireOp.FirstError);

                    return OperationResult<List<ContentManifest>>.CreateFailure(
                        $"Failed to acquire content {result.Id}: {acquireOp.FirstError}");
                }
            }

            var allGoManifests = await FindGeneralsOnlineManifestsAsync(cancellationToken);
            var oldIds = oldManifests.Select(m => m.Id.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newManifests = allGoManifests.Where(m => !oldIds.Contains(m.Id.Value)).ToList();

            if (newManifests.Count == 0)
            {
                return OperationResult<List<ContentManifest>>.CreateFailure(
                    "Acquisition completed but no new GeneralsOnline manifests were found in pool");
            }

            return OperationResult<List<ContentManifest>>.CreateSuccess(newManifests);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[GO Reconciler] Failed to acquire latest version");
            return OperationResult<List<ContentManifest>>.CreateFailure(
                $"Failed to acquire latest version: {ex.Message}");
        }
    }

    /// <summary>
    /// Enforces that profiles using the new GeneralsOnline client also have the new MapPack.
    /// </summary>
    private async Task<OperationResult> EnforceMapPackDependencyAsync(
        List<ContentManifest> newManifests,
        CancellationToken cancellationToken)
    {
        // 1. Identify the new MapPack ID and GameClient IDs
        var newMapPack = newManifests.FirstOrDefault(m => m.ContentType == ContentType.MapPack);
        var newGameClientIds = newManifests
            .Where(m => m.ContentType == ContentType.GameClient)
            .Select(m => m.Id.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (newMapPack == null || newGameClientIds.Count == 0)
        {
            logger.LogInformation("[GO Reconciler] No MapPack (found: {HasMapPack}) or GameClient ({ClientCount}) found for dependency enforcement.", newMapPack != null, newGameClientIds.Count);
            return OperationResult.CreateSuccess();
        }

        var newMapPackId = newMapPack.Id.Value;

        // 2. Get all profiles
        var allProfilesResult = await profileManager.GetAllProfilesAsync(cancellationToken);
        if (!allProfilesResult.Success || allProfilesResult.Data == null)
        {
            logger.LogWarning("[GO Reconciler] Failed to retrieve profiles for dependency enforcement.");
            return OperationResult.CreateFailure("Failed to retrieve profiles for dependency enforcement");
        }

        // 3. Iterate profiles and patch if needed
        var errors = new List<string>();
        foreach (var profile in allProfilesResult.Data)
        {
            // Check if profile uses one of the new GameClients
            bool isGeneralsOnline = profile.GameClient != null &&
                                    newGameClientIds.Contains(profile.GameClient.Id);

            // Check if profile already has the new MapPack
            bool hasMapPack = profile.EnabledContentIds?.Contains(newMapPackId, StringComparer.OrdinalIgnoreCase) ?? false;

            if (isGeneralsOnline && !hasMapPack)
            {
                logger.LogInformation("[GO Reconciler] Adding required MapPack {MapPackId} to profile {ProfileName}", newMapPackId, profile.Name);

                var newEnabledContent = profile.EnabledContentIds != null
                    ? [.. profile.EnabledContentIds]
                    : new List<string>();
                newEnabledContent.Add(newMapPackId);

                var updateRequest = new Core.Models.GameProfile.UpdateProfileRequest
                {
                    EnabledContentIds = newEnabledContent,
                };

                var updateResult = await profileManager.UpdateProfileAsync(profile.Id, updateRequest, cancellationToken);
                if (!updateResult.Success)
                {
                    var errorMsg = $"Failed to update profile '{profile.Name}': {updateResult.FirstError}";
                    errors.Add(errorMsg);
                    logger.LogError("[GO Reconciler] {Error}", errorMsg);
                }
            }
        }

        if (errors.Count > 0)
        {
            return OperationResult.CreateFailure($"Failed to update {errors.Count} profiles: {string.Join("; ", errors.Take(3))}{(errors.Count > 3 ? "..." : string.Empty)}");
        }

        return OperationResult.CreateSuccess();
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
        if (!allProfiles.Success || allProfiles.Data == null)
        {
            return OperationResult<int>.CreateFailure("Failed to retrieve profiles for creating new profiles");
        }

        foreach (var profile in allProfiles.Data)
        {
            // Check if profile is relevant (uses any Old GeneralsOnline manifest)
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
                            // Keep non-GO content
                            newEnabledContent.Add(id);
                        }
                    }
                }

                cloneRequest.EnabledContentIds = newEnabledContent;

                var createResult = await profileManager.CreateProfileAsync(cloneRequest, cancellationToken);
                if (createResult.Success)
                {
                    createdCount++;
                    logger.LogInformation("[GO Reconciler] Created new profile '{Name}' for update", cloneRequest.Name);
                }
                else
                {
                    logger.LogError("[GO Reconciler] Failed to create new profile for update: {Error}", createResult.FirstError);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[GO Reconciler] Error creating profile for update");
            }
        }

        return OperationResult<int>.CreateSuccess(createdCount);
    }
}
