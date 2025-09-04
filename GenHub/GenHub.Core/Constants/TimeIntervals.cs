namespace GenHub.Core.Constants;

/// <summary>
/// Time intervals and durations used throughout the application.
/// </summary>
public static class TimeIntervals
{
    /// <summary>
    /// Default progress reporting interval for downloads.
    /// </summary>
    public static readonly TimeSpan DownloadProgressInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Default timeout for updater operations.
    /// </summary>
    public static readonly TimeSpan UpdaterTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Default CAS maintenance error retry delay.
    /// </summary>
    public static readonly TimeSpan CasMaintenanceRetryDelay = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Memory update interval for UI.
    /// </summary>
    public static readonly TimeSpan MemoryUpdateInterval = TimeSpan.FromSeconds(2);
}
