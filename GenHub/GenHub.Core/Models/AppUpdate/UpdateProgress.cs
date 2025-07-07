namespace GenHub.Core.Models.AppUpdate;

/// <summary>
/// Represents the progress of an update download and installation operation.
/// </summary>
public class UpdateProgress
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateProgress"/> class.
    /// </summary>
    public UpdateProgress()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateProgress"/> class.
    /// </summary>
    /// <param name="percentComplete">The percentage completed (0-100).</param>
    /// <param name="message">The status message.</param>
    public UpdateProgress(int percentComplete, string message)
    {
        PercentComplete = percentComplete;
        Message = message;
        Status = message;
    }

    /// <summary>
    /// Gets or sets the current status message.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the progress message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the percentage completed as an integer (0-100).
    /// </summary>
    public int PercentComplete { get; set; }

    /// <summary>
    /// Gets or sets the number of bytes downloaded.
    /// </summary>
    public long BytesDownloaded { get; set; }

    /// <summary>
    /// Gets or sets the total number of bytes to download.
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Gets or sets the current download speed in bytes per second.
    /// </summary>
    public long BytesPerSecond { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the operation is completed.
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the operation failed.
    /// </summary>
    public bool HasError { get; set; }

    /// <summary>
    /// Gets or sets the error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
