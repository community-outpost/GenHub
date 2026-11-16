using GenHub.Core.Constants;

namespace GenHub.Features.Content.Services.Reconciliation;

/// <summary>
/// Publisher-specific display strings, log prefixes, and localization keys consumed by
/// <see cref="PublisherProfileReconcilerBase"/>. The reconciliation flow is identical for
/// every publisher; only these values differ.
/// </summary>
public sealed record PublisherReconcilerText
{
    /// <summary>
    /// Gets the text for the Community Outpost reconciler.
    /// </summary>
    public static PublisherReconcilerText CommunityOutpost { get; } = new()
    {
        PublisherType = CommunityOutpostConstants.PublisherType,
        LogPrefix = "[CO Reconciler]",
        PublisherDisplayName = "Community Outpost",
        ContextDisplayName = "Community Patch",
        TelemetryContentName = CommunityOutpostConstants.CommunityPatchRetailDisplayName,
        ProgressTitleKey = "Content.Notification.CommunityPatchUpdate.Title",
        ProgressTitleFallback = "Community Patch Update",
        ProgressBodyKey = "Content.Notification.CommunityPatchUpdate.Message",
        ProgressBodyFallback = "Installing {0} {1}. Please wait...",
        AcquireFailedTitleKey = "Content.Notification.CommunityPatchUpdateFailed.Title",
        AcquireFailedTitleFallback = "Community Patch Update Failed",
        AcquireFailedFormat = "Failed to acquire new {0} version: {1}",
        UpdatedTitleKey = "Content.Notification.CommunityPatchUpdated.Title",
        UpdatedTitleFallback = "Community Patch Updated",
        PartialUpdatedTitleKey = "Content.Notification.CommunityPatchUpdatedPartial.Title",
        PartialUpdatedTitleFallback = "Community Patch Updated (Partial)",
        ErrorTitleKey = "Content.Notification.CommunityPatchUpdateError.Title",
        ErrorTitleFallback = "Community Patch Update Error",
        PromptTitleKey = "Content.Prompt.CommunityPatchUpdateAvailable.Title",
        PromptTitleFallback = "Community Patch Update Available",
        PromptBodyKey = "Content.Prompt.CommunityPatchUpdateAvailable.Message",
        PromptBodyFormat = "A new version of the **Community Patch** is available ({0}).\n\nHow do you want to apply this update?",
        AcquireItemFailedFormat = "Failed to acquire {0} content {1}: {2}",
        NoContentMessage = "No Community Outpost content found from provider",
        AcquireNoneMessage = "Acquisition completed but no new Community Outpost manifests were found",
    };

    /// <summary>
    /// Gets the text for the SuperHackers reconciler.
    /// </summary>
    public static PublisherReconcilerText SuperHackers { get; } = new()
    {
        PublisherType = PublisherTypeConstants.TheSuperHackers,
        LogPrefix = "[SH Reconciler]",
        PublisherDisplayName = "SuperHackers",
        ContextDisplayName = "SuperHackers",
        TelemetryContentName = SuperHackersConstants.ServiceName,
        ProgressTitleKey = "Content.Notification.SuperHackersUpdate.Title",
        ProgressTitleFallback = "SuperHackers Update",
        ProgressBodyKey = "Content.Notification.SuperHackersUpdate.Message",
        ProgressBodyFallback = "Installing {0} {1}. Please wait...",
        AcquireFailedTitleKey = "Content.Notification.SuperHackersUpdateFailed.Title",
        AcquireFailedTitleFallback = "SuperHackers Update Failed",
        AcquireFailedFormat = "Failed to acquire new {0} version: {1}",
        UpdatedTitleKey = "Content.Notification.SuperHackersUpdated.Title",
        UpdatedTitleFallback = "SuperHackers Updated",
        PartialUpdatedTitleKey = "Content.Notification.SuperHackersUpdatedPartial.Title",
        PartialUpdatedTitleFallback = "SuperHackers Updated (Partial)",
        ErrorTitleKey = "Content.Notification.SuperHackersUpdateError.Title",
        ErrorTitleFallback = "SuperHackers Update Error",
        PromptTitleKey = "Content.Prompt.SuperHackersUpdateAvailable.Title",
        PromptTitleFallback = "SuperHackers Update Available",
        PromptBodyKey = "Content.Prompt.SuperHackersUpdateAvailable.Message",
        PromptBodyFormat = "A new version of **The Super Hackers** is available ({0}).\n\nHow do you want to apply this update?",
        AcquireItemFailedFormat = "Failed to acquire {0} content {1}: {2}",
        NoContentMessage = "No SuperHackers content found from provider",
        AcquireNoneMessage = "Acquisition completed but no new SuperHackers manifests were found",
    };

