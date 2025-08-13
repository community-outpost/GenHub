namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Represents the phases of a game launch operation.
/// </summary>
public enum LaunchPhase
{
    /// <summary>Validating the game profile configuration.</summary>
    ValidatingProfile,

    /// <summary>Resolving content manifests and dependencies.</summary>
    ResolvingContent,

    /// <summary>Preparing the workspace for launch.</summary>
    PreparingWorkspace,

    /// <summary>Starting the game process.</summary>
    Starting,

    /// <summary>The game is currently running.</summary>
    Running,

    /// <summary>The launch process has completed.</summary>
    Completed,

    /// <summary>The launch process has failed.</summary>
    Failed,
}
