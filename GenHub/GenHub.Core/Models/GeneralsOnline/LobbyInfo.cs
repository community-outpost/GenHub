using System.Collections.Generic;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Represents information about an active lobby.
/// </summary>
public class LobbyInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LobbyInfo"/> class.
    /// </summary>
    /// <param name="lobbyId">The unique lobby identifier.</param>
    /// <param name="lobbyName">The display name of the lobby.</param>
    /// <param name="hostName">The name of the lobby host.</param>
    /// <param name="mapName">The selected map name.</param>
    /// <param name="region">The server region.</param>
    /// <param name="currentPlayers">List of current player names.</param>
    /// <param name="maxPlayers">Maximum number of players allowed.</param>
    /// <param name="isRanked">Whether this is a ranked match.</param>
    /// <param name="isPasswordProtected">Whether the lobby requires a password.</param>
    public LobbyInfo(
        string lobbyId,
        string lobbyName,
        string hostName,
        string mapName,
        string region,
        IReadOnlyList<string> currentPlayers,
        int maxPlayers,
        bool isRanked,
        bool isPasswordProtected)
    {
        LobbyId = lobbyId;
        LobbyName = lobbyName;
        HostName = hostName;
        MapName = mapName;
        Region = region;
        CurrentPlayers = currentPlayers;
        MaxPlayers = maxPlayers;
        IsRanked = isRanked;
        IsPasswordProtected = isPasswordProtected;
    }

    /// <summary>
    /// Gets the unique lobby identifier.
    /// </summary>
    public string LobbyId { get; }

    /// <summary>
    /// Gets the display name of the lobby.
    /// </summary>
    public string LobbyName { get; }

    /// <summary>
    /// Gets the name of the lobby host.
    /// </summary>
    public string HostName { get; }

    /// <summary>
    /// Gets the selected map name.
    /// </summary>
    public string MapName { get; }

    /// <summary>
    /// Gets the server region.
    /// </summary>
    public string Region { get; }

    /// <summary>
    /// Gets the list of current player names.
    /// </summary>
    public IReadOnlyList<string> CurrentPlayers { get; }

    /// <summary>
    /// Gets the maximum number of players allowed.
    /// </summary>
    public int MaxPlayers { get; }

    /// <summary>
    /// Gets a value indicating whether this is a ranked match.
    /// </summary>
    public bool IsRanked { get; }

    /// <summary>
    /// Gets a value indicating whether the lobby requires a password.
    /// </summary>
    public bool IsPasswordProtected { get; }

    /// <summary>
    /// Gets the current player count.
    /// </summary>
    public int PlayerCount => CurrentPlayers.Count;

    /// <summary>
    /// Gets a value indicating whether the lobby has available slots.
    /// </summary>
    public bool HasAvailableSlots => PlayerCount < MaxPlayers;

    /// <summary>
    /// Gets the formatted player count display.
    /// </summary>
    public string PlayerCountDisplay => $"{PlayerCount}/{MaxPlayers}";

    /// <summary>
    /// Gets the status indicator.
    /// </summary>
    public string StatusIndicator => IsPasswordProtected ? "🔒" : (IsRanked ? "🏆" : "🎮");
}