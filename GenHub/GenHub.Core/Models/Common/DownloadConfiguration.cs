using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Common;

/// <summary>
/// Configuration for file download operations.
/// </summary>
public class DownloadConfiguration
{
    /// <summary>Gets or sets the download URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the destination file path.</summary>
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the expected SHA256 hash for verification.</summary>
    public string? ExpectedHash { get; set; }

    /// <summary>Gets or sets a value indicating whether to overwrite existing files.</summary>
    public bool OverwriteExisting { get; set; } = true;

    /// <summary>Gets or sets the timeout for the download operation.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Gets or sets the buffer size for reading data.</summary>
    public int BufferSize { get; set; } = 81920; // 80KB

    /// <summary>Gets or sets the progress reporting interval.</summary>
    public TimeSpan ProgressReportingInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Gets or sets custom HTTP headers.</summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>Gets or sets the user agent string.</summary>
    public string UserAgent { get; set; } = "GenHub/1.0";

    /// <summary>Gets or sets a value indicating whether to verify SSL certificates.</summary>
    public bool VerifySslCertificate { get; set; } = true;

    /// <summary>Gets or sets the maximum number of retry attempts.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Gets or sets the delay between retry attempts.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
}
