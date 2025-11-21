using GenHub.Core.Models.Launching;

namespace GenHub.Core.Models.GameProfile;

/// <summary>Represents information about a launched game instance.</summary>
public class GameLaunchInfo
{
    /// <summary>Gets or sets the unique launch ID.</summary>
    required public string LaunchId { get; set; }

    /// <summary>Gets or sets the profile ID associated with this launch.</summary>
    required public string ProfileId { get; set; }

    /// <summary>Gets or sets the workspace ID used for this launch.</summary>
    required public string WorkspaceId { get; set; }

    /// <summary>Gets or sets the process information for the launched game.</summary>
    required public GameProcessInfo ProcessInfo { get; set; }

    /// <summary>Gets or sets the UTC time when the game was launched.</summary>
    public DateTime LaunchedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets the termination timestamp.</summary>
    public DateTime? TerminatedAt { get; set; }

    /// <summary>Gets a value indicating whether the game is still running.</summary>
    public bool IsRunning => TerminatedAt == null;
}