using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.Reconciliation;

/// <summary>
/// Helper methods for publisher profile reconcilers to apply update strategies and create cloned profiles.
/// </summary>
public static class PublisherReconcilerHelper
{
    /// <summary>
    /// Creates cloned profiles with updated game clients and content manifests for relevant existing profiles.
    /// </summary>
    /// <param name="profileManager">The game profile manager.</param>
    /// <param name="oldManifests">List of older content manifests being superseded.</param>
    /// <param name="newManifests">List of newly acquired content manifests.</param>
    /// <param name="manifestMapping">Mapping from old manifest IDs to new manifest IDs.</param>
    /// <param name="newVersion">The new version string.</param>
    /// <param name="triggeringProfileId">Optional ID of the profile that initiated the update.</param>
    /// <param name="logPrefix">Logging prefix identifying the reconciler.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the count of created profiles and the target profile ID if triggering profile was cloned.</returns>
    public static async Task<OperationResult<(int CreatedCount, string? TargetProfileId)>> CreateNewProfilesForUpdateAsync(
        IGameProfileManager profileManager,
        IReadOnlyList<ContentManifest> oldManifests,
        IReadOnlyList<ContentManifest> newManifests,
        IReadOnlyDictionary<string, string> manifestMapping,
        string newVersion,
        string? triggeringProfileId,
        string logPrefix,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileManager);
        ArgumentNullException.ThrowIfNull(oldManifests);
        ArgumentNullException.ThrowIfNull(newManifests);
        ArgumentNullException.ThrowIfNull(manifestMapping);
        ArgumentNullException.ThrowIfNull(logger);

