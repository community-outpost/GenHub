namespace GenHub.Core.Constants;

/// <summary>
/// Time intervals and durations used throughout the application.
/// </summary>
public static class TimeIntervals
{
    /// <summary>
    /// Default timeout for updater operations.
    /// </summary>
    public static readonly TimeSpan UpdaterTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Default timeout for download operations.
    /// </summary>
    public static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Delay for hiding UI notifications.
    /// </summary>
    public static readonly TimeSpan NotificationHideDelay = TimeSpan.FromMilliseconds(3000);
}