    /// <summary>
    /// Gets the publisher type this text applies to.
    /// </summary>
    public required string PublisherType { get; init; }

    /// <summary>
    /// Gets the log line prefix.
    /// </summary>
    public required string LogPrefix { get; init; }

    /// <summary>
    /// Gets the publisher display name used in messages.
    /// </summary>
    public required string PublisherDisplayName { get; init; }

    /// <summary>
    /// Gets the content display name used in messages.
    /// </summary>
    public required string ContextDisplayName { get; init; }

    /// <summary>
    /// Gets the content name reported to telemetry.
    /// </summary>
    public required string TelemetryContentName { get; init; }

    /// <summary>
    /// Gets the progress notification title resource key.
    /// </summary>
    public required string ProgressTitleKey { get; init; }

    /// <summary>
    /// Gets the progress notification title fallback.
    /// </summary>
    public required string ProgressTitleFallback { get; init; }

    /// <summary>
    /// Gets the progress notification body resource key.
    /// </summary>
    public required string ProgressBodyKey { get; init; }

    /// <summary>
    /// Gets the progress notification body fallback ({0} is the content name, {1} is the version).
    /// </summary>
    public required string ProgressBodyFallback { get; init; }

    /// <summary>
    /// Gets the acquisition-failed title resource key.
    /// </summary>
    public required string AcquireFailedTitleKey { get; init; }

    /// <summary>
    /// Gets the acquisition-failed title fallback.
    /// </summary>
    public required string AcquireFailedTitleFallback { get; init; }

    /// <summary>
    /// Gets the acquisition-failed result format ({0} is the content name, {1} is the error).
    /// </summary>
    public required string AcquireFailedFormat { get; init; }

    /// <summary>
    /// Gets the updated title resource key.
    /// </summary>
    public required string UpdatedTitleKey { get; init; }

    /// <summary>
    /// Gets the updated title fallback.
    /// </summary>
    public required string UpdatedTitleFallback { get; init; }

    /// <summary>
    /// Gets the partially-updated title resource key.
    /// </summary>
    public required string PartialUpdatedTitleKey { get; init; }

    /// <summary>
    /// Gets the partially-updated title fallback.
    /// </summary>
    public required string PartialUpdatedTitleFallback { get; init; }

    /// <summary>
    /// Gets the update-error title resource key.
    /// </summary>
    public required string ErrorTitleKey { get; init; }

    /// <summary>
    /// Gets the update-error title fallback.
    /// </summary>
    public required string ErrorTitleFallback { get; init; }

    /// <summary>
    /// Gets the update-prompt title resource key.
    /// </summary>
    public required string PromptTitleKey { get; init; }

    /// <summary>
    /// Gets the update-prompt title fallback.
    /// </summary>
    public required string PromptTitleFallback { get; init; }

    /// <summary>
    /// Gets the update-prompt body resource key.
    /// </summary>
    public required string PromptBodyKey { get; init; }

    /// <summary>
    /// Gets the update-prompt body format ({0} is the latest version).
    /// </summary>
    public required string PromptBodyFormat { get; init; }

    /// <summary>
    /// Gets the per-item acquisition-failed format ({0} is the publisher, {1} is the id, {2} is the error).
    /// </summary>
    public required string AcquireItemFailedFormat { get; init; }

    /// <summary>
    /// Gets the message used when the provider returns no content.
    /// </summary>
    public required string NoContentMessage { get; init; }

    /// <summary>
    /// Gets the message used when acquisition yields no new manifests.
    /// </summary>
    public required string AcquireNoneMessage { get; init; }
}
