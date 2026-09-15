namespace GenHub.Core.Models.Tools.ReplayManager;

/// <summary>
/// Placeholder for future replay parsing feature.
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
}