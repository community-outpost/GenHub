using GenHub.Core.Models.Enums;
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
    /// Gets the observable stream of notification history.
    /// </summary>
    IObservable<NotificationMessage> NotificationHistory { get; }

    /// <summary>
    /// Shows an informational notification.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    /// <param name="autoDismissMs">Optional auto-dismiss timeout in milliseconds (default: 5000ms). If null, the notification will stay until dismissed.</param>
    /// <param name="showInBadge">Whether this notification should increment the badge count (default: false).</param>
    void ShowInfo(string title, string message, int? autoDismissMs = null, bool showInBadge = false);

    /// <summary>
    /// Shows a success notification.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    /// <param name="autoDismissMs">Optional auto-dismiss timeout in milliseconds (default: 5000ms). If null, the notification will stay until dismissed.</param>
    /// <param name="showInBadge">Whether this notification should increment the badge count (default: false).</param>
    void ShowSuccess(string title, string message, int? autoDismissMs = null, bool showInBadge = false);

    /// <summary>
    /// Shows a warning notification.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    /// <param name="autoDismissMs">Optional auto-dismiss timeout in milliseconds (default: 5000ms). If null, the notification will stay until dismissed.</param>
    /// <param name="showInBadge">Whether this notification should increment the badge count (default: false).</param>
    void ShowWarning(string title, string message, int? autoDismissMs = null, bool showInBadge = false);

    /// <summary>
    /// Shows an error notification.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    /// <param name="autoDismissMs">Optional auto-dismiss timeout in milliseconds.</param>
    /// <param name="showInBadge">Whether this notification should increment the badge count (default: false).</param>
    void ShowError(string title, string message, int? autoDismissMs = null, bool showInBadge = false);

    /// <summary>
    /// Shows a custom notification.
    /// </summary>
    /// <param name="notification">The notification to show.</param>
    void Show(NotificationMessage notification);

    /// <summary>
    /// Dismisses a specific notification.
    /// </summary>
    /// <param name="notificationId">The ID of notification to dismiss.</param>
    void Dismiss(Guid notificationId);

    /// <summary>
    /// Dismisses all active notifications.
    /// </summary>
    void DismissAll();

    /// <summary>
    /// Marks a notification as read.
    /// </summary>
    /// <param name="notificationId">The ID of notification to mark as read.</param>
    void MarkAsRead(Guid notificationId);

    /// <summary>
    /// Clears all notification history.
    /// </summary>
    void ClearHistory();

    /// <summary>
    /// Gets the current notification mute state.
    /// </summary>
    NotificationMuteState MuteState { get; }

    /// <summary>
    /// Mutes notifications for the current session only (resets on app restart).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async I/O operation.</param>
    /// <returns>
    /// A <see cref="Task"/> that represents the asynchronous operation.
    /// The task completes when the session mute state has been successfully saved.
    /// </returns>
    Task MuteSession(CancellationToken cancellationToken = default);

    /// <summary>
    /// Mutes notifications persistently by saving the mute state to user settings.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async I/O operation.</param>
    /// <returns>
    /// A <see cref="Task"/> that represents the asynchronous operation.
    /// The task completes when the mute state has been successfully saved.
    /// </returns>
    Task MutePersistent(CancellationToken cancellationToken = default);

    /// <summary>
    /// Unmutes notifications and persists the state to user settings.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async I/O operation.</param>
    /// <returns>
    /// A <see cref="Task"/> that represents the asynchronous unmute operation.
    /// The task completes when notifications have been successfully unmuted.
    /// </returns>
    Task Unmute(CancellationToken cancellationToken = default);
}
