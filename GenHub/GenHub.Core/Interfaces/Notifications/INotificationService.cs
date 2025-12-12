using GenHub.Core.Models.Notifications;

namespace GenHub.Core.Interfaces.Notifications;

/// <summary>
/// Service for managing and displaying notifications.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Gets the observable stream of notifications.
    /// </summary>
    IObservable<NotificationMessage> Notifications { get; }

    /// <summary>
    /// Gets the observable stream of dismiss requests.
    /// </summary>
    IObservable<Guid> DismissRequests { get; }

    /// <summary>
    /// Gets the observable stream of dismiss all requests.
    /// </summary>
    IObservable<bool> DismissAllRequests { get; }

    /// <summary>
    /// Shows an informational notification.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    /// <param name="autoDismissMs">Optional auto-dismiss timeout in milliseconds.</param>
    void ShowInfo(string title, string message, int? autoDismissMs = null);

    /// <summary>
    /// Shows a success notification.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    /// <param name="autoDismissMs">Optional auto-dismiss timeout in milliseconds.</param>
    void ShowSuccess(string title, string message, int? autoDismissMs = null);

    /// <summary>
    /// Shows a warning notification.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    /// <param name="autoDismissMs">Optional auto-dismiss timeout in milliseconds.</param>
    void ShowWarning(string title, string message, int? autoDismissMs = null);

    /// <summary>
    /// Shows an error notification.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    /// <param name="autoDismissMs">Optional auto-dismiss timeout in milliseconds.</param>
    void ShowError(string title, string message, int? autoDismissMs = null);

    /// <summary>
    /// Shows a custom notification.
    /// </summary>
    /// <param name="notification">The notification to show.</param>
    void Show(NotificationMessage notification);

    /// <summary>
    /// Dismisses a specific notification.
    /// </summary>
    /// <param name="notificationId">The ID of the notification to dismiss.</param>
    void Dismiss(Guid notificationId);

    /// <summary>
    /// Dismisses all active notifications.
    /// </summary>
    void DismissAll();
}