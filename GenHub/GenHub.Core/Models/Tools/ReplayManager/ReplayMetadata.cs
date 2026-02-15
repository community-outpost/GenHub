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
    /// Gets the list of players.
    /// </summary>
    public IReadOnlyList<string>? Players { get; init; }

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
}