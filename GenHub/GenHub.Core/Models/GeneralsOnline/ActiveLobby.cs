// <copyright file="ActiveLobby.cs" company="GenHub">
// Copyright (c) GenHub. All rights reserved.
// </copyright>

using System.Collections.Generic;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Represents an active game lobby with detailed match settings.
/// </summary>
public class ActiveLobby
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActiveLobby"/> class.
    /// </summary>
    /// <param name="lobbyName">The display name of the lobby.</param>
    /// <param name="mapName">The map being played.</param>
    /// <param name="mapThumbnailUrl">URL to the map thumbnail image.</param>
    /// <param name="mapSlots">Maximum player slots for the map.</param>
    /// <param name="region">The server region code.</param>
    /// <param name="slots">List of player slots in the lobby.</param>
    /// <param name="status">Current match status.</param>
    /// <param name="settings">Lobby game settings.</param>
    public ActiveLobby(
        string lobbyName,
        string mapName,
        string mapThumbnailUrl,
        int mapSlots,
        string region,
        IReadOnlyList<LobbySlot> slots,
        LobbyStatus status,
        LobbySettings settings)
    {
        LobbyName = lobbyName;
        MapName = mapName;
        MapThumbnailUrl = mapThumbnailUrl;
        MapSlots = mapSlots;
        Region = region;
        Slots = slots;
        Status = status;
        Settings = settings;
    }

    /// <summary>
    /// Gets the display name of the lobby (e.g., "[AS] Generals Online Lobby").
    /// </summary>
    public string LobbyName { get; }

    /// <summary>
    /// Gets the map being played (e.g., "Defcon 6").
    /// </summary>
    public string MapName { get; }

    /// <summary>
    /// Gets the URL to the map thumbnail image.
    /// </summary>
    public string MapThumbnailUrl { get; }

    /// <summary>
    /// Gets the maximum player slots for the map.
    /// </summary>
    public int MapSlots { get; }

    /// <summary>
    /// Gets the server region code (e.g., "AS", "EU", "AF").
    /// </summary>
    public string Region { get; }

    /// <summary>
    /// Gets the list of player slots in the lobby.
    /// </summary>
    public IReadOnlyList<LobbySlot> Slots { get; }

    /// <summary>
    /// Gets the current match status.
    /// </summary>
    public LobbyStatus Status { get; }

    /// <summary>
    /// Gets the lobby game settings.
    /// </summary>
    public LobbySettings Settings { get; }

    /// <summary>
    /// Gets the current player count (occupied slots).
    /// </summary>
    public int PlayerCount
    {
        get
        {
            int count = 0;
            foreach (var slot in Slots)
            {
                if (slot.SlotState == SlotState.Occupied)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// Gets the number of open slots available.
    /// </summary>
    public int OpenSlots
    {
        get
        {
            int count = 0;
            foreach (var slot in Slots)
            {
                if (slot.SlotState == SlotState.Open)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// Gets the formatted player count display (e.g., "2/6").
    /// </summary>
    public string PlayerCountDisplay => $"{PlayerCount}/{MapSlots}";

    /// <summary>
    /// Gets the full map thumbnail URL.
    /// </summary>
    public string FullMapThumbnailUrl => string.IsNullOrEmpty(MapThumbnailUrl)
        ? "https://www.playgenerals.online/assets/images/mapthumbnails/custom.png"
        : $"https://www.playgenerals.online/{MapThumbnailUrl}";

    /// <summary>
    /// Gets a value indicating whether the lobby can be joined.
    /// </summary>
    public bool CanJoin => Status == LobbyStatus.Waiting && OpenSlots > 0 && !Settings.IsPassworded;

    /// <summary>
    /// Gets a value indicating whether the match is in progress.
    /// </summary>
    public bool IsInProgress => Status == LobbyStatus.InProgress;

    /// <summary>
    /// Gets a value indicating whether the lobby is waiting for players.
    /// </summary>
    public bool IsWaiting => Status == LobbyStatus.Waiting;
}

/// <summary>
/// Represents a player slot in a lobby.
/// </summary>
public class LobbySlot
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LobbySlot"/> class.
    /// </summary>
    /// <param name="playerName">The player name, or null if empty/closed.</param>
    /// <param name="teamColor">The team color hex code.</param>
    /// <param name="factionIcon">The faction icon URL, or null if unknown.</param>
    /// <param name="slotState">The state of the slot.</param>
    public LobbySlot(string? playerName, string teamColor, string? factionIcon, SlotState slotState)
    {
        PlayerName = playerName;
        TeamColor = teamColor;
        FactionIcon = factionIcon;
        SlotState = slotState;
    }

    /// <summary>
    /// Gets the player name, or null if slot is empty/closed.
    /// </summary>
    public string? PlayerName { get; }

    /// <summary>
    /// Gets the team color hex code.
    /// </summary>
    public string TeamColor { get; }

    /// <summary>
    /// Gets the faction icon URL, or null if unknown.
    /// </summary>
    public string? FactionIcon { get; }

    /// <summary>
    /// Gets the state of the slot.
    /// </summary>
    public SlotState SlotState { get; }

    /// <summary>
    /// Gets the display name for the slot.
    /// </summary>
    public string DisplayName => SlotState switch
    {
        SlotState.Occupied => PlayerName ?? "Unknown",
        SlotState.Open => "Open",
        SlotState.Closed => "Closed",
        _ => "Unknown",
    };

    /// <summary>
    /// Gets the foreground color for the slot display.
    /// </summary>
    public string SlotForeground => SlotState switch
    {
        SlotState.Occupied => "#FFFFFF",
        SlotState.Open => "#4ade80",
        SlotState.Closed => "#888888",
        _ => "#888888",
    };
}

/// <summary>
/// Lobby game settings.
/// </summary>
public class LobbySettings
{
    /// <summary>
    /// Gets default/empty settings.
    /// </summary>
    public static LobbySettings Default { get; } = new(
        isCustomMap: false,
        isOriginalArmies: false,
        startingCash: "$10000",
        limitSuperweapons: false,
        trackStats: false,
        allowObservers: false,
        isPassworded: false,
        cameraHeight: "Default");

    /// <summary>
    /// Initializes a new instance of the <see cref="LobbySettings"/> class.
    /// </summary>
    /// <param name="isCustomMap">Whether a custom map is being used.</param>
    /// <param name="isOriginalArmies">Whether original armies are selected.</param>
    /// <param name="startingCash">Starting cash amount.</param>
    /// <param name="limitSuperweapons">Whether superweapons are limited.</param>
    /// <param name="trackStats">Whether stats are being tracked.</param>
    /// <param name="allowObservers">Whether observers are allowed.</param>
    /// <param name="isPassworded">Whether the lobby requires a password.</param>
    /// <param name="cameraHeight">Camera height setting.</param>
    public LobbySettings(
        bool isCustomMap,
        bool isOriginalArmies,
        string startingCash,
        bool limitSuperweapons,
        bool trackStats,
        bool allowObservers,
        bool isPassworded,
        string cameraHeight)
    {
        IsCustomMap = isCustomMap;
        IsOriginalArmies = isOriginalArmies;
        StartingCash = startingCash;
        LimitSuperweapons = limitSuperweapons;
        TrackStats = trackStats;
        AllowObservers = allowObservers;
        IsPassworded = isPassworded;
        CameraHeight = cameraHeight;
    }

    /// <summary>
    /// Gets a value indicating whether a custom map is being used.
    /// </summary>
    public bool IsCustomMap { get; }

    /// <summary>
    /// Gets a value indicating whether original armies (non-ZH) are selected.
    /// </summary>
    public bool IsOriginalArmies { get; }

    /// <summary>
    /// Gets the starting cash amount.
    /// </summary>
    public string StartingCash { get; }

    /// <summary>
    /// Gets a value indicating whether superweapons are limited.
    /// </summary>
    public bool LimitSuperweapons { get; }

    /// <summary>
    /// Gets a value indicating whether stats are being tracked (ranked).
    /// </summary>
    public bool TrackStats { get; }

    /// <summary>
    /// Gets a value indicating whether observers are allowed.
    /// </summary>
    public bool AllowObservers { get; }

    /// <summary>
    /// Gets a value indicating whether the lobby requires a password.
    /// </summary>
    public bool IsPassworded { get; }

    /// <summary>
    /// Gets the camera height setting.
    /// </summary>
    public string CameraHeight { get; }
}

/// <summary>
/// Possible states for a lobby slot.
/// </summary>
public enum SlotState
{
    /// <summary>
    /// Slot is occupied by a player.
    /// </summary>
    Occupied,

    /// <summary>
    /// Slot is open for players to join.
    /// </summary>
    Open,

    /// <summary>
    /// Slot is closed/disabled.
    /// </summary>
    Closed,
}

/// <summary>
/// Possible statuses for a lobby.
/// </summary>
public enum LobbyStatus
{
    /// <summary>
    /// Match is in progress.
    /// </summary>
    InProgress,

    /// <summary>
    /// Waiting for players / game setup.
    /// </summary>
    Waiting,

    /// <summary>
    /// Unknown status.
    /// </summary>
    Unknown,
}
