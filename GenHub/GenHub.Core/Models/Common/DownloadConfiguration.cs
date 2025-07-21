using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Common;

/// <summary>
/// Configuration for file download operations.
/// </summary>
public sealed class DownloadConfiguration
{
    /// <summary>
    /// Default User-Agent string for GenHub downloads.
    /// TODO: Replace with dynamic version from central configuration when app settings system is implemented.
    /// Consider using IConfiguration or similar service to inject actual application version.
    /// </summary>
    private const string DefaultUserAgent = "GenHub/1.0";

    /// <summary>
    /// Default buffer size for file download operations (80KB).
    /// This size provides a good balance between memory usage and I/O performance.
    /// Larger buffers reduce I/O calls but use more memory; smaller buffers do the opposite.
    /// 80KB is chosen as a compromise that works well for most network conditions.
    /// TODO: Consider making this configurable through application settings.
    /// </summary>
    private const int DefaultBufferSize = 81920; // 80KB

    /// <summary>
    /// Default timeout for download operations.
    /// Set to 30 minutes to accommodate users with low bandwidth connections.
    /// GenHub (~55MB) requires minimum 30KB/s bandwidth to complete within this timeout.
    /// TODO: Make this configurable through application settings in the future.
    /// </summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Gets or sets the user agent string.</summary>
    public string UserAgent { get; set; } = DefaultUserAgent;

    /// <summary>Gets or sets the buffer size for reading data.</summary>
    public int BufferSize { get; set; } = DefaultBufferSize;

    /// <summary>Gets or sets the timeout for the download operation.</summary>
    public TimeSpan Timeout { get; set; } = DefaultTimeout;

    /// <summary>Gets or sets the download URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the destination file path.</summary>
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the expected SHA256 hash for verification.</summary>
    public string? ExpectedHash { get; set; }

    /// <summary>Gets or sets a value indicating whether to overwrite existing files.</summary>
    public bool OverwriteExisting { get; set; } = true;

    /// <summary>Gets or sets the progress reporting interval.</summary>
    public TimeSpan ProgressReportingInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Gets or sets custom HTTP headers.</summary>
    public Dictionary<string, string> Headers { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether to verify SSL certificates.</summary>
    public bool VerifySslCertificate { get; set; } = true;

    /// <summary>Gets or sets the maximum number of retry attempts.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Gets or sets the delay between retry attempts.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
}
