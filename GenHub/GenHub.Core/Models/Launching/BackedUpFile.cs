namespace GenHub.Core.Models.Launching;

/// <summary>
/// Represents a file that was backed up before being overwritten by GenHub.
/// </summary>
public class BackedUpFile
{
    /// <summary>
    /// Gets or sets the original path of the file (relative to game directory).
    /// </summary>
    public required string OriginalPath { get; set; }

    /// <summary>
    /// Gets or sets the backup path (relative to game directory, typically in .genhub-backup/).
    /// </summary>
    public required string BackupPath { get; set; }
}
