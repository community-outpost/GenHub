using System;
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

    /// <summary>
    /// Deterministic placeholder image shown when content has no icon or thumbnail.
    /// A fixed seed keeps the placeholder stable across cards and sessions.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Stable placeholder image endpoint for content without artwork.")]
    /// <summary>
    /// Host name for the Picsum placeholder service.
    /// </summary>
    public const string PicsumHost = "picsum.photos";

    /// <summary>
    /// Checks if the specified URL points to a Picsum placeholder image.
    /// </summary>
    /// <param name="url">The URL to test.</param>
    /// <returns><c>true</c> if the URL is a Picsum URL; otherwise, <c>false</c>.</returns>
    public static bool IsPicsumUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        return url.Contains(PicsumHost, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Deterministic placeholder image shown when content has no icon or thumbnail.
    /// A fixed seed keeps the placeholder stable across cards and sessions.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Stable placeholder image endpoint for content without artwork.")]
    public const string DefaultContentImageUrl = "https://picsum.photos/seed/genhub/640/360";

    /// <summary>
    /// Generates a deterministic placeholder image URL with a given seed and dimensions.
    /// </summary>
    /// <param name="seed">The unique seed string.</param>
    /// <param name="width">The image width in pixels.</param>
    /// <param name="height">The image height in pixels.</param>
    /// <returns>A formatted Picsum photo URL.</returns>
    [SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Stable placeholder image endpoint for artwork fallbacks.")]
    public static string GetPicsumUrl(string? seed, int width, int height)
    {
        var safeSeed = string.IsNullOrWhiteSpace(seed) ? "genhub" : Uri.EscapeDataString(seed.Trim().ToLowerInvariant());
        return $"https://picsum.photos/seed/{safeSeed}/{width}/{height}";
    }
}
