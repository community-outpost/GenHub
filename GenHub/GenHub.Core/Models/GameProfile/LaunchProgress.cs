using GenHub.Core.Models.Workspace;

namespace GenHub.Core.Models.GameProfile;

/// <summary>Represents the progress of a game launch operation.</summary>
public class LaunchProgress
{
    private int _percentComplete;

    /// <summary>Gets or sets the current launch phase.</summary>
    public LaunchPhase Phase { get; set; }

    /// <summary>Gets or sets the percentage completion (0-100).</summary>
    public int PercentComplete
    {
        get => _percentComplete;
        set
        {
            if (value < 0 || value > 100)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Percentage must be between 0 and 100.");
            _percentComplete = value;
        }
    }

    /// <summary>Gets or sets the workspace cleanup confirmation data when awaiting user decision.</summary>
    public WorkspaceCleanupConfirmation? CleanupConfirmation { get; set; }

    /// <summary>Gets or sets a value indicating whether workspace files are actively being initialized or materialized.</summary>
    public bool IsInitializingWorkspace { get; set; }

    /// <summary>Gets or sets the total number of files to process during workspace initialization.</summary>
    public int? TotalFiles { get; set; }

    /// <summary>Gets or sets the number of files processed so far during workspace initialization.</summary>
    public int? FilesProcessed { get; set; }

    /// <summary>Gets or sets the current file being processed during workspace initialization.</summary>
    public string? CurrentFile { get; set; }
}
