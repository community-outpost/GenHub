using System;
using System.Collections.Generic;

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
    /// Minimum allowed concurrent downloads.
    /// </summary>
    public const int MinConcurrentDownloads = 1;

    /// <summary>
    /// Upper bound on concurrent file downloads for bulk delivery.
    /// </summary>
    public const int MaxDeliveryConcurrency = 8;

    /// <summary>
    /// SocketsHttpHandler connection timeout in seconds.
    /// </summary>
    public const int HttpConnectTimeoutSeconds = 30;

    /// <summary>
    /// SocketsHttpHandler pooled connection lifetime in minutes.
    /// </summary>
    public const int HttpPooledConnectionLifetimeMinutes = 5;

    /// <summary>
    /// SocketsHttpHandler pooled connection idle timeout in seconds.
    /// </summary>
    public const int HttpPooledConnectionIdleTimeoutSeconds = 60;

    /// <summary>
    /// SocketsHttpHandler maximum connections per server.
    /// </summary>
    public const int HttpMaxConnectionsPerServer = 16;

    /// <summary>
    /// Default maximum retry attempts for failed downloads.
    /// </summary>
    public const int MaxRetryAttempts = 3;

    /// <summary>
    /// Maximum redirect hops followed when validated redirects are enabled.
    /// </summary>
    public const int MaxRedirects = 5;

    /// <summary>
    /// Default download timeout in seconds.
    /// </summary>
    public const int TimeoutSeconds = 600; // 10 minutes

    /// <summary>
    /// Default buffer size for file operations (4KB).
    /// </summary>
    public const int FileBufferSizeBytes = 4096;

    /// <summary>
    /// Binary archive and payload extensions that should never receive HTML error responses.
    /// </summary>
    public static readonly IReadOnlySet<string> BinaryTargetExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".big", ".gib", ".ctr", ".exe", ".dat",
    };

    /// <summary>
    /// Checks whether a file extension represents a binary target that should not receive an HTML response.
    /// </summary>
    /// <param name="extension">The file extension, including leading period.</param>
    /// <returns><c>true</c> if the extension is a known binary payload format; otherwise, <c>false</c>.</returns>
    public static bool IsBinaryTargetExtension(string? extension) =>
        !string.IsNullOrEmpty(extension) && BinaryTargetExtensions.Contains(extension);
}
