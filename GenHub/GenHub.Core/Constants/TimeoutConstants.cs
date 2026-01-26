using System;

namespace GenHub.Core.Constants;

/// <summary>
/// Centralized timeout values for various operations.
/// </summary>
public static class TimeoutConstants
{
    /// <summary>
    /// Default timeout for general HTTP requests (30 seconds).
    /// </summary>
    public const int DefaultHttpRequestSeconds = 30;

    /// <summary>
    /// Timeout for ModDB browser navigation (30 seconds).
    /// </summary>
    public const int ModDBDiscoveryBrowserTimeoutSeconds = 30;

    /// <summary>
    /// Timeout for ModDB content selector waiting (5 seconds).
    /// </summary>
    public const int ModDBDiscoverySelectorTimeoutSeconds = 5;

    /// <summary>
    /// Gets the ModDB discovery browser timeout as a TimeSpan.
    /// </summary>
    public static TimeSpan ModDBDiscoveryBrowserTimeout => TimeSpan.FromSeconds(ModDBDiscoveryBrowserTimeoutSeconds);

    /// <summary>
    /// Gets the ModDB discovery selector timeout as a TimeSpan.
    /// </summary>
    public static TimeSpan ModDBDiscoverySelectorTimeout => TimeSpan.FromSeconds(ModDBDiscoverySelectorTimeoutSeconds);

    /// <summary>
    /// Default timeout for catalog refresh operations (30 seconds).
    /// </summary>
    public const int CatalogRefreshSeconds = 30;

    /// <summary>
    /// Gets the default HTTP request timeout as a TimeSpan.
    /// </summary>
    public static TimeSpan DefaultHttpRequest => TimeSpan.FromSeconds(DefaultHttpRequestSeconds);

    /// <summary>
    /// Gets the catalog refresh timeout as a TimeSpan.
    /// </summary>
    public static TimeSpan CatalogRefresh => TimeSpan.FromSeconds(CatalogRefreshSeconds);

    /// <summary>
    /// Default timeout for content downloads (30 minutes).
    /// </summary>
    public static readonly TimeSpan ContentDownload = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Default timeout for updater operations (10 minutes).
    /// </summary>
    public static readonly TimeSpan Updater = TimeSpan.FromMinutes(10);
}
