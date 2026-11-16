using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Models.Common;
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
/// prompts the user with update options (or applies them directly when the user opted in to automatic
/// updates for that catalog), and applies the update.
/// </summary>
public class GenericCatalogProfileReconciler(
    ILogger<GenericCatalogProfileReconciler> logger,
    IGameProfileManager profileManager,
    GenericCatalogContentServices contentServices,
    INotificationService notificationService,
    IDialogService dialogService,
    IUserSettingsService userSettingsService,
    ILocalizationService? localizationService = null) : IGenericCatalogProfileReconciler
{
    private sealed record UpdateDecision(UpdateStrategy Strategy, bool DeleteOldVersions, bool IsAutomatic);

    private sealed record ItemReconciliation(OperationResult<PublisherReconciliationResult> Result, bool IsAutomatic);

    private sealed class ReconciliationOutcome
    {
        private OperationResult<PublisherReconciliationResult>? _result;

        public bool IsComplete { get; private set; }

        public void Add(ItemReconciliation item)
        {
            var next = item.Result;
            var applied = _result is { Success: true, Data: not null } ? _result : null;

            if (!next.Success)
            {
                _result = item.IsAutomatic ? applied ?? next : next;
                IsComplete = !item.IsAutomatic;
                return;
            }

            if (applied?.Data != null && next.Data != null)
            {
                _result = OperationResult<PublisherReconciliationResult>.CreateSuccess(
                    PublisherReconciliationResult.Success(
                        next.Data.Strategy ?? UpdateStrategy.ReplaceCurrent,
                        next.Data.TargetProfileId ?? applied.Data.TargetProfileId,
                        Math.Max(applied.Data.ProfilesAffectedCount, next.Data.ProfilesAffectedCount)));
            }
            else
            {
                _result = next;
            }

            IsComplete = !item.IsAutomatic || next.Data?.Strategy != UpdateStrategy.ReplaceCurrent;
        }

        public OperationResult<PublisherReconciliationResult> ToResult() =>
            _result ?? OperationResult<PublisherReconciliationResult>.CreateSuccess(PublisherReconciliationResult.None);
    }

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

            var subResult = await contentServices.SubscriptionStore.GetSubscriptionsAsync(cancellationToken);
            if (!subResult.Success || subResult.Data == null || subResult.Data.Count == 0)
            {
                return OperationResult<PublisherReconciliationResult>.CreateSuccess(PublisherReconciliationResult.None);
            }

            var allowAutomatic = !string.IsNullOrEmpty(triggeringProfileId);
            var outcome = new ReconciliationOutcome();
            foreach (var profile in profilesToCheck)
            {
                foreach (var subscription in subResult.Data)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ReconcileSubscriptionAsync(profile, subscription, outcome, allowAutomatic, cancellationToken);
                    if (outcome.IsComplete)
                    {
                        return outcome.ToResult();
                    }
                }
            }

            return outcome.ToResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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

    private async Task ReconcileSubscriptionAsync(
        GameProfile profile,
        PublisherSubscription subscription,
        ReconciliationOutcome outcome,
        bool allowAutomatic,
        CancellationToken cancellationToken)
    {
        contentServices.CatalogDiscoverer.Configure(subscription);
        var discoveryResult = await contentServices.CatalogDiscoverer.DiscoverAsync(new ContentSearchQuery(), cancellationToken);
        if (!discoveryResult.Success || discoveryResult.Data?.Items == null)
        {
            return;
        }

        foreach (var item in discoveryResult.Data.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var itemOutcome = await TryReconcileItemAsync(profile, subscription, item, allowAutomatic, cancellationToken);
            if (itemOutcome == null)
            {
                continue;
            }

            outcome.Add(itemOutcome);
            if (outcome.IsComplete)
            {
                return;
            }
        }
    }

    private async Task<ItemReconciliation?> TryReconcileItemAsync(
        GameProfile profile,
        PublisherSubscription subscription,
        ContentSearchResult item,
        bool allowAutomatic,
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

        var decision = await ResolveUpdateDecisionAsync(settings, subscription, item, itemVersion, allowAutomatic);
        if (decision == null)
        {
            return null;
        }

        try
        {
            var result = await ExecuteItemUpdateAsync(
                profile.Id,
                subscription,
                item,
                itemVersion,
                localManifestId,
                decision,
                cancellationToken);
            return new ItemReconciliation(result, decision.IsAutomatic);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Catalog Reconciler] Failed to update {ContentName} to {Version}", item.Name, itemVersion);
            ShowUpdateFailure(item.Name, itemVersion, ex.Message, decision.IsAutomatic);
            return new ItemReconciliation(
                OperationResult<PublisherReconciliationResult>.CreateFailure($"Failed to update {item.Name}: {ex.Message}"),
                decision.IsAutomatic);
        }
    }

    private async Task<UpdateDecision?> ResolveUpdateDecisionAsync(
        UserSettings settings,
        PublisherSubscription subscription,
        ContentSearchResult item,
        string itemVersion,
        bool allowAutomatic)
    {
        var preference = settings.GetSubscription(subscription.PublisherId);
        var deleteOldVersions = preference?.DeleteOldVersions ?? true;

        if (allowAutomatic && preference?.AutoUpdateEnabled == true)
        {
            var strategy = preference.PreferredUpdateStrategy ?? settings.PreferredUpdateStrategy ?? UpdateStrategy.ReplaceCurrent;
            logger.LogInformation(
                "[Catalog Reconciler] Automatic updates are enabled for {PublisherId}. Updating {ContentName} to {Version} without prompting.",
                subscription.PublisherId,
                item.Name,
                itemVersion);
            return new UpdateDecision(strategy, deleteOldVersions, IsAutomatic: true);
        }

        var promptResult = await dialogService.ShowUpdateOptionDialogAsync(
            $"{item.Name} Update Available",
            $"A new version of **{item.Name}** is available ({itemVersion}).\n\nHow do you want to apply this update?",
            deleteOldVersions);

        if (promptResult == null || string.Equals(promptResult.Action, "Skip", StringComparison.OrdinalIgnoreCase))
        {
            await HandleSkippedPromptAsync(promptResult, subscription.PublisherId, itemVersion);
            return null;
        }

        if (promptResult.IsDoNotAskAgain)
        {
            await SaveAutoUpdatePreferenceAsync(subscription.PublisherId, promptResult.Strategy, promptResult.DeleteOldVersions);
        }

        return new UpdateDecision(promptResult.Strategy, promptResult.DeleteOldVersions, IsAutomatic: false);
    }

    private async Task SaveAutoUpdatePreferenceAsync(string publisherId, UpdateStrategy strategy, bool deleteOldVersions)
    {
        logger.LogInformation("[Catalog Reconciler] Enabling automatic updates for {PublisherId}", publisherId);
        var saved = await userSettingsService.TryUpdateAndSaveAsync(s =>
        {
            s.SetAutoUpdatePreference(publisherId, true, strategy);
            s.GetOrCreateSubscription(publisherId).DeleteOldVersions = deleteOldVersions;
            return true;
        });

        if (!saved)
        {
            logger.LogWarning("[Catalog Reconciler] Failed to save the automatic update preference for {PublisherId}", publisherId);
        }
    }

    private async Task<string?> GetLocalManifestIdIfApplicableAsync(
        GameProfile? profile,
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

        if (profile == null)
        {
            return localManifestId;
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
        UpdateDecision decision,
        CancellationToken cancellationToken)
    {
        var strategy = decision.Strategy;
        var shouldDeleteOldVersions = decision.DeleteOldVersions;

        notificationService.ShowInfo(
            localizationService.GetLocalizedString("Content.Notification.DownloadingUpdate.Title", "Downloading Update"),
            localizationService.GetLocalizedString("Content.Notification.DownloadingUpdate.Message", $"Downloading {item.Name} v{itemVersion}...", item.Name, itemVersion));
        var downloadResult = await contentServices.DownloadCoordinator.DownloadContentAsync(item, null, cancellationToken);
        if (!downloadResult.Success || downloadResult.Data == null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (decision.IsAutomatic)
            {
                ShowUpdateFailure(item.Name, itemVersion, downloadResult.FirstError, isAutomatic: true);
            }
            else
            {
                notificationService.ShowError(
                    localizationService.GetLocalizedString("Content.Notification.UpdateFailed.Title", "Update Failed"),
                    localizationService.GetLocalizedString("Content.Notification.UpdateFailed.DownloadMessage", $"Failed to download {item.Name}: {downloadResult.FirstError}", item.Name, downloadResult.FirstError));
            }

            contentServices.TelemetryService?.TrackEvent(TelemetryConstants.Events.ContentUpdateFailed, new Dictionary<string, object?>
            {
                [TelemetryConstants.Properties.PublisherId] = subscription.PublisherId,
                [TelemetryConstants.Properties.ContentName] = item.Name,
                [TelemetryConstants.Properties.ContentId] = localManifestId,
                [TelemetryConstants.Properties.Author] = item.AuthorName ?? subscription.PublisherName ?? subscription.PublisherId,
                [TelemetryConstants.Properties.ErrorMessage] = downloadResult.FirstError ?? "Failed to download update",
            });
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
                "[Catalog Reconciler]",
                localizationService),
            cancellationToken);

        if (!updateOutcome.Proceed)
        {
            contentServices.TelemetryService?.TrackEvent(TelemetryConstants.Events.ContentUpdateFailed, new Dictionary<string, object?>
            {
                [TelemetryConstants.Properties.PublisherId] = subscription.PublisherId,
                [TelemetryConstants.Properties.ContentName] = item.Name,
                [TelemetryConstants.Properties.ContentId] = newManifest.Id.Value,
                [TelemetryConstants.Properties.Author] = item.AuthorName ?? subscription.PublisherName ?? subscription.PublisherId,
                [TelemetryConstants.Properties.FromVersion] = oldManifestResult.Data?.Version ?? string.Empty,
                [TelemetryConstants.Properties.ToVersion] = itemVersion,
                [TelemetryConstants.Properties.Strategy] = strategy.ToString(),
                [TelemetryConstants.Properties.ErrorMessage] = updateOutcome.Error ?? "Failed to apply update",
            });
            return OperationResult<PublisherReconciliationResult>.CreateFailure(updateOutcome.Error ?? "Failed to apply update");
        }

        if (updateOutcome.ShouldDeleteOldVersions && !updateOutcome.AnyFailure)
        {
            await contentServices.ReconciliationService.ScheduleGarbageCollectionAsync(false, cancellationToken);
        }

        contentServices.TelemetryService?.TrackEvent(TelemetryConstants.Events.ContentUpdateApplied, new Dictionary<string, object?>
        {
            [TelemetryConstants.Properties.PublisherId] = subscription.PublisherId,
            [TelemetryConstants.Properties.ContentName] = item.Name,
            [TelemetryConstants.Properties.ContentId] = newManifest.Id.Value,
            [TelemetryConstants.Properties.Author] = item.AuthorName ?? subscription.PublisherName ?? subscription.PublisherId,
            [TelemetryConstants.Properties.FromVersion] = oldManifestResult.Data?.Version ?? string.Empty,
            [TelemetryConstants.Properties.ToVersion] = itemVersion,
            [TelemetryConstants.Properties.Strategy] = strategy.ToString(),
            [TelemetryConstants.Properties.ProfilesUpdated] = updateOutcome.ProfilesUpdated,
            [TelemetryConstants.Properties.Success] = !updateOutcome.AnyFailure,
        });

        ShowUpdateCompleted(item.Name, itemVersion, decision.IsAutomatic);

        return OperationResult<PublisherReconciliationResult>.CreateSuccess(
            PublisherReconciliationResult.Success(
                strategy,
                updateOutcome.TargetProfileId ?? triggeringProfileId,
                updateOutcome.ProfilesUpdated));
    }

    private void ShowUpdateCompleted(string contentName, string version, bool isAutomatic)
    {
        var title = localizationService.GetLocalizedString("Content.Notification.UpdateCompleted.Title", "Update Completed");
        if (isAutomatic)
        {
            notificationService.ShowSuccess(
                title,
                localizationService.GetLocalizedString(
                    "Content.Notification.AutoUpdateCompleted.Message",
                    $"{contentName} was updated automatically to version {version}. You can turn this off in Settings under Publisher Subscriptions.",
                    contentName,
                    version),
                NotificationDurations.Long);
            return;
        }

        notificationService.ShowSuccess(
            title,
            localizationService.GetLocalizedString("Content.Notification.UpdateCompleted.Message", $"Updated {contentName} to version {version}.", contentName, version),
            NotificationDurations.Medium);
    }

    private void ShowUpdateFailure(string contentName, string version, string? error, bool isAutomatic)
    {
        var title = localizationService.GetLocalizedString("Content.Notification.UpdateFailed.Title", "Update Failed");
        var message = isAutomatic
            ? localizationService.GetLocalizedString(
                "Content.Notification.AutoUpdateFailed.Message",
                $"{contentName} could not be updated to version {version} automatically: {error}. The installed version is still in use.",
                contentName,
                version,
                error)
            : localizationService.GetLocalizedString("Content.Notification.UpdateError.Message", $"An error occurred during update: {error}", error);

        notificationService.ShowError(title, message, NotificationDurations.Critical);
    }
}
