namespace GenHub.Core.Models.Enums;

/// <summary>
/// Represents the status of a tournament.
/// </summary>
public enum TournamentStatus
{
    /// <summary>
    /// Tournament is accepting signups.
    /// </summary>
    SignupsOpen,

    /// <summary>
    /// Tournament is upcoming but not yet started.
    /// </summary>
    Upcoming,

    /// <summary>
    /// Tournament is currently active.
    /// </summary>
    Active,

    /// <summary>
    /// Tournament has finished.
    /// </summary>
    Finished,
}
