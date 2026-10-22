namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Lifecycle state of a Generals Online lobby.
/// Mirrors the backend ELobbyState values.
/// </summary>
public enum GeneralsOnlineLobbyState
{
    /// <summary>
    /// Unknown lobby state.
    /// </summary>
    Unknown = -1,

    /// <summary>
    /// Lobby is forming and waiting for players.
    /// </summary>
    GameSetup = 0,

    /// <summary>
    /// Match is currently in progress.
    /// </summary>
    InGame = 1,

    /// <summary>
    /// Match has completed.
    /// </summary>
    Complete = 2,
}
