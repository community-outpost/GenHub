namespace GenHub.Core.Constants;

/// <summary>
/// Standard durations for notification auto-dismiss timeouts in milliseconds.
/// </summary>
public static class NotificationDurations
{
    /// <summary>
    /// Short duration: 3 seconds. For quick info messages.
    /// </summary>
    public const int Short = 3000;

    /// <summary>
    /// Medium duration: 5 seconds. For standard notifications.
    /// </summary>
    public const int Medium = 5000;

    /// <summary>
    /// Long duration: 6 seconds. For important messages requiring attention.
    /// </summary>
    public const int Long = 6000;

    /// <summary>
    /// Very long duration: 10 seconds. For complex messages users need to read.
    /// </summary>
    public const int VeryLong = 10000;

    /// <summary>
    /// Critical duration: 15 seconds. For errors requiring user action.
    /// </summary>
    public const int Critical = 15000;
}
