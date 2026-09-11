using System.Collections.Generic;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Metadata describing a game profile and its launch/engine configurations for sharing.
/// </summary>
public sealed class SharedProfileMetadata
{
    /// <summary>
    /// Gets the display name of the profile.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the description of the profile.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets the theme color hex string.
    /// </summary>
    public string? ThemeColor { get; init; }

    /// <summary>
    /// Gets the icon asset path or URI.
    /// </summary>
    public string? IconPath { get; init; }

    /// <summary>
    /// Gets the cover image asset path or URI.
    /// </summary>
    public string? CoverPath { get; init; }

    /// <summary>
    /// Gets the target game type (Generals or ZeroHour).
    /// </summary>
    public GameType GameType { get; init; }

    /// <summary>
    /// Gets the game client version string.
    /// </summary>
    public string GameVersion { get; init; } = string.Empty;

    /// <summary>
    /// Gets the game client manifest ID if using a specific provider client.
    /// </summary>
    public string? GameClientManifestId { get; init; }

    /// <summary>
    /// Gets a value indicating whether to use Steam launch.
    /// </summary>
    public bool? UseSteamLaunch { get; init; }

    /// <summary>
    /// Gets the preferred workspace strategy.
    /// </summary>
    public WorkspaceStrategy? WorkspaceStrategy { get; init; }

    /// <summary>
    /// Gets the command line arguments.
    /// </summary>
    public string CommandLineArguments { get; init; } = string.Empty;

    /// <summary>
    /// Gets the dictionary of game settings and client overrides.
    /// </summary>
    public Dictionary<string, object?> GameSettingsOverrides { get; init; } = [];
}
