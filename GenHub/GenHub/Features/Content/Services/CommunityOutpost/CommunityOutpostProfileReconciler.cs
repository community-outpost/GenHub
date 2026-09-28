using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services.Reconciliation;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

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
    IGameProfileManager profileManager,
    ITelemetryService? telemetryService = null,
    ILocalizationService? localizationService = null)
    : PublisherProfileReconcilerBase(
        logger,
        new PublisherContentServices(updateService, manifestPool, contentOrchestrator, reconciliationService),
        new PublisherInteractionServices(notificationService, dialogService, userSettingsService, localizationService),
        profileManager,
        PublisherReconcilerText.CommunityOutpost,
        telemetryService),
    ICommunityOutpostProfileReconciler
{
    /// <inheritdoc/>
    protected override ContentManifest? FindReplacementManifest(
        ContentManifest oldManifest,
        IReadOnlyList<ContentManifest> newManifests)
    {
        var targetIsNonRetail = IsNonRetail(oldManifest);
        return newManifests
            .Where(n => IsNonRetail(n) == targetIsNonRetail)
            .OrderByDescending(n => GameVersionHelper.ParseVersionToInt(n.Version))
            .FirstOrDefault();
    }

    private static bool IsNonRetail(ContentManifest manifest) =>
        CommunityOutpostConstants.IsNonRetailIdentifier(manifest.Id.Value) ||
        CommunityOutpostConstants.IsNonRetailIdentifier(manifest.Name) ||
        (manifest.Metadata?.Tags?.Any(CommunityOutpostConstants.IsNonRetailIdentifier) == true);
}
