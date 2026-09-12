using System.Diagnostics.CodeAnalysis;

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
    /// Maximum total memory cache budget for decoded bitmaps in bytes (128 MB).
    /// </summary>
    public const long MaxMemoryCacheSizeBytes = 128L * 1024 * 1024;

    /// <summary>
    /// Maximum decoded size in bytes for a single cached image (32 MB).
    /// </summary>
    public const long MaxDecodedImageSizeBytes = 32L * 1024 * 1024;

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

    /// <summary>
    /// Fixed referrer URL required by ModDB to serve image requests and prevent hotlink blocking.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "ModDB requires its own fixed referrer to serve images and prevent hotlink blocking.")]
    public const string ModDbReferrerUrl = "https://www.moddb.com/";
}
