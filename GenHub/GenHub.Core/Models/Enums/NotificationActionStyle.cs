namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines the visual style of a notification action button.
/// </summary>
public enum NotificationActionStyle
{
    /// <summary>
    /// Primary action - typically blue, used for main confirm/accept actions.
    /// </summary>
    Primary,

    /// <summary>
    /// Secondary action - typically gray, used for cancel/dismiss actions.
    /// </summary>
    Secondary,

    /// <summary>
    /// Danger action - typically red, used for destructive/deny actions.
    /// </summary>
    Danger,

    /// <summary>
    /// Success action - typically green, used for positive/approve actions.
    /// </summary>
    Success,
}
