using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameVersions;

namespace GenHub.Core.Interfaces.GameProfiles;

/// <summary>
/// Defines the contract for a game profile.
/// </summary>
public interface IGameProfile
{
    /// <summary>
    /// Gets the unique identifier of the profile.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the name of the profile.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the game version associated with this profile.
    /// </summary>
    GameVersion GameVersion { get; }

    /// <summary>
    /// Gets the version string of the game.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Gets the path to the executable for this profile.
    /// </summary>
    string ExecutablePath { get; }

    /// <summary>
    /// Gets the list of enabled content IDs for this profile.
    /// </summary>
    List<string> EnabledContentIds { get; }

    /// <summary>
    /// Gets the preferred workspace strategy for this profile.
    /// </summary>
    WorkspaceStrategy PreferredStrategy { get; }
}
