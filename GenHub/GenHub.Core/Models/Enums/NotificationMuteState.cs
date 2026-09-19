namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines the notification mute state.
/// </summary>
public enum NotificationMuteState
{
    /// <summary>
    /// Not muted; notifications are shown normally.
    /// </summary>
    None,

    /// <summary>
    /// Muted for the current session only (resets on app restart).
    /// </summary>
    Session,

    /// <summary>
    /// Muted persistently (saved to user settings).
    /// </summary>
    Persistent,
}
