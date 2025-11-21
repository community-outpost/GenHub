using GenHub.Core.Constants;

namespace GenHub.Core.Models.Common;

/// <summary>
/// Configuration for file download operations.
/// </summary>
public sealed class DownloadConfiguration
{
    /// <summary>
    /// Default timeout for download operations.
    /// </summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(DownloadDefaults.TimeoutSeconds);

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadConfiguration"/> class.
    /// </summary>
    public DownloadConfiguration()
    {
        UserAgent = ApiConstants.DefaultUserAgent;
        BufferSize = DownloadDefaults.BufferSizeBytes;
        Timeout = DefaultTimeout;
        Url = string.Empty;
        DestinationPath = string.Empty;
        OverwriteExisting = true;
        ProgressReportingInterval = TimeSpan.FromMilliseconds(100);
        Headers = [];
        VerifySslCertificate = true;
        MaxRetryAttempts = 3;
        RetryDelay = TimeSpan.FromSeconds(1);
    }

    /// <summary>Gets or sets the user agent string.</summary>
    public string UserAgent { get; set; }

    /// <summary>Gets or sets the buffer size for reading data.</summary>
    public int BufferSize { get; set; }

    /// <summary>Gets or sets the timeout for the download operation.</summary>
    public TimeSpan Timeout { get; set; }

    /// <summary>Gets or sets the download URL.</summary>
    public string Url { get; set; }

    /// <summary>Gets or sets the destination file path.</summary>
    public string DestinationPath { get; set; }

    /// <summary>Gets or sets the expected SHA256 hash for verification.</summary>
    public string? ExpectedHash { get; set; }

    /// <summary>Gets or sets a value indicating whether to overwrite existing files.</summary>
    public bool OverwriteExisting { get; set; }

    /// <summary>Gets or sets the progress reporting interval.</summary>
    public TimeSpan ProgressReportingInterval { get; set; }

    /// <summary>Gets or sets custom HTTP headers.</summary>
    public Dictionary<string, string> Headers { get; set; }

    /// <summary>Gets or sets a value indicating whether to verify SSL certificates.</summary>
    public bool VerifySslCertificate { get; set; }

    /// <summary>Gets or sets the maximum number of retry attempts.</summary>
    public int MaxRetryAttempts { get; set; }

    /// <summary>Gets or sets the delay between retry attempts.</summary>
    public TimeSpan RetryDelay { get; set; }
}