        var oldIds = oldManifests.Select(m => m.Id.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int createdCount = 0;
        string? targetProfileId = null;

        var allProfiles = await profileManager.GetAllProfilesAsync(cancellationToken);
        if (!allProfiles.Success || allProfiles.Data == null)
        {
            return OperationResult<(int, string?)>.CreateSuccess((0, null));
        }

        foreach (var profile in allProfiles.Data)
        {
            bool isRelevant = (profile.GameClient != null && oldIds.Contains(profile.GameClient.Id)) ||
                              (profile.EnabledContentIds?.Any(id => oldIds.Contains(id)) == true);

            if (!isRelevant)
            {
                continue;
            }

            try
            {
                GameClient? updatedGameClient = profile.GameClient;
                if (profile.GameClient != null && manifestMapping.TryGetValue(profile.GameClient.Id, out var newClientId))
                {
                    var matchedManifest = newManifests.FirstOrDefault(m => m.Id.Value == newClientId);
                    if (matchedManifest != null)
                    {
                        updatedGameClient = new GameClient
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
                    logger.LogDebug(
                        "{Prefix} No manifest mapping found for GameClient '{ClientId}' in profile '{ProfileName}'. Preserving existing client.",
                        logPrefix,
                        profile.GameClient.Id,
                        profile.Name);
                }

                var newEnabledContent = new List<string>();
                if (profile.EnabledContentIds != null)
                {
                    foreach (var id in profile.EnabledContentIds)
                    {
                        newEnabledContent.Add(manifestMapping.TryGetValue(id, out var newId) ? newId : id);
                    }
                }

                var cloneRequest = GameSettingsMapper.CreateCloneRequest(
                    profile,
                    $"{profile.Name} (v{newVersion})",
                    updatedGameClient,
                    newEnabledContent);

                var createResult = await profileManager.CreateProfileAsync(cloneRequest, cancellationToken);
                if (createResult.Success)
                {
                    createdCount++;
                    if (!string.IsNullOrEmpty(triggeringProfileId) &&
                        string.Equals(profile.Id, triggeringProfileId, StringComparison.OrdinalIgnoreCase) &&
                        createResult.Data != null)
                    {
                        targetProfileId = createResult.Data.Id;
                    }

                    logger.LogInformation("{Prefix} Created new profile '{Name}' for update", logPrefix, cloneRequest.Name);
                }
                else
                {
                    logger.LogError("{Prefix} Failed to create new profile for update: {Error}", logPrefix, createResult.FirstError);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Prefix} Error creating profile for update", logPrefix);
            }
        }

        return OperationResult<(int, string?)>.CreateSuccess((createdCount, targetProfileId));
    }

    /// <summary>
    /// Applies the chosen update strategy (replace current vs create new profile).
    /// </summary>
    /// <param name="strategy">The update strategy selected.</param>
    /// <param name="oldManifests">List of older content manifests being superseded.</param>
    /// <param name="newManifests">List of newly acquired content manifests.</param>
    /// <param name="manifestMapping">Mapping from old manifest IDs to new manifest IDs.</param>
    /// <param name="latestVersion">The latest version string.</param>
    /// <param name="shouldDeleteOldVersions">Whether old manifest versions should be deleted.</param>
    /// <param name="triggeringProfileId">Optional ID of the profile that initiated the update.</param>
    /// <param name="publisherDisplayName">Display name for notifications.</param>
    /// <param name="logPrefix">Logging prefix identifying the reconciler.</param>
    /// <param name="profileManager">The game profile manager.</param>
    /// <param name="reconciliationService">The content reconciliation service.</param>
    /// <param name="notificationService">The notification service.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple with operation outcome details.</returns>
    public static async Task<(bool Proceed, string? Error, int ProfilesUpdated, bool AnyFailure, bool ShouldDeleteOldVersions, string? TargetProfileId)> ApplyUpdateStrategyAsync(
        UpdateStrategy strategy,
        IReadOnlyList<ContentManifest> oldManifests,
        IReadOnlyList<ContentManifest> newManifests,
        IReadOnlyDictionary<string, string> manifestMapping,
        string latestVersion,
        bool shouldDeleteOldVersions,
        string? triggeringProfileId,
        string publisherDisplayName,
        string logPrefix,
        IGameProfileManager profileManager,
        IContentReconciliationService reconciliationService,
        INotificationService notificationService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        int profilesUpdated = 0;
        bool anyFailure = false;
        string? targetProfileId = null;

        if (strategy == UpdateStrategy.CreateNewProfile)
        {
            shouldDeleteOldVersions = false;

            var createResult = await CreateNewProfilesForUpdateAsync(
                profileManager,
                oldManifests,
                newManifests,
                manifestMapping,
                latestVersion,
                triggeringProfileId,
                logPrefix,
                logger,
                cancellationToken);

            if (createResult.Success)
            {
                profilesUpdated = createResult.Data.CreatedCount;
                targetProfileId = createResult.Data.TargetProfileId;
            }
            else
            {
                anyFailure = true;
                notificationService.ShowWarning($"{publisherDisplayName} Update Partial", $"Failed to create some new profiles: {createResult.FirstError}");
            }

            return (true, null, profilesUpdated, anyFailure, shouldDeleteOldVersions, targetProfileId);
        }

        targetProfileId = triggeringProfileId;
        var bulkUpdateResult = await reconciliationService.OrchestrateBulkUpdateAsync(
            manifestMapping is Dictionary<string, string> dict ? dict : manifestMapping.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase),
            shouldDeleteOldVersions,
            cancellationToken);

        if (bulkUpdateResult.Success)
        {
            profilesUpdated = bulkUpdateResult.Data.ProfilesUpdated;
            if (bulkUpdateResult.Data.FailedProfilesCount > 0)
            {
                anyFailure = true;
                notificationService.ShowWarning($"{publisherDisplayName} Update Partial", $"{bulkUpdateResult.Data.FailedProfilesCount} profiles could not be updated.", NotificationDurations.VeryLong);
            }

            return (true, null, profilesUpdated, anyFailure, shouldDeleteOldVersions, targetProfileId);
        }

        anyFailure = true;
        notificationService.ShowWarning($"{publisherDisplayName} Update Partial", $"Some profiles could not be updated: {bulkUpdateResult.FirstError}", NotificationDurations.VeryLong);
        return (false, $"Bulk update failed: {bulkUpdateResult.FirstError}", profilesUpdated, anyFailure, shouldDeleteOldVersions, null);
    }
}
