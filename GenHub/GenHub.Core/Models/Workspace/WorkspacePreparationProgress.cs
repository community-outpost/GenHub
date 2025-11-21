using GenHub.Core.Models.Common;

namespace GenHub.Core.Models.Workspace;

/// <summary>Progress information for workspace preparation operations.</summary>
public class WorkspacePreparationProgress
{
    /// <summary>Gets or sets the current operation being performed.</summary>
    public string CurrentOperation { get; set; } = string.Empty;

    /// <summary>Gets or sets the current file being processed.</summary>
    public string CurrentFile { get; set; } = string.Empty;

    /// <summary>Gets or sets the number of files processed.</summary>
    public int FilesProcessed { get; set; }

    /// <summary>Gets or sets the total number of files to process.</summary>
    public int TotalFiles { get; set; }

    /// <summary>Gets or sets the download progress for the current file (if applicable).</summary>
    public DownloadProgress? DownloadProgress { get; set; }

    /// <summary>Gets the overall percentage of completion.</summary>
    public double OverallPercentage => TotalFiles > 0 ? (double)FilesProcessed / TotalFiles * 100 : 0;

    /// <summary>Gets the overall percentage of completion as an integer.</summary>
    public int PercentComplete => (int)Math.Round(OverallPercentage);
}