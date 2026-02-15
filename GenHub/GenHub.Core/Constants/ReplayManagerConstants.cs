namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the Replay Manager feature.
/// </summary>
public static class ReplayManagerConstants
{
    /// <summary>
    /// Maximum size for a single replay file in bytes (1 MB).
    /// </summary>
    public const long MaxReplaySizeBytes = 1024 * 1024;

    /// <summary>
    /// Maximum upload bytes per period (10 MB).
    /// </summary>
    public const long MaxUploadBytesPerPeriod = 10 * 1024 * 1024;

    /// <summary>
    /// Prefix for temporary import files.
    /// </summary>
    public const string TempImportFilePrefix = "genhub_import_";

    /// <summary>
    /// Prefix for temporary share files.
    /// </summary>
    public const string TempShareFilePrefix = "genhub_share_";

    /// <summary>
    /// Default file name for imported replays.
    /// </summary>
    public const string DefaultImportedReplayFileName = "imported_replay.rep";

    /// <summary>
    /// Name of the subdirectory for auto-saved replays.
    /// </summary>
    public const string SavedReplaysDirectoryName = "Saved";

    /// <summary>
    /// Default replay file name that gets overwritten by the game.
    /// </summary>
    public const string DefaultReplayFileName = "00000000.rep";

    /// <summary>
    /// Interval in milliseconds for checking file stability.
    /// </summary>
    public const int FileStabilityCheckIntervalMs = 2000;

    /// <summary>
    /// Number of consecutive stability checks required before considering file complete.
    /// </summary>
    public const int FileStabilityCheckCount = 3;

    /// <summary>
    /// Minimum file size in bytes to consider a replay valid (10 KB).
    /// </summary>
    public const long MinimumReplayFileSizeBytes = 10 * 1024;

    /// <summary>
    /// Magic bytes for replay file header validation.
    /// </summary>
    public const string ReplayMagicBytes = "GENREP";

    /// <summary>
    /// Date format for auto-saved replay filenames.
    /// </summary>
    public const string SavedReplayDateFormat = "yyyyMMdd_HHmmss";

    /// <summary>
    /// Grace period in seconds to allow stability checks to complete before stopping monitoring.
    /// </summary>
    public const int StopMonitoringGracePeriodSeconds = 10;

    /// <summary>
    /// Maximum number of retry attempts when saving a replay file encounters an I/O conflict.
    /// </summary>
    public const int SaveRetryMaxAttempts = 3;

    /// <summary>
    /// Maximum byte length for reading null-terminated strings from replay files.
    /// </summary>
    public const int MaxStringReadBytes = 4096;

    /// <summary>
    /// Maximum number of uniqueness suffix attempts when finding a non-colliding file path.
    /// </summary>
    public const int MaxUniquePathAttempts = 1000;
}