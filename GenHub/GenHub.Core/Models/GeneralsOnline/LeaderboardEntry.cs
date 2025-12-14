namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Represents a player entry in the leaderboard.
/// </summary>
public class LeaderboardEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LeaderboardEntry"/> class.
    /// </summary>
    /// <param name="rank">The player's rank position.</param>
    /// <param name="playerName">The player's display name.</param>
    /// <param name="score">The player's rating score.</param>
    /// <param name="wins">Number of wins.</param>
    /// <param name="losses">Number of losses.</param>
    public LeaderboardEntry(int rank, string playerName, double score, int wins, int losses)
    {
        Rank = rank;
        PlayerName = playerName;
        Score = score;
        Wins = wins;
        Losses = losses;
    }

    /// <summary>
    /// Gets the player's rank position.
    /// </summary>
    public int Rank { get; }

    /// <summary>
    /// Gets the player's display name.
    /// </summary>
    public string PlayerName { get; }

    /// <summary>
    /// Gets the player's rating score.
    /// </summary>
    public double Score { get; }

    /// <summary>
    /// Gets the number of wins.
    /// </summary>
    public int Wins { get; }

    /// <summary>
    /// Gets the number of losses.
    /// </summary>
    public int Losses { get; }

    /// <summary>
    /// Gets the win rate percentage.
    /// </summary>
    public double WinRate => TotalGames > 0 ? (double)Wins / TotalGames * 100 : 0;

    /// <summary>
    /// Gets the total number of games played.
    /// </summary>
    public int TotalGames => Wins + Losses;
}