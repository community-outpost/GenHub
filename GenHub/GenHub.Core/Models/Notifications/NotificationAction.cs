using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Notifications;

/// <summary>
/// Represents a single action button on a notification.
/// </summary>
public class NotificationAction(
    string text,
    Action callback,
    NotificationActionStyle style = NotificationActionStyle.Primary,
    bool dismissOnExecute = true)
{
    /// <summary>
    /// Gets the text to display on the action button.
    /// </summary>
    public string Text { get; init; } = text ?? throw new ArgumentNullException(nameof(text));

    /// <summary>
    /// Gets the callback to execute when the action button is clicked.
    /// </summary>
    public Action? Callback { get; private set; } = callback ?? throw new ArgumentNullException(nameof(callback));

    /// <summary>
    /// Gets the style of the action button.
    /// </summary>
    public NotificationActionStyle Style { get; init; } = style;

    /// <summary>
    /// Gets a value indicating whether the notification should be dismissed after executing this action.
    /// </summary>
    public bool DismissOnExecute { get; init; } = dismissOnExecute;

    /// <summary>
    /// Clears the callback to prevent memory leaks.
    /// </summary>
    public void ClearCallback()
    {
        Callback = null;
    }
}
