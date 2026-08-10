using GenHub.Core.Models.Launching;

namespace GenHub.Core.Models.GameProfile;

/// <summary>Represents information about a launched game instance.</summary>
public class GameLaunchInfo
{
    /// <summary>Gets or sets the unique launch ID.</summary>
    public required string LaunchId { get; set; }

    /// <summary>Gets or sets the profile ID associated with this launch.</summary>
    public required string ProfileId { get; set; }

    /// <summary>Gets or sets the workspace ID used for this launch.</summary>
    public required string WorkspaceId { get; set; }

    /// <summary>Gets or sets the process information for the launched game.</summary>
    public required GameProcessInfo ProcessInfo { get; set; }

    /// <summary>Gets or sets the UTC time when the game was launched.</summary>
    public DateTime LaunchedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets the termination timestamp.</summary>
    public DateTime? TerminatedAt { get; set; }

    /// <summary>Gets or sets the process exit code, when it is known.</summary>
    public int? ExitCode { get; set; }

    /// <summary>
    /// Gets or sets why this launch is considered failed, when the process exited
    /// abnormally after the launch had already been reported as started.
    /// </summary>
    /// <remarks>
    /// The late-failure channel: an initialisation abort slow enough to outlive the
    /// post-spawn detection window cannot fail the start operation retroactively, so the
    /// failure is recorded here instead. A clean exit leaves this null — quitting the
    /// game is not a failed launch.
    /// </remarks>
    public string? FailureReason { get; set; }

    /// <summary>Gets a value indicating whether this launch ended in failure.</summary>
    public bool HasFailed => FailureReason != null;

    /// <summary>Gets a value indicating whether the game is still running.</summary>
    public bool IsRunning => TerminatedAt == null;
}