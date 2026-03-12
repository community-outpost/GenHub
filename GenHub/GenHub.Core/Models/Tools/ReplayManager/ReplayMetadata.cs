using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Tools.ReplayManager;

/// <summary>
/// Metadata extracted from a replay file.
/// </summary>
public sealed class ReplayMetadata
{
    /// <summary>
    /// Gets the map name.
    /// </summary>
    public string? MapName { get; init; }

    /// <summary>
    /// Gets the list of player information.
    /// </summary>
    public IReadOnlyList<PlayerInfo>? Players { get; init; }

    /// <summary>
    /// Gets the game duration.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Gets the date the game was played.
    /// </summary>
    public DateTime? GameDate { get; init; }

    /// <summary>
    /// Gets the game type (Generals or Zero Hour).
    /// </summary>
    public GameType? GameType { get; init; }

    /// <summary>
    /// Gets the file size in bytes.
    /// </summary>
    public long FileSizeBytes { get; init; }

    /// <summary>
    /// Gets a value indicating whether the replay was successfully parsed.
    /// </summary>
    public bool IsParsed { get; init; }

    /// <summary>
    /// Gets the original file path.
    /// </summary>
    public string? OriginalFilePath { get; init; }

    /// <summary>
    /// Gets the game mode (e.g., Skirmish, Online).
    /// </summary>
    public string? GameMode { get; init; }

    /// <summary>
    /// Gets the game version string.
    /// </summary>
    public string? GameVersion { get; init; }

    /// <summary>
    /// Gets the build date string.
    /// </summary>
    public string? BuildDate { get; init; }

    /// <summary>
    /// Gets the starting credits for the match.
    /// </summary>
    public int? StartingCredits { get; init; }

    /// <summary>
    /// Gets whether fog of war was enabled.
    /// </summary>
    public bool? FogOfWar { get; init; }

    /// <summary>
    /// Gets the game speed setting.
    /// </summary>
    public string? GameSpeed { get; init; }
}

/// <summary>
/// Information about a player in a replay.
/// </summary>
public sealed class PlayerInfo
{
    /// <summary>
    /// Gets the player name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the player type (Human or AI).
    /// </summary>
    public required PlayerType Type { get; init; }

    /// <summary>
    /// Gets the faction/side (e.g., USA, China, GLA).
    /// </summary>
    public string? Faction { get; init; }

    /// <summary>
    /// Gets the team number.
    /// </summary>
    public int? Team { get; init; }

    /// <summary>
    /// Gets the player color.
    /// </summary>
    public string? Color { get; init; }

    /// <summary>
    /// Gets the AI difficulty (for AI players).
    /// </summary>
    public string? AiDifficulty { get; init; }

    /// <summary>
    /// Gets the starting position/slot number.
    /// </summary>
    public int? StartPosition { get; init; }
}

/// <summary>
/// Player type enumeration.
/// </summary>
public enum PlayerType
{
    /// <summary>
    /// Human player.
    /// </summary>
    Human,

    /// <summary>
    /// AI/Computer player.
    /// </summary>
    Computer,

    /// <summary>
    /// Observer/spectator.
    /// </summary>
    Observer
}
