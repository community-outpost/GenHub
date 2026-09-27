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
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Features.Content.Services.SuperHackers;

/// <summary>
/// Service for reconciling profiles when SuperHackers updates are detected.
/// </summary>
/// <remarks>
/// The acquisition query retrieves all GameClients for TheSuperHackers without filtering
/// by the triggering profile's GameType (Generals vs Zero Hour). As a result, both Generals
/// and Zero Hour game client updates are downloaded and stored concurrently during
/// reconciliation. Having both installed is currently fine and intended so they stay updated
/// and get replaced by updates anyway. If single-client selective downloads are desired in
/// the future, filter the query target game by the profile's GameType.
/// </remarks>
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
    ITelemetryService? telemetryService = null,
    ILocalizationService? localizationService = null)
    : PublisherProfileReconcilerBase(
        logger,
        new PublisherContentServices(updateService, manifestPool, contentOrchestrator, reconciliationService),
        new PublisherInteractionServices(notificationService, dialogService, userSettingsService, localizationService),
        profileManager,
        PublisherReconcilerText.SuperHackers,
        telemetryService),
    ISuperHackersProfileReconciler
{
    /// <inheritdoc/>
    protected override ContentManifest? FindReplacementManifest(
        ContentManifest oldManifest,
        IReadOnlyList<ContentManifest> newManifests) =>
        newManifests
            .Where(n =>
                n.ContentType == oldManifest.ContentType &&
                MatchesByVariant(oldManifest.Id.Value, n.Id.Value))
            .OrderByDescending(n => GameVersionHelper.ParseVersionToInt(n.Version))
            .FirstOrDefault();

    private static bool MatchesByVariant(string oldId, string newId)
    {
        var oldVariant = ExtractVariant(oldId);
        var newVariant = ExtractVariant(newId);
        return string.Equals(oldVariant, newVariant, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractVariant(string manifestId)
    {
        if (string.IsNullOrEmpty(manifestId))
        {
            return null;
        }

        var parts = manifestId.Split('.');
        if (parts.Length == 0)
        {
            return null;
        }

        var lastPart = parts[^1];

        // Exact matches for known suffixes
        if (lastPart.Equals(SuperHackersConstants.GeneralsSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return SuperHackersConstants.GeneralsSuffix;
        }

        if (lastPart.Equals(SuperHackersConstants.ZeroHourSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return SuperHackersConstants.ZeroHourSuffix;
        }

        return parts.Length > 1 ? parts[^1] : null;
    }
}
