namespace GenHub.Core.Constants;

/// <summary>
/// Default values and limits for download operations.
/// </summary>
public static class DownloadDefaults
{
    /// <summary>
    /// Default buffer size for file download operations (80KB).
    /// </summary>
    public const int BufferSizeBytes = 81920;

    /// <summary>
    /// Minimum buffer size in kilobytes for validation.
    /// </summary>
    public const double MinBufferSizeKB = 4.0;

    /// <summary>
    /// Default buffer size in kilobytes for display purposes.
    /// </summary>
    public const double BufferSizeKB = 80.0;

    /// <summary>
    /// Maximum buffer size in kilobytes for validation.
    /// </summary>
    public const double MaxBufferSizeKB = 1024.0;

    /// <summary>
    /// Default maximum number of concurrent downloads.
    /// </summary>
    public const int MaxConcurrentDownloads = 3;

    /// <summary>
    /// Default maximum retry attempts for failed downloads.
    /// </summary>
    public const int MaxRetryAttempts = 3;

    /// <summary>
    /// Default download timeout in seconds.
    /// </summary>
    public const int TimeoutSeconds = 600; // 10 minutes

    /// <summary>
    /// Default buffer size for file operations (4KB).
    /// </summary>
    public const int FileBufferSizeBytes = 4096;
}