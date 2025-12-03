using System;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Notifications;

/// <summary>
/// Represents a notification message to be displayed to the user.
/// </summary>
public class NotificationMessage
{
    /// <summary>
    /// Gets the unique identifier for this notification.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the type of notification.
    /// </summary>
    public NotificationType Type { get; init; }

    /// <summary>
    /// Gets the notification title.
    /// </summary>
    public string Title { get; init; }

    /// <summary>
    /// Gets the notification message content.
    /// </summary>
    public string Message { get; init; }

    /// <summary>
    /// Gets the timestamp when the notification was created.
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// Gets the auto-dismiss timeout in milliseconds. Null means no auto-dismiss.
    /// </summary>
    public int? AutoDismissMilliseconds { get; init; }

    /// <summary>
    /// Gets a value indicating whether this notification has an actionable button.
    /// </summary>
    public bool IsActionable => !string.IsNullOrEmpty(ActionText) && Action != null;

    /// <summary>
    /// Gets the text for the action button.
    /// </summary>
    public string? ActionText { get; init; }

    /// <summary>
    /// Gets the action to execute when the action button is clicked.
    /// </summary>
    public Action? Action { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationMessage"/> class.
    /// </summary>
    /// <param name="type">The notification type.</param>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    /// <param name="autoDismissMilliseconds">Optional auto-dismiss timeout.</param>
    /// <param name="actionText">The action button text.</param>
    /// <param name="action">The action to execute.</param>
    public NotificationMessage(
        NotificationType type,
        string title,
        string message,
        int? autoDismissMilliseconds = 5000,
        string? actionText = null,
        Action? action = null)
    {
        Id = Guid.NewGuid();
        Type = type;
        Title = title;
        Message = message;
        Timestamp = DateTime.UtcNow;
        AutoDismissMilliseconds = autoDismissMilliseconds;
        ActionText = actionText;
        Action = action;
    }
}