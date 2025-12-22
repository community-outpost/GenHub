namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Represents an active player currently online.
/// </summary>
public class ActivePlayer
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActivePlayer"/> class.
    /// </summary>
    /// <param name="playerName">The player's display name.</param>
    /// <param name="status">The player's current status.</param>
    /// <param name="lobbyName">The name of the lobby if in one.</param>
    /// <param name="clientVersion">The client version being used.</param>
    /// <param name="timeOnlineSeconds">Total time online in seconds.</param>
    public ActivePlayer(
        string playerName,
        PlayerStatus status,
        string? lobbyName,
        string clientVersion,
        int timeOnlineSeconds)
    {
        PlayerName = playerName;
        Status = status;
        LobbyName = lobbyName;
        ClientVersion = clientVersion;
        TimeOnlineSeconds = timeOnlineSeconds;
    }

    /// <summary>
    /// Gets the player's display name.
    /// </summary>
    public string PlayerName { get; }

    /// <summary>
    /// Gets the player's current status.
    /// </summary>
    public PlayerStatus Status { get; }

    /// <summary>
    /// Gets the name of the lobby if in one.
    /// </summary>
    public string? LobbyName { get; }

    /// <summary>
    /// Gets the client version being used.
    /// </summary>
    public string ClientVersion { get; }

    /// <summary>
    /// Gets the total time online in seconds.
    /// </summary>
    public int TimeOnlineSeconds { get; }

    /// <summary>
    /// Gets a value indicating whether the player is in a lobby.
    /// </summary>
    public bool IsInLobby => !string.IsNullOrEmpty(LobbyName);

    /// <summary>
    /// Gets the status text for display.
    /// </summary>
    public string StatusText => Status switch
    {
        PlayerStatus.InGame => "In Game",
        PlayerStatus.GameSetup => "Setup",
        PlayerStatus.InChatRoom => "Chat",
        _ => "Online"
    };

    /// <summary>
    /// Gets the formatted time online display.
    /// </summary>
    public string TimeOnlineDisplay
    {
        get
        {
            var hours = TimeOnlineSeconds / 3600;
            var minutes = (TimeOnlineSeconds % 3600) / 60;
            var seconds = TimeOnlineSeconds % 60;

            if (hours > 0)
            {
                return $"{hours}h {minutes}m";
            }

            return minutes > 0 ? $"{minutes}m {seconds}s" : $"{seconds}s";
        }
    }
}

/// <summary>
/// Represents a player's current status.
/// </summary>
public enum PlayerStatus
{
    /// <summary>
    /// Player is in the chat room or server list.
    /// </summary>
    InChatRoom,

    /// <summary>
    /// Player is in game setup.
    /// </summary>
    GameSetup,

    /// <summary>
    /// Player is in an active game.
    /// </summary>
    InGame,
}
