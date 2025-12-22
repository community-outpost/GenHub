using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Represents information about a match.
/// </summary>
public class MatchInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MatchInfo"/> class.
    /// </summary>
    /// <param name="matchId">The unique match identifier.</param>
    /// <param name="lobbyName">The name of the lobby/match.</param>
    /// <param name="mapName">The name of the map played.</param>
    /// <param name="region">The server region (EU, NA, AS, AF).</param>
    /// <param name="players">List of player names in the match.</param>
    /// <param name="matchType">Type of match (Quickmatch, Custom, etc.).</param>
    /// <param name="startTime">When the match started.</param>
    /// <param name="duration">How long the match lasted.</param>
    /// <param name="mapThumbnailUrl">URL to the map thumbnail image.</param>
    public MatchInfo(
        int matchId,
        string lobbyName,
        string mapName,
        string region,
        IReadOnlyList<string> players,
        string matchType,
        DateTime? startTime,
        TimeSpan? duration,
        string? mapThumbnailUrl)
    {
        MatchId = matchId;
        LobbyName = lobbyName;
        MapName = mapName;
        Region = region;
        Players = players;
        MatchType = matchType;
        StartTime = startTime;
        Duration = duration;
        MapThumbnailUrl = mapThumbnailUrl;
    }

    /// <summary>
    /// Gets the unique match identifier.
    /// </summary>
    public int MatchId { get; }

    /// <summary>
    /// Gets the name of the lobby/match.
    /// </summary>
    public string LobbyName { get; }

    /// <summary>
    /// Gets the name of the map played.
    /// </summary>
    public string MapName { get; }

    /// <summary>
    /// Gets the server region (EU, NA, AS, AF).
    /// </summary>
    public string Region { get; }

    /// <summary>
    /// Gets the list of player names in the match.
    /// </summary>
    public IReadOnlyList<string> Players { get; }

    /// <summary>
    /// Gets the type of match (Quickmatch, Custom, etc.).
    /// </summary>
    public string MatchType { get; }

    /// <summary>
    /// Gets when the match started.
    /// </summary>
    public DateTime? StartTime { get; }

    /// <summary>
    /// Gets how long the match lasted.
    /// </summary>
    public TimeSpan? Duration { get; }

    /// <summary>
    /// Gets the URL to the map thumbnail image.
    /// </summary>
    public string? MapThumbnailUrl { get; }

    /// <summary>
    /// Gets the player count for this match.
    /// </summary>
    public int PlayerCount => Players.Count;
}