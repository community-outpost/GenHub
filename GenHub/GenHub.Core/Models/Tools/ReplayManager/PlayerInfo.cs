namespace GenHub.Core.Models.Tools.ReplayManager;

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
