namespace GenHub.Core.Interfaces.GameProfiles;

/// <summary>
/// Represents a game profile for the launcher.
/// </summary>
public interface IGameProfile
{
    /// <summary>
    /// Gets the version of the game profile.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Gets the executable path for the game profile.
    /// </summary>
    string ExecutablePath { get; }
}
