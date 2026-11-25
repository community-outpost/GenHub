using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;

namespace GenHub.Features.Content.Services.Reconciliation;

/// <summary>
/// Aggregates user interaction services required by <see cref="PublisherProfileReconcilerBase"/>.
/// Constructed by publisher reconcilers from their publisher-specific services.
/// </summary>
public sealed class PublisherInteractionServices(
    INotificationService notificationService,
    IDialogService dialogService,
    IUserSettingsService userSettingsService,
    ILocalizationService? localizationService)
{
    /// <summary>
    /// Gets the notification service.
    /// </summary>
    public INotificationService NotificationService { get; } = notificationService;

    /// <summary>
    /// Gets the dialog service.
    /// </summary>
    public IDialogService DialogService { get; } = dialogService;

    /// <summary>
    /// Gets the user settings service.
    /// </summary>
    public IUserSettingsService UserSettingsService { get; } = userSettingsService;

    /// <summary>
    /// Gets the localization service, or null when unavailable.
    /// </summary>
    public ILocalizationService? LocalizationService { get; } = localizationService;
}
