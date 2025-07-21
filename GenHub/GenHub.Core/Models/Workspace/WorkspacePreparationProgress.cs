using System;

namespace GenHub.Core.Models.Workspace;

/// <summary>
/// Comprehensive progress information for workspace preparation.
/// </summary>
public class WorkspacePreparationProgress
{
    /// <summary>
    /// Gets or sets the number of files processed so far.
    /// </summary>
    public int FilesProcessed { get; set; }

    /// <summary>
    /// Gets or sets the total number of files to process.
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// Gets or sets the number of bytes processed so far.
    /// </summary>
    public long BytesProcessed { get; set; }

    /// <summary>
    /// Gets or sets the total number of bytes to process.
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Gets or sets the description of the current operation.
    /// </summary>
    public string CurrentOperation { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the path of the current file being processed.
    /// </summary>
    public string CurrentFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the estimated time remaining for the operation.
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; set; }

    /// <summary>
    /// Gets the percentage of files processed.
    /// </summary>
    public double FilePercentage => TotalFiles > 0 ? (double)FilesProcessed / TotalFiles * 100 : 0;

    /// <summary>
    /// Gets the percentage of bytes processed.
    /// </summary>
    public double BytePercentage => TotalBytes > 0 ? (double)BytesProcessed / TotalBytes * 100 : 0;
}
