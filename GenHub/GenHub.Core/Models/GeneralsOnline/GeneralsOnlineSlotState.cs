namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// State of a single slot in a Generals Online lobby.
/// Mirrors the backend EPlayerType values.
/// </summary>
public enum GeneralsOnlineSlotState
{
    /// <summary>
    /// Slot is open and joinable.
    /// </summary>
    SlotOpen = 0,

    /// <summary>
    /// Slot is closed by the host.
    /// </summary>
    SlotClosed = 1,

    /// <summary>
    /// Slot holds an easy AI opponent.
    /// </summary>
    SlotEasyAi = 2,

    /// <summary>
    /// Slot holds a medium AI opponent.
    /// </summary>
    SlotMediumAi = 3,

    /// <summary>
    /// Slot holds a brutal AI opponent.
    /// </summary>
    SlotBrutalAi = 4,

    /// <summary>
    /// Slot holds a human player.
    /// </summary>
    SlotPlayer = 5,
}
