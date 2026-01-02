using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Notifications;

/// <summary>
/// Represents a single action button on a notification.
/// </summary>
public class NotificationAction
{
    /// <summary>
    /// Gets the text to display on the action button.
    /// </summary>
    public string Text { get; init; }

    /// <summary>
    /// Gets the callback to execute when the action button is clicked.
    /// </summary>
    public Action? Callback { get; private set; }

    /// <summary>
    /// Gets the style of the action button.
    /// </summary>
    public NotificationActionStyle Style { get; init; }

    /// <summary>
    /// Gets a value indicating whether the notification should be dismissed after executing this action.
    /// </summary>
    public bool DismissOnExecute { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationAction"/> class.
    /// </summary>
    /// <param name="text">The text to display on the action button.</param>
    /// <param name="callback">The callback to execute when the action button is clicked.</param>
    /// <param name="style">The style of the action button.</param>
    /// <param name="dismissOnExecute">Whether the notification should be dismissed after executing this action.</param>
    public NotificationAction(
        string text,
        Action callback,
        NotificationActionStyle style = NotificationActionStyle.Primary,
        bool dismissOnExecute = true)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Callback = callback ?? throw new ArgumentNullException(nameof(callback));
        Style = style;
        DismissOnExecute = dismissOnExecute;
    }

    /// <summary>
    /// Clears the callback to prevent memory leaks.
    /// </summary>
    public void ClearCallback()
    {
        Callback = null;
    }
}