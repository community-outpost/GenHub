namespace GenHub.Core.Constants;

/// <summary>
/// Validation limits and constraints.
/// </summary>
public static class ValidationLimits
{
    /// <summary>
    /// Minimum allowed concurrent downloads.
    /// </summary>
    public const int MinConcurrentDownloads = 1;

    /// <summary>
    /// Maximum allowed concurrent downloads.
    /// </summary>
    public const int MaxConcurrentDownloads = 10;

    /// <summary>
    /// Minimum allowed download timeout in seconds.
    /// </summary>
    public const int MinDownloadTimeoutSeconds = 30;

    /// <summary>
    /// Maximum allowed download timeout in seconds.
    /// </summary>
    public const int MaxDownloadTimeoutSeconds = 3600; // 1 hour

    /// <summary>
    /// Minimum allowed download save timeout in milliseconds.
    /// </summary>
    public const int MinDownloadSaveTimeoutMs = 60000;

    /// <summary>
    /// Minimum allowed download buffer size in bytes.
    /// </summary>
    public const int MinDownloadBufferSizeBytes = 4096; // 4KB

    /// <summary>
    /// Maximum allowed download buffer size in bytes.
    /// </summary>
    public const int MaxDownloadBufferSizeBytes = 1048576; // 1MB

    /// <summary>
    /// Maximum number of entries allowed when extracting a guarded ZIP archive.
    /// </summary>
    public const int MaxZipArchiveEntries = 50000;

    /// <summary>
    /// Maximum uncompressed size in bytes allowed for a single guarded ZIP archive entry (2GB).
    /// </summary>
    public const long MaxZipArchiveEntryBytes = 2147483648L;

    /// <summary>
    /// Maximum total uncompressed size in bytes allowed when extracting a guarded ZIP archive (20GB).
    /// </summary>
    public const long MaxZipArchiveTotalBytes = 21474836480L;

    /// <summary>
    /// Buffer size in bytes used when copying guarded ZIP archive entries to disk (80KB).
    /// </summary>
    public const int ZipCopyBufferSize = 81920;
}
