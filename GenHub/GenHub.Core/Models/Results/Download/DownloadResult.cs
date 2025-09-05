using System;

namespace GenHub.Core.Models.Results;

/// <summary>Result of a file download operation.</summary>
public class DownloadResult(bool success, string? filePath = null, long bytesDownloaded = 0, string? errorMessage = null, TimeSpan? elapsed = null, bool hashVerified = false)
    : ResultBase(success, errorMessage, elapsed ?? TimeSpan.Zero)
{
    /// <summary>Gets the path to the downloaded file.</summary>
    public string? FilePath { get; } = filePath;

    /// <summary>Gets the number of bytes downloaded.</summary>
    public long BytesDownloaded { get; } = bytesDownloaded;

    /// <summary>Gets a value indicating whether hash verification passed.</summary>
    public bool HashVerified { get; } = hashVerified;

    /// <summary>Gets the average download speed in bytes per second.</summary>
    public long AverageSpeedBytesPerSecond =>
        Elapsed.TotalSeconds > 0 ? (long)(BytesDownloaded / Elapsed.TotalSeconds) : 0;

    /// <summary>Gets the formatted bytes downloaded (e.g. "1.2 MB").</summary>
    public string FormattedBytesDownloaded => FormatBytes(BytesDownloaded);

    /// <summary>Gets the formatted average download speed (e.g. "1.2 MB/s").</summary>
    public string FormattedSpeed => FormatBytes(AverageSpeedBytesPerSecond) + "/s";

    /// <summary>Gets the error message if the download failed.</summary>
    public string? ErrorMessage => FirstError;

    /// <summary>
    /// Creates a successful download result.
    /// </summary>
    /// <param name="filePath">The path to the downloaded file.</param>
    /// <param name="bytesDownloaded">The number of bytes downloaded.</param>
    /// <param name="elapsed">Time taken for the download.</param>
    /// <param name="hashVerified">Whether hash verification passed.</param>
    /// <returns>A successful download result.</returns>
    public static DownloadResult CreateSuccess(
        string filePath,
        long bytesDownloaded,
        TimeSpan elapsed,
        bool hashVerified = false) =>
        new(true, filePath, bytesDownloaded, null, elapsed, hashVerified);

    /// <summary>
    /// Creates a failed download result.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    /// <param name="bytesDownloaded">The number of bytes downloaded before failure.</param>
    /// <param name="elapsed">Time taken before failure.</param>
    /// <returns>A failed download result.</returns>
    public static DownloadResult CreateFailed(
        string errorMessage,
        long bytesDownloaded = 0,
        TimeSpan? elapsed = null) =>
        new(false, null, bytesDownloaded, errorMessage, elapsed);

    /// <summary>Formats bytes into a human-readable string.</summary>
    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }

        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.0} {1}", len, sizes[order]);
    }
}
