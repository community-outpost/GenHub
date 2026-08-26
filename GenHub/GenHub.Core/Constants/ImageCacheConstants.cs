namespace GenHub.Core.Constants;

/// <summary>
/// Constants for image downloading, validation, and caching.
/// </summary>
public static class ImageCacheConstants
{
    /// <summary>
    /// Maximum allowed image download payload in bytes (15 MB).
    /// </summary>
    public const long MaxImageDownloadSizeBytes = 15L * 1024 * 1024;

    /// <summary>
    /// Maximum number of bitmap entries stored in the memory LRU cache.
    /// </summary>
    public const int MaxMemoryCacheEntries = 200;

    /// <summary>
    /// Maximum disk cache size in bytes (250 MB).
    /// </summary>
    public const long MaxDiskCacheSizeBytes = 250L * 1024 * 1024;

    /// <summary>
    /// Time-to-live for disk-cached images in days.
    /// </summary>
    public const int DiskCacheTtlDays = 30;

    /// <summary>
    /// Default HTTP timeout in seconds for downloading images.
    /// </summary>
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>
    /// Maximum allowed HTTP redirects when downloading images.
    /// </summary>
    public const int MaxRedirects = 5;
}
