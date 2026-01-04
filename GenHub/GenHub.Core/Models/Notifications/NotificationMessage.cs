using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Notifications;

/// <summary>
/// Represents a notification message to be displayed to user.
/// </summary>
public record NotificationMessage
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
    /// When null, the notification must be manually dismissed by clicking the X button.
    /// </summary>
    public int? AutoDismissMilliseconds { get; init; }

    /// <summary>
    /// Gets the collection of actions available for this notification.
    /// </summary>
    public IReadOnlyList<NotificationAction> Actions { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether this notification has any actionable buttons.
    /// </summary>
    public bool IsActionable => Actions != null && Actions.Count > 0;

    /// <summary>
    /// Gets a value indicating whether this notification should persist in the feed
    /// even after being dismissed from the toast view.
    /// </summary>
    public bool IsPersistent { get; init; }

    /// <summary>
    /// Gets a value indicating whether this notification has been read.
    /// </summary>
    public bool IsRead { get; init; }

    /// <summary>
    /// Gets a value indicating whether this notification has been dismissed.
    /// </summary>
    public bool IsDismissed { get; init; }

    /// <summary>
    /// Gets a value indicating whether this notification should be shown in the badge count.
    /// When true, this notification will increment the unread badge counter on the notification bell.
    /// When false (default), the notification will appear in the feed but not affect the badge count.
    /// </summary>
    public bool ShowInBadge { get; init; }

    /// <summary>
    /// Gets the text for the first action button (backward compatibility).
    /// </summary>
    public string? ActionText => Actions?.Count > 0 ? Actions[0].Text : null;

    /// <summary>
    /// Gets the callback for the first action button (backward compatibility).
    /// </summary>
    public Action? Action => Actions?.Count > 0 ? Actions[0].Callback : null;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationMessage"/> class.
    /// </summary>
    /// <param name="type">The notification type.</param>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    /// <param name="autoDismissMilliseconds">Optional auto-dismiss timeout.</param>
    /// <param name="actionText">The action button text (backward compatibility).</param>
    /// <param name="action">The action to execute (backward compatibility).</param>
    /// <param name="actions">The collection of actions available for this notification.</param>
    /// <param name="isPersistent">Whether the notification should persist in the feed.</param>
    /// <param name="showInBadge">Whether this notification should be shown in the badge count (default: false).</param>
    public NotificationMessage(
        NotificationType type,
        string title,
        string message,
        int? autoDismissMilliseconds = 5000,
        string? actionText = null,
        Action? action = null,
        IReadOnlyList<NotificationAction>? actions = null,
        bool isPersistent = false,
        bool showInBadge = false)
    {
        Id = Guid.NewGuid();
        Type = type;
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Timestamp = DateTime.UtcNow;
        AutoDismissMilliseconds = autoDismissMilliseconds;
        IsPersistent = isPersistent;
        ShowInBadge = showInBadge;
        IsRead = false;
        IsDismissed = false;

        // Support both old single-action and new multi-action patterns
        if (actions != null && actions.Count > 0)
        {
            Actions = actions;
        }
        else if (action != null && !string.IsNullOrEmpty(actionText))
        {
            Actions =
            [
                new NotificationAction(actionText, action),
            ];
        }
    }

    /// <summary>
    /// Creates a new notification message with the specified read status.
    /// </summary>
    /// <param name="isRead">The read status.</param>
    /// <returns>A new notification message.</returns>
    public NotificationMessage WithIsRead(bool isRead) => this with { IsRead = isRead };

    /// <summary>
    /// Creates a new notification message with the specified dismissed status.
    /// </summary>
    /// <param name="isDismissed">The dismissed status.</param>
    /// <returns>A new notification message.</returns>
    public NotificationMessage WithIsDismissed(bool isDismissed) => this with { IsDismissed = isDismissed };
}
