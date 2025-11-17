namespace GenHub.Core.Models.Common;

/// <summary>
/// Progress information for file downloads.
/// </summary>
public class DownloadProgress
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadProgress"/> class.
    /// </summary>
    /// <param name="bytesReceived">Bytes received.</param>
    /// <param name="totalBytes">Total bytes.</param>
    /// <param name="fileName">File name.</param>
    /// <param name="url">Download URL.</param>
    /// <param name="bytesPerSecond">Current download speed.</param>
    /// <param name="elapsedTime">Elapsed download time.</param>
    public DownloadProgress(
        long bytesReceived,
        long totalBytes,
        string fileName,
        string url,
        long bytesPerSecond,
        TimeSpan elapsedTime = default)
    {
        BytesReceived = bytesReceived;
        TotalBytes = totalBytes;
        FileName = fileName;
        Url = url;
        BytesPerSecond = bytesPerSecond;
        ElapsedTime = elapsedTime;
    }

    /// <summary>Gets the bytes received.</summary>
    public long BytesReceived { get; }

    /// <summary>Gets the total bytes.</summary>
    public long TotalBytes { get; }

    /// <summary>Gets the file name.</summary>
    public string FileName { get; }

    /// <summary>Gets the download URL.</summary>
    public string Url { get; }

    /// <summary>Gets the current download speed in bytes per second.</summary>
    public long BytesPerSecond { get; }

    /// <summary>Gets the elapsed download time.</summary>
    public TimeSpan ElapsedTime { get; }

    /// <summary>Gets the download percentage (0-100).</summary>
    public double Percentage => TotalBytes > 0 ? (double)BytesReceived / TotalBytes * 100 : 0;

    /// <summary>Gets the estimated time remaining.</summary>
    public TimeSpan? EstimatedTimeRemaining =>
        BytesPerSecond > 0 && TotalBytes > 0 && BytesReceived < TotalBytes
            ? TimeSpan.FromSeconds((TotalBytes - BytesReceived) / (double)BytesPerSecond)
            : null;

    /// <summary>Gets a formatted string representation of the download speed.</summary>
    public string FormattedSpeed => string.Format("{0}/s", FormatBytes(BytesPerSecond));

    /// <summary>Gets a formatted string representation of the progress.</summary>
    public string FormattedProgress => $"{FormatBytes(BytesReceived)} / {FormatBytes(TotalBytes)}";

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024.0;
        }

        // TODO: Replace with localized formatting when localization system is implemented
        // This uses InvariantCulture for consistent formatting across different system locales
        // Future: Consider using IStringLocalizer or similar localization service
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.0} {1}", len, sizes[order]);
    }
}