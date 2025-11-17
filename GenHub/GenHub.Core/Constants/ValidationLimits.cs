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
    /// Minimum allowed download buffer size in bytes.
    /// </summary>
    public const int MinDownloadBufferSizeBytes = 4096; // 4KB

    /// <summary>
    /// Maximum allowed download buffer size in bytes.
    /// </summary>
    public const int MaxDownloadBufferSizeBytes = 1048576; // 1MB
}