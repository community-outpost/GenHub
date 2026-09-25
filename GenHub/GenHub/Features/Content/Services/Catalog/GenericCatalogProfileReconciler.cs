using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Notifications;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Reconciliation;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PublisherSubscription = GenHub.Core.Models.Providers.PublisherSubscription;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Service for reconciling profiles when subscribed generic catalog updates are detected.
/// Checks profiles for installed content items with available updates from followed publisher catalogs,
/// prompts the user with update options, and applies the update.
/// </summary>
public class GenericCatalogProfileReconciler(
    ILogger<GenericCatalogProfileReconciler> logger,
    IGameProfileManager profileManager,
    IPublisherSubscriptionStore subscriptionStore,
    GenericCatalogContentServices contentServices,
    INotificationService notificationService,
    IDialogService dialogService,
    IUserSettingsService userSettingsService) : IGenericCatalogProfileReconciler
{
    /// <inheritdoc />
    public string PublisherType => CatalogConstants.GenericPublisherType;

    /// <inheritdoc />
    public async Task<OperationResult<PublisherReconciliationResult>> CheckAndReconcileIfNeededAsync(
        string triggeringProfileId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var profilesToCheck = await GetProfilesToCheckAsync(triggeringProfileId, cancellationToken);
            if (profilesToCheck.Count == 0)
            {
                return OperationResult<PublisherReconciliationResult>.CreateSuccess(PublisherReconciliationResult.None);
            }

            var subResult = await subscriptionStore.GetSubscriptionsAsync(cancellationToken);
            if (!subResult.Success || subResult.Data == null || subResult.Data.Count == 0)
            {
                return OperationResult<PublisherReconciliationResult>.CreateSuccess(PublisherReconciliationResult.None);
            }

            foreach (var profile in profilesToCheck)
            {
                foreach (var subscription in subResult.Data)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var reconciliationOutcome = await ReconcileSubscriptionAsync(profile, subscription, cancellationToken);
                    if (reconciliationOutcome != null)
                    {
                        return reconciliationOutcome;
                    }
                }
            }

            return OperationResult<PublisherReconciliationResult>.CreateSuccess(PublisherReconciliationResult.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Catalog Reconciler] Failed during check and reconciliation for profile {ProfileId}", triggeringProfileId);
            return OperationResult<PublisherReconciliationResult>.CreateFailure($"Catalog reconciliation error: {ex.Message}");
        }
    }

    private async Task<List<GameProfile>> GetProfilesToCheckAsync(
        string triggeringProfileId,
        CancellationToken cancellationToken)
    {
        List<GameProfile> profiles = [];
        if (!string.IsNullOrEmpty(triggeringProfileId))
        {
            var profileResult = await profileManager.GetProfileAsync(triggeringProfileId, cancellationToken);
            if (!profileResult.Success || profileResult.Data == null)
            {
                logger.LogWarning("[Catalog Reconciler] Profile {ProfileId} not found, skipping catalog reconciliation", triggeringProfileId);
                return profiles;
            }

            profiles.Add(profileResult.Data);
            return profiles;
        }

        var allProfilesResult = await profileManager.GetAllProfilesAsync(cancellationToken);
        if (allProfilesResult.Success && allProfilesResult.Data != null)
        {
            profiles.AddRange(allProfilesResult.Data);
        }

        return profiles;
    }

    private async Task<OperationResult<PublisherReconciliationResult>?> ReconcileSubscriptionAsync(
        GameProfile profile,
        PublisherSubscription subscription,
        CancellationToken cancellationToken)
    {
        contentServices.CatalogDiscoverer.Configure(subscription);
        var discoveryResult = await contentServices.CatalogDiscoverer.DiscoverAsync(new ContentSearchQuery(), cancellationToken);
        if (!discoveryResult.Success || discoveryResult.Data?.Items == null)
        {
            return null;
        }

        foreach (var item in discoveryResult.Data.Items)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var itemResult = await TryReconcileItemAsync(profile, subscription, item, cancellationToken);
            if (itemResult != null)
            {
                return itemResult;
            }
        }

        return null;
    }

    private async Task<OperationResult<PublisherReconciliationResult>?> TryReconcileItemAsync(
        GameProfile profile,
        PublisherSubscription subscription,
        ContentSearchResult item,
        CancellationToken cancellationToken)
    {
        var localManifestId = await GetLocalManifestIdIfApplicableAsync(profile, item, cancellationToken);
        if (string.IsNullOrEmpty(localManifestId))
        {
            return null;
        }

        var itemVersion = item.Version ?? string.Empty;
        var settings = userSettingsService.Get();
        if (settings.IsVersionSkipped(subscription.PublisherId, itemVersion))
        {
            logger.LogInformation("[Catalog Reconciler] Version {Version} of {ContentName} is skipped. Skipping prompt.", itemVersion, item.Name);
            return null;
        }

        var promptResult = await dialogService.ShowUpdateOptionDialogAsync(
            $"{item.Name} Update Available",
            $"A new version of **{item.Name}** is available ({itemVersion}).\n\nHow do you want to apply this update?",
            initialDeleteOldVersions: true);

        if (promptResult == null || string.Equals(promptResult.Action, "Skip", StringComparison.OrdinalIgnoreCase))
        {
            await HandleSkippedPromptAsync(promptResult, subscription.PublisherId, itemVersion);
            return null;
        }

        return await ExecuteItemUpdateAsync(
            profile.Id,
            subscription,
            item,
            itemVersion,
            localManifestId,
            promptResult,
            cancellationToken);
    }

    private async Task<string?> GetLocalManifestIdIfApplicableAsync(
        GameProfile profile,
        ContentSearchResult item,
        CancellationToken cancellationToken)
    {
        var state = await contentServices.ContentStateService.GetStateAsync(item, cancellationToken);
        if (state != ContentState.UpdateAvailable)
        {
            return null;
        }

        var localManifestId = await contentServices.ContentStateService.GetLocalManifestIdAsync(item, cancellationToken);
        if (string.IsNullOrEmpty(localManifestId))
        {
            return null;
        }

        var isUsedInProfile = (profile.EnabledContentIds?.Contains(localManifestId, StringComparer.OrdinalIgnoreCase) == true) ||
                              string.Equals(profile.GameClient?.Id, localManifestId, StringComparison.OrdinalIgnoreCase);

        return isUsedInProfile ? localManifestId : null;
    }

    private async Task HandleSkippedPromptAsync(
        UpdateDialogResult? promptResult,
        string publisherId,
        string itemVersion)
    {
        if (promptResult?.IsDoNotAskAgain == true && !string.IsNullOrEmpty(itemVersion))
        {
            await userSettingsService.TryUpdateAndSaveAsync(s =>
            {
                s.SkipVersion(publisherId, itemVersion);
                return true;
            });
        }
    }

    private async Task<OperationResult<PublisherReconciliationResult>> ExecuteItemUpdateAsync(
        string triggeringProfileId,
        PublisherSubscription subscription,
        ContentSearchResult item,
        string itemVersion,
        string localManifestId,
        UpdateDialogResult promptResult,
        CancellationToken cancellationToken)
    {
        var strategy = promptResult.Strategy;
        var shouldDeleteOldVersions = promptResult.DeleteOldVersions;

        notificationService.ShowInfo("Downloading Update", $"Downloading {item.Name} v{itemVersion}...");
        var downloadResult = await contentServices.DownloadCoordinator.DownloadContentAsync(item, null, cancellationToken);
        if (!downloadResult.Success || downloadResult.Data == null)
        {
            notificationService.ShowError("Update Failed", $"Failed to download {item.Name}: {downloadResult.FirstError}");
            return OperationResult<PublisherReconciliationResult>.CreateFailure($"Failed to download update: {downloadResult.FirstError}");
        }

        var newManifest = downloadResult.Data;
        var oldManifestResult = await contentServices.ManifestPool.GetManifestAsync(localManifestId, cancellationToken);
        var oldManifests = oldManifestResult.Success && oldManifestResult.Data != null
            ? new List<ContentManifest> { oldManifestResult.Data }
            : new List<ContentManifest>();

        var newManifests = new List<ContentManifest> { newManifest };
        var manifestMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [localManifestId] = newManifest.Id.Value,
        };

        var updateOutcome = await PublisherReconcilerHelper.ApplyUpdateStrategyAsync(
            new UpdateStrategyExecutionArgs(
                strategy,
                oldManifests,
                newManifests,
                manifestMapping,
                itemVersion,
                shouldDeleteOldVersions,
                triggeringProfileId),
            new PublisherReconciliationContext(
                profileManager,
                contentServices.ReconciliationService,
                notificationService,
                logger,
                subscription.PublisherName ?? subscription.PublisherId,
                "[Catalog Reconciler]"),
            cancellationToken);

        if (!updateOutcome.Proceed)
        {
            return OperationResult<PublisherReconciliationResult>.CreateFailure(updateOutcome.Error ?? "Failed to apply update");
        }

        if (updateOutcome.ShouldDeleteOldVersions && !updateOutcome.AnyFailure)
        {
            await contentServices.ReconciliationService.ScheduleGarbageCollectionAsync(false, cancellationToken);
        }

        notificationService.ShowSuccess(
            "Update Completed",
            $"Updated {item.Name} to version {itemVersion}.",
            NotificationDurations.Medium);

        return OperationResult<PublisherReconciliationResult>.CreateSuccess(
            PublisherReconciliationResult.Success(
                strategy,
                updateOutcome.TargetProfileId ?? triggeringProfileId,
                updateOutcome.ProfilesUpdated));
    }
}
