using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Service for reconciling profiles when subscribed generic catalog updates are detected.
/// Checks profiles for installed content items with available updates from followed publisher catalogs,
/// prompts the user with update options, and applies the update.
/// </summary>
public class GenericCatalogProfileReconciler(
    ILogger<GenericCatalogProfileReconciler> logger,
    IGameProfileManager profileManager,
    IContentManifestPool manifestPool,
    IPublisherSubscriptionStore subscriptionStore,
    GenericCatalogDiscoverer catalogDiscoverer,
    IContentStateService contentStateService,
    IContentDownloadCoordinator downloadCoordinator,
    IContentReconciliationService reconciliationService,
    INotificationService notificationService,
    IDialogService dialogService,
    IUserSettingsService userSettingsService) : IGenericCatalogProfileReconciler
{
    /// <inheritdoc/>
    public string PublisherType => CatalogConstants.GenericPublisherType;

    /// <inheritdoc/>
    public async Task<OperationResult<PublisherReconciliationResult>> CheckAndReconcileIfNeededAsync(
        string triggeringProfileId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var profileResult = await profileManager.GetProfileAsync(triggeringProfileId, cancellationToken);
            if (!profileResult.Success || profileResult.Data == null)
            {
                return OperationResult<PublisherReconciliationResult>.CreateSuccess(PublisherReconciliationResult.None);
            }

            var profile = profileResult.Data;
            var subResult = await subscriptionStore.GetSubscriptionsAsync(cancellationToken);
            if (!subResult.Success || subResult.Data == null || subResult.Data.Count == 0)
            {
                return OperationResult<PublisherReconciliationResult>.CreateSuccess(PublisherReconciliationResult.None);
            }

            foreach (var subscription in subResult.Data)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                catalogDiscoverer.Configure(subscription);
                var discoveryResult = await catalogDiscoverer.DiscoverAsync(new ContentSearchQuery(), cancellationToken);
                if (!discoveryResult.Success || discoveryResult.Data?.Items == null)
                {
                    continue;
                }

                foreach (var item in discoveryResult.Data.Items)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var state = await contentStateService.GetStateAsync(item, cancellationToken);
                    if (state != ContentState.UpdateAvailable)
                    {
                        continue;
                    }

                    var localManifestId = await contentStateService.GetLocalManifestIdAsync(item, cancellationToken);
                    if (string.IsNullOrEmpty(localManifestId))
                    {
                        continue;
                    }

                    var isUsedInProfile = (profile.EnabledContentIds?.Contains(localManifestId, StringComparer.OrdinalIgnoreCase) == true) ||
                                          string.Equals(profile.GameClient?.Id, localManifestId, StringComparison.OrdinalIgnoreCase);

                    if (!isUsedInProfile)
                    {
                        continue;
                    }

                    var settings = userSettingsService.Get();
                    var itemVersion = item.Version ?? string.Empty;
                    if (settings.IsVersionSkipped(subscription.PublisherId, itemVersion))
                    {
                        logger.LogInformation("[Catalog Reconciler] Version {Version} of {ContentName} is skipped. Skipping prompt.", itemVersion, item.Name);
                        continue;
                    }

                    var promptResult = await dialogService.ShowUpdateOptionDialogAsync(
                        $"{item.Name} Update Available",
                        $"A new version of **{item.Name}** is available ({itemVersion}).\n\nHow do you want to apply this update?",
                        initialDeleteOldVersions: true);

                    if (promptResult == null || string.Equals(promptResult.Action, "Skip", StringComparison.OrdinalIgnoreCase))
                    {
                        if (promptResult?.IsDoNotAskAgain == true && !string.IsNullOrEmpty(itemVersion))
                        {
                            await userSettingsService.TryUpdateAndSaveAsync(s =>
                            {
                                s.SkipVersion(subscription.PublisherId, itemVersion);
                                return true;
                            });
                        }

                        continue;
                    }

                    var strategy = promptResult.Strategy;
                    var shouldDeleteOldVersions = promptResult.DeleteOldVersions;

                    notificationService.ShowInfo("Downloading Update", $"Downloading {item.Name} v{itemVersion}...");
                    var downloadResult = await downloadCoordinator.DownloadContentAsync(item, null, cancellationToken);
                    if (!downloadResult.Success || downloadResult.Data == null)
                    {
                        notificationService.ShowError("Update Failed", $"Failed to download {item.Name}: {downloadResult.FirstError}");
                        return OperationResult<PublisherReconciliationResult>.CreateFailure($"Failed to download update: {downloadResult.FirstError}");
                    }

                    var newManifest = downloadResult.Data;
                    var oldManifestResult = await manifestPool.GetManifestAsync(localManifestId, cancellationToken);
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
                            reconciliationService,
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
                        await reconciliationService.ScheduleGarbageCollectionAsync(false, cancellationToken);
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

            return OperationResult<PublisherReconciliationResult>.CreateSuccess(PublisherReconciliationResult.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Catalog Reconciler] Failed during check and reconciliation for profile {ProfileId}", triggeringProfileId);
            return OperationResult<PublisherReconciliationResult>.CreateFailure($"Catalog reconciliation error: {ex.Message}");
        }
    }
}
