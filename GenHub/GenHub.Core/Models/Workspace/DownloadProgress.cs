namespace GenHub.Core.Models.Workspace;

/// <summary>
/// Progress information for file downloads.
/// </summary>
public class DownloadProgress
{
    /// <summary>
    /// Gets or sets the number of bytes received.
    /// </summary>
    public long BytesReceived { get; set; }

    /// <summary>
    /// Gets or sets the total number of bytes to receive.
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Gets the percentage of bytes received.
    /// </summary>
    public double Percentage => TotalBytes > 0 ? (double)BytesReceived / TotalBytes * 100 : 0;
}
