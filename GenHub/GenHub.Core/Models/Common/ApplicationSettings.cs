using GenHub.Core.Constants;

namespace GenHub.Core.Models.Common;

/// <summary>
/// Core application state and configuration settings.
/// </summary>
public class ApplicationSettings : ICloneable
{
    /// <summary>Gets or sets the maximum number of concurrent downloads allowed. Defaults to <see cref="DownloadDefaults.MaxConcurrentDownloads"/>.</summary>
    public int MaxConcurrentDownloads { get; set; } = DownloadDefaults.MaxConcurrentDownloads;

    /// <summary>Gets or sets a value indicating whether downloads are allowed to continue in the background.</summary>
    public bool AllowBackgroundDownloads { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to automatically check for updates on startup.</summary>
    public bool AutoCheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>Gets or sets the timestamp of the last update check in ISO 8601 format.</summary>
    public string? LastUpdateCheckTimestamp { get; set; }

    /// <summary>Gets or sets a value indicating whether detailed logging information is enabled.</summary>
    public bool EnableDetailedLogging { get; set; }

    /// <summary>Gets or sets the buffer size (in bytes) for file download operations.</summary>
    public int DownloadBufferSize { get; set; } = DownloadDefaults.BufferSizeBytes;

    /// <summary>Gets or sets the download timeout in seconds.</summary>
    public int DownloadTimeoutSeconds { get; set; } = DownloadDefaults.TimeoutSeconds;

    /// <summary>Gets or sets the user-agent string for downloads.</summary>
    public string? DownloadUserAgent { get; set; } = AppConstants.DefaultUserAgent;

    /// <summary>Gets or sets the cache directory path.</summary>
    public string? CachePath { get; set; }

    /// <summary>Gets or sets the application data directory path where metadata is stored.</summary>
    public string? ApplicationDataPath { get; set; }

    /// <summary>
    /// Gets or sets the subscribed PR number for update notifications.
    /// </summary>
    public int? SubscribedPrNumber { get; set; }

    /// <summary>
    /// Gets or sets the subscribed branch name for update notifications (e.g. "development").
    /// </summary>
    public string? SubscribedBranch { get; set; }

    /// <summary>
    /// Gets or sets the last dismissed update version to prevent repeated notifications.
    /// </summary>
    public string? DismissedUpdateVersion { get; set; }

    /// <summary>
    /// Creates a deep copy of the current ApplicationSettings instance.
    /// </summary>
    /// <returns>A new ApplicationSettings instance.</returns>
    public object Clone()
    {
        return new ApplicationSettings
        {
            MaxConcurrentDownloads = MaxConcurrentDownloads,
            AllowBackgroundDownloads = AllowBackgroundDownloads,
            AutoCheckForUpdatesOnStartup = AutoCheckForUpdatesOnStartup,
            LastUpdateCheckTimestamp = LastUpdateCheckTimestamp,
            EnableDetailedLogging = EnableDetailedLogging,
            DownloadBufferSize = DownloadBufferSize,
            DownloadTimeoutSeconds = DownloadTimeoutSeconds,
            DownloadUserAgent = DownloadUserAgent,
            CachePath = CachePath,
            ApplicationDataPath = ApplicationDataPath,
            SubscribedPrNumber = SubscribedPrNumber,
            SubscribedBranch = SubscribedBranch,
            DismissedUpdateVersion = DismissedUpdateVersion,
        };
    }
}
