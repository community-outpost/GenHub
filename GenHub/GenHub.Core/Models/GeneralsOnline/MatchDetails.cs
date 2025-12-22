using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Represents detailed information about a specific match.
/// </summary>
public class MatchDetails
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MatchDetails"/> class.
    /// </summary>
    public MatchDetails(
        int matchId,
        string lobbyName,
        string mapName,
        DateTime? timeStarted,
        DateTime? timeEnded,
        bool isOfficial,
        bool isOriginalBombs,
        bool hasStartingCash,
        string? limitSuperweapons,
        bool allowObservers,
        int maxCameraHeight,
        Dictionary<string, PlayerMatchDetails> participants)
    {
        MatchId = matchId;
        LobbyName = lobbyName;
        MapName = mapName;
        TimeStarted = timeStarted;
        TimeEnded = timeEnded;
        IsOfficial = isOfficial;
        IsOriginalBombs = isOriginalBombs;
        HasStartingCash = hasStartingCash;
        LimitSuperweapons = limitSuperweapons;
        AllowObservers = allowObservers;
        MaxCameraHeight = maxCameraHeight;
        Participants = participants;
    }

    /// <summary>
    /// Gets the match ID.
    /// </summary>
    public int MatchId { get; }

    /// <summary>
    /// Gets the lobby name.
    /// </summary>
    public string LobbyName { get; }

    /// <summary>
    /// Gets the map name.
    /// </summary>
    public string MapName { get; }

    /// <summary>
    /// Gets the time the match started.
    /// </summary>
    public DateTime? TimeStarted { get; }

    /// <summary>
    /// Gets the time the match ended.
    /// </summary>
    public DateTime? TimeEnded { get; }

    /// <summary>
    /// Gets a value indicating whether this is an official match.
    /// </summary>
    public bool IsOfficial { get; }

    /// <summary>
    /// Gets a value indicating whether original bombs only are enabled.
    /// </summary>
    public bool IsOriginalBombs { get; }

    /// <summary>
    /// Gets a value indicating whether starting cash is enabled.
    /// </summary>
    public bool HasStartingCash { get; }

    /// <summary>
    /// Gets the superweapon limit setting.
    /// </summary>
    public string? LimitSuperweapons { get; }

    /// <summary>
    /// Gets a value indicating whether observers are allowed.
    /// </summary>
    public bool AllowObservers { get; }

    /// <summary>
    /// Gets the maximum camera height.
    /// </summary>
    public int MaxCameraHeight { get; }

    /// <summary>
    /// Gets the participant details keyed by player name.
    /// </summary>
    public Dictionary<string, PlayerMatchDetails> Participants { get; }

    /// <summary>
    /// Gets the match duration.
    /// </summary>
    public TimeSpan? Duration => TimeEnded.HasValue && TimeStarted.HasValue
        ? TimeEnded.Value - TimeStarted.Value
        : null;
}

/// <summary>
/// Represents detailed information about a player in a match.
/// </summary>
public class PlayerMatchDetails
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerMatchDetails"/> class.
    /// </summary>
    public PlayerMatchDetails(
        string displayName,
        string team,
        string color,
        string faction,
        int buildingsBuilt,
        int buildingsDestroyed,
        int unitsBuilt,
        int unitsDestroyed,
        int unitsLost,
        int totalMoneyEarned,
        string outcome)
    {
        DisplayName = displayName;
        Team = team;
        Color = color;
        Faction = faction;
        BuildingsBuilt = buildingsBuilt;
        BuildingsDestroyed = buildingsDestroyed;
        UnitsBuilt = unitsBuilt;
        UnitsDestroyed = unitsDestroyed;
        UnitsLost = unitsLost;
        TotalMoneyEarned = totalMoneyEarned;
        Outcome = outcome;
    }

    /// <summary>
    /// Gets the player display name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the team name.
    /// </summary>
    public string Team { get; }

    /// <summary>
    /// Gets the player color.
    /// </summary>
    public string Color { get; }

    /// <summary>
    /// Gets the faction.
    /// </summary>
    public string Faction { get; }

    /// <summary>
    /// Gets the number of buildings built.
    /// </summary>
    public int BuildingsBuilt { get; }

    /// <summary>
    /// Gets the number of buildings destroyed.
    /// </summary>
    public int BuildingsDestroyed { get; }

    /// <summary>
    /// Gets the number of units built.
    /// </summary>
    public int UnitsBuilt { get; }

    /// <summary>
    /// Gets the number of units destroyed.
    /// </summary>
    public int UnitsDestroyed { get; }

    /// <summary>
    /// Gets the number of units lost.
    /// </summary>
    public int UnitsLost { get; }

    /// <summary>
    /// Gets the total money earned.
    /// </summary>
    public int TotalMoneyEarned { get; }

    /// <summary>
    /// Gets the match outcome (Won/Lost).
    /// </summary>
    public string Outcome { get; }
}
