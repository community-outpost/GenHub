namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Represents the time period for leaderboard rankings.
/// </summary>
public enum LeaderboardPeriod
{
    /// <summary>
    /// Daily leaderboard (resets every day).
    /// </summary>
    Daily,

    /// <summary>
    /// Monthly leaderboard (resets every month).
    /// </summary>
    Monthly,

    /// <summary>
    /// Annual leaderboard (resets every year).
    /// </summary>
    Annual,
}