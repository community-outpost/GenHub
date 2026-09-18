using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.Reconciliation;

/// <summary>
/// Contextual dependencies required for publisher reconciliation strategy execution.
/// </summary>
/// <param name="ProfileManager">The game profile manager.</param>
/// <param name="ReconciliationService">The content reconciliation service.</param>
/// <param name="NotificationService">The notification service.</param>
/// <param name="Logger">The logger instance.</param>
/// <param name="PublisherDisplayName">Display name for user-facing notifications.</param>
/// <param name="LogPrefix">Logging prefix identifying the reconciler.</param>
public sealed record PublisherReconciliationContext(
    IGameProfileManager ProfileManager,
    IContentReconciliationService ReconciliationService,
    INotificationService NotificationService,
    ILogger Logger,
    string PublisherDisplayName,
    string LogPrefix);
