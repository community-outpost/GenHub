using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
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
    /// <param name="args">Execution arguments for the update strategy.</param>
    /// <param name="context">Contextual dependencies.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the count of created profiles and the target profile ID if triggering profile was cloned.</returns>
    public static async Task<OperationResult<(int CreatedCount, string? TargetProfileId)>> CreateNewProfilesForUpdateAsync(
        UpdateStrategyExecutionArgs args,
        PublisherReconciliationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);

        var oldIds = args.OldManifests.Select(m => m.Id.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int createdCount = 0;
        string? targetProfileId = null;

        var allProfiles = await context.ProfileManager.GetAllProfilesAsync(cancellationToken);
        if (!allProfiles.Success || allProfiles.Data == null)
        {
            var error = allProfiles.FirstError ?? "Failed to retrieve profiles for creating new profiles";
            context.Logger.LogError("{Prefix} {Error}", context.LogPrefix, error);
            return OperationResult<(int, string?)>.CreateFailure(error);
        }

        int relevantCount = 0;
        foreach (var profile in allProfiles.Data)
        {
            if (!IsProfileRelevant(profile, oldIds))
            {
                continue;
            }

            relevantCount++;
            var result = await TryCloneProfileForUpdateAsync(profile, args, context, cancellationToken);
            if (result.Created)
            {
                createdCount++;
                if (result.IsTriggeringProfile)
                {
                    targetProfileId = result.NewProfileId;
                }
            }
        }

        if (createdCount == 0 && relevantCount > 0)
        {
            const string error = "Failed to create new profiles for update";
            context.Logger.LogError("{Prefix} {Error}", context.LogPrefix, error);
            return OperationResult<(int, string?)>.CreateFailure(error);
        }

        return OperationResult<(int, string?)>.CreateSuccess((createdCount, targetProfileId));
    }

    /// <summary>
    /// Applies the chosen update strategy (replace current vs create new profile).
    /// </summary>
    /// <param name="args">Execution arguments for the update strategy.</param>
    /// <param name="context">Contextual dependencies.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple with operation outcome details.</returns>
    public static async Task<(bool Proceed, string? Error, int ProfilesUpdated, bool AnyFailure, bool ShouldDeleteOldVersions, string? TargetProfileId)> ApplyUpdateStrategyAsync(
        UpdateStrategyExecutionArgs args,
        PublisherReconciliationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);

        int profilesUpdated = 0;
        bool anyFailure = false;
        string? targetProfileId = null;
        bool shouldDeleteOld = args.ShouldDeleteOldVersions;

        if (args.Strategy == UpdateStrategy.CreateNewProfile)
        {
            shouldDeleteOld = false;

            var createResult = await CreateNewProfilesForUpdateAsync(args, context, cancellationToken);
            if (createResult.Success)
            {
                profilesUpdated = createResult.Data.CreatedCount;
                targetProfileId = createResult.Data.TargetProfileId;
            }
            else
            {
                anyFailure = true;
                context.NotificationService.ShowWarning(
                    $"{context.PublisherDisplayName} Update Partial",
                    $"Failed to create some new profiles: {createResult.FirstError}");
            }

            return (true, null, profilesUpdated, anyFailure, shouldDeleteOld, targetProfileId);
        }

        targetProfileId = args.TriggeringProfileId;
        var dict = args.ManifestMapping is Dictionary<string, string> d
            ? d
            : args.ManifestMapping.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

        var bulkUpdateResult = await context.ReconciliationService.OrchestrateBulkUpdateAsync(
            dict,
            shouldDeleteOld,
            cancellationToken);

        if (bulkUpdateResult.Success)
        {
            profilesUpdated = bulkUpdateResult.Data.ProfilesUpdated;
            if (bulkUpdateResult.Data.FailedProfilesCount > 0)
            {
                anyFailure = true;
                context.NotificationService.ShowWarning(
                    $"{context.PublisherDisplayName} Update Partial",
                    $"{bulkUpdateResult.Data.FailedProfilesCount} profiles could not be updated.",
                    NotificationDurations.VeryLong);
            }

            return (true, null, profilesUpdated, anyFailure, shouldDeleteOld, targetProfileId);
        }

        anyFailure = true;
        context.NotificationService.ShowWarning(
            $"{context.PublisherDisplayName} Update Partial",
            $"Some profiles could not be updated: {bulkUpdateResult.FirstError}",
            NotificationDurations.VeryLong);

        return (false, $"Bulk update failed: {bulkUpdateResult.FirstError}", profilesUpdated, anyFailure, shouldDeleteOld, targetProfileId);
    }

    private static bool IsProfileRelevant(GameProfile profile, HashSet<string> oldIds)
    {
        return (profile.GameClient != null && oldIds.Contains(profile.GameClient.Id)) ||
               (profile.EnabledContentIds?.Any(id => oldIds.Contains(id)) == true);
    }

    private static GameClient? ResolveUpdatedGameClient(
        GameProfile profile,
        IReadOnlyDictionary<string, string> manifestMapping,
        IReadOnlyList<ContentManifest> newManifests,
        string logPrefix,
        ILogger logger)
    {
        if (profile.GameClient == null)
        {
            return null;
        }

        if (manifestMapping.TryGetValue(profile.GameClient.Id, out var newClientId))
        {
            var matchedManifest = newManifests.FirstOrDefault(m => m.Id.Value == newClientId);
            if (matchedManifest != null)
            {
                return new GameClient
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
        else
        {
            logger.LogDebug(
                "{Prefix} No manifest mapping found for GameClient '{ClientId}' in profile '{ProfileName}'. Preserving existing client.",
                logPrefix,
                profile.GameClient.Id,
                profile.Name);
        }

        return profile.GameClient;
    }

    private static List<string> RemapContentIds(
        IEnumerable<string>? enabledContentIds,
        IReadOnlyDictionary<string, string> manifestMapping)
    {
        var newEnabledContent = new List<string>();
        if (enabledContentIds != null)
        {
            foreach (var id in enabledContentIds)
            {
                newEnabledContent.Add(manifestMapping.TryGetValue(id, out var newId) ? newId : id);
            }
        }

        return newEnabledContent;
    }

    private static async Task<(bool Created, bool IsTriggeringProfile, string? NewProfileId)> TryCloneProfileForUpdateAsync(
        GameProfile profile,
        UpdateStrategyExecutionArgs args,
        PublisherReconciliationContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var updatedClient = ResolveUpdatedGameClient(
                profile,
                args.ManifestMapping,
                args.NewManifests,
                context.LogPrefix,
                context.Logger);

            var newEnabledContent = RemapContentIds(profile.EnabledContentIds, args.ManifestMapping);

            var cloneRequest = GameSettingsMapper.CreateCloneRequest(
                profile,
                $"{profile.Name} (v{args.LatestVersion})",
                updatedClient,
                newEnabledContent);

            var createResult = await context.ProfileManager.CreateProfileAsync(cloneRequest, cancellationToken);
            if (createResult.Success)
            {
                context.Logger.LogInformation(
                    "{Prefix} Created new profile '{Name}' for update",
                    context.LogPrefix,
                    cloneRequest.Name);

                bool isTriggering = !string.IsNullOrEmpty(args.TriggeringProfileId) &&
                                   string.Equals(profile.Id, args.TriggeringProfileId, StringComparison.OrdinalIgnoreCase);

                return (true, isTriggering, createResult.Data?.Id);
            }

            context.Logger.LogError(
                "{Prefix} Failed to create new profile for update: {Error}",
                context.LogPrefix,
                createResult.FirstError);

            return (false, false, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "{Prefix} Error creating profile for update", context.LogPrefix);
            return (false, false, null);
        }
    }
}
