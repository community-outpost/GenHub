namespace GenHub.Core.Models.Content;

/// <summary>
/// Represents progress during the content acquisition phase where packages are downloaded,
/// extracted, and scanned to transform manifests from package-level to file-level operations.
/// </summary>
public class ContentAcquisitionProgress
{
    /// <summary>
    /// Gets or sets the current phase of acquisition.
    /// </summary>
    public ContentAcquisitionPhase Phase { get; set; } = ContentAcquisitionPhase.Downloading;

    /// <summary>
    /// Gets or sets the overall progress percentage (0-100) for the current phase.
    /// </summary>
    public double ProgressPercentage { get; set; }

    /// <summary>
    /// Gets or sets a description of the current operation being performed.
    /// </summary>
    public string CurrentOperation { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of bytes processed (downloaded or extracted).
    /// </summary>
    public long BytesProcessed { get; set; }

    /// <summary>
    /// Gets or sets the total number of bytes to process.
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Gets or sets the number of files processed during scanning/transformation.
    /// </summary>
    public int FilesProcessed { get; set; }

    /// <summary>
    /// Gets or sets the total number of files to process.
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// Gets or sets the current file being processed (for detailed progress tracking).
    /// </summary>
    public string CurrentFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the estimated time remaining for the current phase.
    /// </summary>
    public TimeSpan EstimatedTimeRemaining { get; set; }
}