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
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Common;
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
    IGameProfileManager profileManager,
    IContentVersionComparer versionComparer) : ISuperHackersProfileReconciler, IPublisherReconciler
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

            var updateResult = await updateService.CheckForUpdatesAsync(cancellationToken);
            if (!updateResult.Success)
            {
                logger.LogWarning("[SH Reconciler] Update check failed: {Error}", updateResult.FirstError);
                return OperationResult<bool>.CreateFailure($"Failed to check for SuperHackers updates: {updateResult.FirstError}");
            }

            if (!updateResult.IsUpdateAvailable)
            {
                logger.LogInformation("[SH Reconciler] No update available. Current version: {Version}", updateResult.CurrentVersion);
                return OperationResult<bool>.CreateSuccess(false);
            }

            logger.LogInformation(
                "[SH Reconciler] Update available! Current: {CurrentVersion}, Latest: {LatestVersion}",
                updateResult.CurrentVersion,
                updateResult.LatestVersion);

            var settings = userSettingsService.Get();
            if (settings.IsVersionSkipped(PublisherTypeConstants.TheSuperHackers, updateResult.LatestVersion ?? string.Empty))
            {
                logger.LogInformation("[SH Reconciler] User opted to skip version {Version}. Skipping.", updateResult.LatestVersion);
                return OperationResult<bool>.CreateSuccess(false);
            }

            var promptResult = await ResolveUpdateStrategyAsync(updateResult, settings);
            if (!promptResult.ShouldProceed)
            {
                return OperationResult<bool>.CreateSuccess(false);
            }

            var strategy = promptResult.Strategy;
            var shouldDeleteOldVersions = promptResult.ShouldDeleteOldVersions;

            // Notify user that update is being installed
            notificationService.ShowInfo(
                "SuperHackers Update Found",
                $"Installing SuperHackers {updateResult.LatestVersion}. Please wait...",
                NotificationDurations.VeryLong);

            var oldManifests = await FindSuperHackersManifestsAsync(cancellationToken);
            logger.LogInformation("[SH Reconciler] Found {Count} existing SuperHackers manifests to replace", oldManifests.Count);

            var acquireResult = await AcquireLatestVersionAsync(oldManifests, cancellationToken);
            if (!acquireResult.Success)
            {
                notificationService.ShowError(
                    "SuperHackers Update Failed",
                    $"Failed to download update: {acquireResult.FirstError}",
                    NotificationDurations.Critical);

                return OperationResult<bool>.CreateFailure($"Failed to acquire new SuperHackers version: {acquireResult.FirstError}");
            }

            var newManifests = acquireResult.Data!;
            var applyResult = await ExecuteUpdateStrategyAsync(strategy, shouldDeleteOldVersions, oldManifests, newManifests, updateResult.LatestVersion ?? "Unknown", cancellationToken);
            if (!applyResult.Success)
            {
                return OperationResult<bool>.CreateFailure(applyResult);
            }

            var (profilesUpdated, anyFailure) = applyResult.Data;
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
        IEnumerable<ContentManifest> oldManifests,
        IEnumerable<ContentManifest> newManifests,
        IVersionScheme versionScheme)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var oldManifest in oldManifests)
        {
            var newManifest = newManifests
                .Where(n =>
                    (n.ContentType == oldManifest.ContentType ||
                     (oldManifest.ContentType == ContentType.Mod && n.ContentType == ContentType.GameClient)) &&
                    MatchesByVariant(oldManifest.Id.Value, n.Id.Value))
                .OrderByDescending(n => n.Version, versionScheme)
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

    private async Task<(bool ShouldProceed, UpdateStrategy Strategy, bool ShouldDeleteOldVersions)> ResolveUpdateStrategyAsync(
        ContentUpdateCheckResult updateResult,
        GenHub.Core.Models.Common.UserSettings settings)
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
            $"A new version of **The Super Hackers** is available ({updateResult.LatestVersion}).\n\nHow do you want to apply this update?");

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

        return (true, strategy, shouldDeleteOldVersions);
    }

    private async Task<OperationResult<(int ProfilesUpdated, bool AnyFailure)>> ExecuteUpdateStrategyAsync(
        UpdateStrategy strategy,
        bool shouldDeleteOldVersions,
        List<ContentManifest> oldManifests,
        List<ContentManifest> newManifests,
        string latestVersion,
        CancellationToken cancellationToken)
    {
        int profilesUpdated = 0;
        bool anyFailure = false;

        if (strategy == UpdateStrategy.CreateNewProfile)
        {
            var createResult = await CreateNewProfilesForUpdateAsync(oldManifests, newManifests, latestVersion, cancellationToken);
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
            var manifestMapping = BuildManifestMapping(oldManifests, newManifests, versionComparer.GetScheme(PublisherTypeConstants.TheSuperHackers));
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
                notificationService.ShowWarning("SuperHackers Update Partial", $"Some profiles could not be updated: {bulkUpdateResult.FirstError}", NotificationDurations.VeryLong);
                return OperationResult<(int, bool)>.CreateFailure($"Bulk update failed: {bulkUpdateResult.FirstError}");
            }
        }

        return OperationResult<(int, bool)>.CreateSuccess((profilesUpdated, anyFailure));
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
        IReadOnlyList<ContentManifest> oldManifests,
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

            // Layers beneath the orchestrator still report cancellation as a failed result, so a
            // failure raised while shutting down must not be surfaced as a real acquisition error.
            cancellationToken.ThrowIfCancellationRequested();

            if (!searchResult.Success || searchResult.Data == null || !searchResult.Data.Any())
            {
                 return OperationResult<List<ContentManifest>>.CreateFailure(
                    "No SuperHackers content found from provider");
            }

            foreach (var result in searchResult.Data)
            {
                var acquireOp = await contentOrchestrator.AcquireContentAsync(result, progress: null, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
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

    /// <summary>
    /// Creates new profiles for the update instead of replacing existing ones.
    /// </summary>
    private async Task<OperationResult<int>> CreateNewProfilesForUpdateAsync(
        IReadOnlyList<ContentManifest> oldManifests,
        IReadOnlyList<ContentManifest> newManifests,
        string newVersion,
        CancellationToken cancellationToken)
    {
        var oldIds = oldManifests.Select(m => m.Id.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var manifestMapping = BuildManifestMapping(oldManifests, newManifests, versionComparer.GetScheme(PublisherTypeConstants.TheSuperHackers));
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
                // Format new profile name cleanly (avoid redundant "v" if newVersion already has prefix)
                var formattedVersion = newVersion.StartsWith("weekly-", StringComparison.OrdinalIgnoreCase) ||
                                       newVersion.StartsWith('v') || newVersion.StartsWith('V')
                    ? newVersion
                    : $"v{newVersion}";

                // Clone the profile
                var cloneRequest = new Core.Models.GameProfile.CreateProfileRequest
                {
                   Name = $"{profile.Name} ({formattedVersion})",
                   Description = profile.Description,
                   GameInstallationId = profile.GameInstallationId,
                   WorkspaceStrategy = profile.WorkspaceStrategy,
                   GameClient = profile.GameClient, // Default to old, override below if mapping exists
                   ThemeColor = profile.ThemeColor,
                   IconPath = profile.IconPath,
                   CoverPath = profile.CoverPath,
                   UseSteamLaunch = profile.UseSteamLaunch,
                   CommandLineArguments = profile.CommandLineArguments,
                   GameSpyIPAddress = profile.GameSpyIPAddress,
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
                        cloneRequest.GameClientId = matchedManifest.Id.Value;
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
