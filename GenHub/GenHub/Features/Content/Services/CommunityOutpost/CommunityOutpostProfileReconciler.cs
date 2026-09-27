using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Features.Content.Services.Reconciliation;
using Microsoft.Extensions.Logging;

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
        updateService,
        manifestPool,
        contentOrchestrator,
        reconciliationService,
        notificationService,
        dialogService,
        userSettingsService,
        profileManager,
        PublisherReconcilerText.CommunityOutpost,
        telemetryService,
        localizationService),
    ICommunityOutpostProfileReconciler
{
}
