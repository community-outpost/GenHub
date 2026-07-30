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

    /// <summary>
    /// Gets or sets the launch receipt drift detected before this launch, one warning per
    /// drifted field, so a UI can show it. Informational only — drift never blocks a
    /// launch — and empty when no receipt existed or nothing drifted.
    /// </summary>
    public List<string> ReceiptDriftWarnings { get; set; } = [];

    /// <summary>Gets a value indicating whether the game is still running.</summary>
    public bool IsRunning => TerminatedAt == null;
}