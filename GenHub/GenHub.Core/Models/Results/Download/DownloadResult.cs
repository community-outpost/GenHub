using GenHub.Core.Helpers;

namespace GenHub.Core.Models.Results;

/// <summary>
/// Result of a file download operation.
/// </summary>
public class DownloadResult : ResultBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadResult"/> class.
    /// Initializes a new instance of the DownloadResult class.
    /// </summary>
    /// <param name="success">Indicates if the operation was successful.</param>
    /// <param name="filePath">The path to the downloaded file.</param>
    /// <param name="bytesDownloaded">The number of bytes downloaded.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <param name="hashVerified">Whether the hash was verified.</param>
    /// <param name="averageSpeedBytesPerSecond">The average speed.</param>
    /// <param name="errors">The errors.</param>
    protected DownloadResult(
        bool success,
        string? filePath,
        long bytesDownloaded,
        TimeSpan elapsed,
        bool hashVerified,
        double averageSpeedBytesPerSecond,
        IEnumerable<string>? errors = null)
        : base(success, errors, elapsed)
    {
        FilePath = filePath;
        BytesDownloaded = bytesDownloaded;
        HashVerified = hashVerified;
        AverageSpeedBytesPerSecond = averageSpeedBytesPerSecond;
        FormattedBytesDownloaded = ByteFormatHelper.FormatBytes(bytesDownloaded);
        FormattedSpeed = FormatSpeed(averageSpeedBytesPerSecond);
    }

    /// <summary>
    /// Gets the path to the downloaded file.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// Gets the number of bytes downloaded.
    /// </summary>
    public long BytesDownloaded { get; }

    /// <summary>
    /// Gets a value indicating whether the hash verification passed.
    /// </summary>
    public bool HashVerified { get; }

    /// <summary>
    /// Gets the average download speed in bytes per second.
    /// </summary>
    public double AverageSpeedBytesPerSecond { get; }

    /// <summary>
    /// Gets the formatted bytes downloaded (e.g., "1.2 MB").
    /// </summary>
    public string FormattedBytesDownloaded { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the formatted average download speed (e.g., "1.2 MB/s").
    /// </summary>
    public string FormattedSpeed { get; private set; } = string.Empty;

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
        bool hashVerified = false)
    {
        var averageSpeed = elapsed.TotalSeconds > 0 ? bytesDownloaded / elapsed.TotalSeconds : 0;
        return new DownloadResult(true, filePath, bytesDownloaded, elapsed, hashVerified, averageSpeed);
    }

    /// <summary>
    /// Creates a failed download result.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    /// <param name="bytesDownloaded">The bytes downloaded before failure.</param>
    /// <param name="elapsed">Time taken before failure.</param>
    /// <returns>A failed download result.</returns>
    public static DownloadResult CreateFailure(
        string errorMessage,
        long bytesDownloaded = 0,
        TimeSpan elapsed = default)
    {
        return new DownloadResult(false, null, bytesDownloaded, elapsed, false, 0, new[] { errorMessage });
    }

    /// <summary>
    /// Formats bytes per second into a human-readable speed string.
    /// </summary>
    private static string FormatSpeed(double bytesPerSecond)
    {
        return ByteFormatHelper.FormatBytes((long)bytesPerSecond) + "/s";
    }
}