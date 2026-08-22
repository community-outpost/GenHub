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
    /// Maximum allowed entries in a replay ZIP archive.
    /// </summary>
    public const int MaxZipEntries = 100;

    /// <summary>
    /// Maximum aggregate uncompressed bytes for a replay ZIP archive (50 MB).
    /// </summary>
    public const long MaxAggregateUncompressedBytes = 50 * 1024 * 1024;

    /// <summary>
    /// Maximum compression ratio allowed for replay ZIP archives.
    /// </summary>
    public const double MaxCompressionRatio = 50.0;

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
    /// Notification title for delete failure.
    /// </summary>
    public const string DeleteFailedTitle = "Delete Failed";

    /// <summary>
    /// Mock path separator indicator for demo environments on Windows.
    /// </summary>
    public const string WindowsMockPathSegment = "\\Mock\\";

    /// <summary>
    /// Mock path separator indicator for demo environments on Unix.
    /// </summary>
    public const string UnixMockPathSegment = "/Mock/";
}