using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Service for reconciling profiles when GeneralsOnline updates are detected.
/// When an update is found, this service updates all profiles using GeneralsOnline,
/// removes old manifests and CAS content, and prepares profiles for the new version.
/// </summary>
public class GeneralsOnlineProfileReconciler(
    ILogger<GeneralsOnlineProfileReconciler> logger,
    GeneralsOnlineUpdateService updateService,
    IGameProfileManager profileManager,
    IContentManifestPool manifestPool,
    IContentOrchestrator contentOrchestrator,
    IWorkspaceManager workspaceManager,
    INotificationService notificationService)
    : IGeneralsOnlineProfileReconciler
    {
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

            logger.LogInformation(
                "[GO Reconciler] Update available! Current: {CurrentVersion}, Latest: {LatestVersion}",
                updateResult.CurrentVersion,
                updateResult.LatestVersion);

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

            // Step 5: Update all affected profiles
            var updateProfilesResult = await UpdateAllAffectedProfilesAsync(
                oldManifests,
                newManifests,
                cancellationToken);

            if (!updateProfilesResult.Success)
            {
                notificationService.ShowWarning(
                    "GeneralsOnline Update Partial",
                    $"Some profiles could not be updated: {updateProfilesResult.FirstError}",
                    NotificationDurations.VeryLong);
            }

            // Step 6: Remove old manifests from pool (excluding any that match the new ones)
            await RemoveOldManifestsAsync(oldManifests, newManifests, cancellationToken);

            // Step 7: Show success notification
            notificationService.ShowSuccess(
                "GeneralsOnline Updated",
                $"Successfully updated to version {updateResult.LatestVersion}. {updateProfilesResult.Data} profiles updated.",
                NotificationDurations.Long);

            logger.LogInformation(
                "[GO Reconciler] Reconciliation complete. Updated {ProfileCount} profiles",
                updateProfilesResult.Data);

            return OperationResult<bool>.CreateSuccess(true);
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
            // Find corresponding new manifest by matching content type and name pattern
            var newManifest = newManifests.FirstOrDefault(n =>
                n.ContentType == oldManifest.ContentType &&
                MatchesByVariant(oldManifest.Id.Value, n.Id.Value));

            if (newManifest != null)
            {
                mapping[oldManifest.Id.Value] = newManifest.Id.Value;
            }
        }

        return mapping;
    }

    /// <summary>
    /// Checks if two manifest IDs refer to the same variant (30hz, 60hz, or quickmatch-maps).
    /// </summary>
    private static bool MatchesByVariant(string oldId, string newId)
    {
        // Extract variant suffix from manifest ID
        // Format: 1.{version}.generalsonline.{contenttype}.{variant}
        var oldVariant = ExtractVariant(oldId);
        var newVariant = ExtractVariant(newId);

        return string.Equals(oldVariant, newVariant, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts the variant suffix from a manifest ID.
    /// </summary>
    private static string? ExtractVariant(string manifestId)
    {
        // Manifest ID formats:
        // Real: 1.1220251.generalsonline.gameclient.30hz
        // Spoofed: 1.101201.generalsonline.gameclient.30hz
        // File: ...manifest.json (should not be here but just in case)
        var parts = manifestId.Split('.');
        if (parts.Length == 0) return null;

        var lastPart = parts[^1];

        // Handle common variants directly
        if (lastPart.Equals("30hz", StringComparison.OrdinalIgnoreCase) ||
            lastPart.Equals("60hz", StringComparison.OrdinalIgnoreCase) ||
            lastPart.Equals("quickmatchmaps", StringComparison.OrdinalIgnoreCase))
        {
            return lastPart;
        }

        // If something like .manifest.json appended (though ID shouldn't have it)
        if (parts.Length > 1)
        {
             return parts[^1];
        }

        return null;
    }

    /// <summary>
    /// Checks if a profile uses any GeneralsOnline content.
    /// </summary>
    private static bool ProfileUsesGeneralsOnline(
        GameProfile profile,
        List<ContentManifest> goManifests)
    {
        if (profile.EnabledContentIds == null || profile.EnabledContentIds.Count == 0)
        {
            return false;
        }

        var goManifestIds = goManifests.Select(m => m.Id.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return profile.EnabledContentIds.Any(id => goManifestIds.Contains(id));
    }

    /// <summary>
    /// Updates content IDs by replacing old GeneralsOnline IDs with new ones.
    /// </summary>
    private static List<string> UpdateContentIds(
        List<string>? currentIds,
        Dictionary<string, string> mapping)
    {
        if (currentIds == null || currentIds.Count == 0)
        {
            return [];
        }

        var newIds = new List<string>();

        foreach (var id in currentIds)
        {
            if (mapping.TryGetValue(id, out var newId))
            {
                newIds.Add(newId);
            }
            else
            {
                // Keep non-GO content IDs as-is
                newIds.Add(id);
            }
        }

        return newIds;
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
                m.Publisher?.PublisherType?.Equals(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) == true ||

                // Check ID pattern
                m.Id.Value.Contains(".generalsonline.", StringComparison.OrdinalIgnoreCase) ||

                // Check Name fallback
                m.Name.Contains("GeneralsOnline", StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>
    /// Acquires the latest GeneralsOnline version by searching and downloading.
    /// Returns all new manifests found in the pool that weren't there before.
    /// </summary>
    private async Task<OperationResult<List<ContentManifest>>> AcquireLatestVersionAsync(
        List<ContentManifest> oldManifests,
        CancellationToken cancellationToken)
    {
        try
        {
            // Search for GeneralsOnline content
            var searchQuery = new ContentSearchQuery
            {
                ProviderName = GeneralsOnlineConstants.PublisherType,
                ContentType = ContentType.GameClient,
                TargetGame = GameType.ZeroHour,
            };

            var searchResult = await contentOrchestrator.SearchAsync(searchQuery, cancellationToken);
            if (!searchResult.Success || searchResult.Data == null || !searchResult.Data.Any())
            {
                return OperationResult<List<ContentManifest>>.CreateFailure(
                    "No GeneralsOnline content found from provider");
            }

            // Acquire each search result (triggers provider to install 30Hz, 60Hz, MapPack)
            foreach (var result in searchResult.Data)
            {
                logger.LogInformation(
                    "[GO Reconciler] Acquiring content: {Name} ({ContentType})",
                    result.Name,
                    result.ContentType);

                var acquireResult = await contentOrchestrator.AcquireContentAsync(
                    result,
                    progress: null,
                    cancellationToken);

                if (!acquireResult.Success)
                {
                    logger.LogWarning(
                        "[GO Reconciler] Failed to acquire {Name}: {Error}",
                        result.Name,
                        acquireResult.FirstError);
                }
            }

            // Rationale: The provider might create multiple manifests (variants/dependencies)
            // We scan the pool to find EVERYTHING that is GeneralsOnline and NOT in our old list.
            var allGoManifests = await FindGeneralsOnlineManifestsAsync(cancellationToken);
            var oldIds = oldManifests.Select(m => m.Id.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var newManifests = allGoManifests
                .Where(m => !oldIds.Contains(m.Id.Value))
                .ToList();

            if (newManifests.Count == 0)
            {
                return OperationResult<List<ContentManifest>>.CreateFailure(
                    "Acquisition completed but no new GeneralsOnline manifests were found in the pool");
            }

            logger.LogInformation(
                "[GO Reconciler] Identified {Count} new manifests in pool: {Manifests}",
                newManifests.Count,
                string.Join(", ", newManifests.Select(m => m.Id.Value)));

            return OperationResult<List<ContentManifest>>.CreateSuccess(newManifests);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[GO Reconciler] Failed to acquire latest version");
            return OperationResult<List<ContentManifest>>.CreateFailure(
                $"Failed to acquire latest version: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates all profiles that use GeneralsOnline content.
    /// </summary>
    private async Task<OperationResult<int>> UpdateAllAffectedProfilesAsync(
        List<ContentManifest> oldManifests,
        List<ContentManifest> newManifests,
        CancellationToken cancellationToken)
    {
        var profilesResult = await profileManager.GetAllProfilesAsync(cancellationToken);
        if (!profilesResult.Success || profilesResult.Data == null)
        {
            return OperationResult<int>.CreateFailure(
                $"Failed to get profiles: {profilesResult.FirstError}");
        }

        // Build mapping from old manifest IDs to new manifest IDs
        var manifestMapping = BuildManifestMapping(oldManifests, newManifests);

        var updatedCount = 0;
        var errors = new List<string>();

        foreach (var profile in profilesResult.Data)
        {
            // Check if profile uses any GeneralsOnline content
            if (!ProfileUsesGeneralsOnline(profile, oldManifests))
            {
                continue;
            }

            logger.LogInformation(
                "[GO Reconciler] Updating profile: {ProfileName} ({ProfileId})",
                profile.Name,
                profile.Id);

            try
            {
                // Update enabled content IDs
                var newContentIds = UpdateContentIds(profile.EnabledContentIds, manifestMapping);

                // Cleanup workspace if it exists
                if (!string.IsNullOrEmpty(profile.ActiveWorkspaceId))
                {
                    logger.LogDebug(
                        "[GO Reconciler] Cleaning up workspace for profile {ProfileId}",
                        profile.Id);

                    var cleanupResult = await workspaceManager.CleanupWorkspaceAsync(
                        profile.ActiveWorkspaceId,
                        cancellationToken);

                    if (!cleanupResult.Success)
                    {
                        logger.LogWarning(
                            "[GO Reconciler] Failed to cleanup workspace for profile {ProfileId}: {Error}",
                            profile.Id,
                            cleanupResult.FirstError);
                    }
                }

                // Update the profile
                var updateRequest = new UpdateProfileRequest
                {
                    EnabledContentIds = newContentIds,

                    // Clear active workspace so it will be recreated on next launch
                    ActiveWorkspaceId = string.Empty,
                };

                var updateResult = await profileManager.UpdateProfileAsync(
                    profile.Id,
                    updateRequest,
                    cancellationToken);

                if (updateResult.Success)
                {
                    updatedCount++;
                    logger.LogInformation(
                        "[GO Reconciler] Successfully updated profile: {ProfileName}",
                        profile.Name);

                    // Notify UI to refresh this profile's ViewModel
                    try
                    {
                        var updatedProfileResult = await profileManager.GetProfileAsync(profile.Id, cancellationToken);
                        if (updatedProfileResult.Success && updatedProfileResult.Data is GameProfile updatedGameProfile)
                        {
                            WeakReferenceMessenger.Default.Send(new ProfileUpdatedMessage(updatedGameProfile));
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "[GO Reconciler] Failed to send ProfileUpdatedMessage for {ProfileName}", profile.Name);
                    }
                }
                else
                {
                    errors.Add($"Failed to update profile '{profile.Name}': {updateResult.FirstError}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[GO Reconciler] Error updating profile {ProfileId}", profile.Id);
                errors.Add($"Error updating profile '{profile.Name}': {ex.Message}");
            }
        }

        if (errors.Count > 0 && updatedCount == 0)
        {
            return OperationResult<int>.CreateFailure(string.Join("; ", errors));
        }

        if (errors.Count > 0)
        {
            logger.LogWarning(
                "[GO Reconciler] Updated {Count} profiles with {ErrorCount} errors",
                updatedCount,
                errors.Count);
        }

        return OperationResult<int>.CreateSuccess(updatedCount);
    }

    /// <summary>
    /// Removes old GeneralsOnline manifests from the manifest pool, unless they are part of the new update.
    /// </summary>
    private async Task RemoveOldManifestsAsync(
        List<ContentManifest> oldManifests,
        List<ContentManifest> newManifests,
        CancellationToken cancellationToken)
    {
        // Create lookup for new IDs to avoid accidental deletion of fresh content
        var newManifestIds = newManifests.Select(m => m.Id.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var manifest in oldManifests)
        {
            if (newManifestIds.Contains(manifest.Id.Value))
            {
                logger.LogInformation(
                    "[GO Reconciler] Skipping removal of manifest {ManifestId} as it matches new content",
                    manifest.Id.Value);
                continue;
            }

            logger.LogInformation(
                "[GO Reconciler] Removing old manifest: {ManifestId} ({Name})",
                manifest.Id.Value,
                manifest.Name);

            var removeResult = await manifestPool.RemoveManifestAsync(manifest.Id, cancellationToken);
            if (!removeResult.Success)
            {
                logger.LogWarning(
                    "[GO Reconciler] Failed to remove old manifest {ManifestId}: {Error}",
                    manifest.Id.Value,
                    removeResult.FirstError);
            }
        }
    }
}
