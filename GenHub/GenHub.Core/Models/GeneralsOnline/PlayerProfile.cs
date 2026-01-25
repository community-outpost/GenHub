using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Represents a player's profile information.
/// </summary>
public class PlayerProfile
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerProfile"/> class.
    /// </summary>
    /// <param name="playerName">The player's display name.</param>
    /// <param name="playerId">The unique player identifier.</param>
    /// <param name="dailyRank">Current daily ladder rank.</param>
    /// <param name="monthlyRank">Current monthly ladder rank.</param>
    /// <param name="annualRank">Current annual ladder rank.</param>
    /// <param name="dailyScore">Daily ladder score.</param>
    /// <param name="monthlyScore">Monthly ladder score.</param>
    /// <param name="annualScore">Annual ladder score.</param>
    /// <param name="totalWins">Total career wins.</param>
    /// <param name="totalLosses">Total career losses.</param>
    /// <param name="lastSeen">When the player was last online.</param>
    /// <param name="recentMatches">List of recent match IDs.</param>
    public PlayerProfile(
        string playerName,
        string? playerId,
        int? dailyRank,
        int? monthlyRank,
        int? annualRank,
        double dailyScore,
        double monthlyScore,
        double annualScore,
        int totalWins,
        int totalLosses,
        DateTime? lastSeen,
        IReadOnlyList<int> recentMatches)
    {
        PlayerName = playerName;
        PlayerId = playerId;
        DailyRank = dailyRank;
        MonthlyRank = monthlyRank;
        AnnualRank = annualRank;
        DailyScore = dailyScore;
        MonthlyScore = monthlyScore;
        AnnualScore = annualScore;
        TotalWins = totalWins;
        TotalLosses = totalLosses;
        LastSeen = lastSeen;
        RecentMatches = recentMatches;
    }

    /// <summary>
    /// Creates an empty player profile.
    /// </summary>
    /// <param name="playerName">The player name.</param>
    /// <returns>An empty player profile.</returns>
    public static PlayerProfile Empty(string playerName) => new(
        playerName,
        null,
        null,
        null,
        null,
        0,
        0,
        0,
        0,
        0,
        null,
        Array.Empty<int>());

    /// <summary>
    /// Gets the player's display name.
    /// </summary>
    public string PlayerName { get; }

    /// <summary>
    /// Gets the unique player identifier.
    /// </summary>
    public string? PlayerId { get; }

    /// <summary>
    /// Gets the current daily ladder rank.
    /// </summary>
    public int? DailyRank { get; }

    /// <summary>
    /// Gets the current monthly ladder rank.
    /// </summary>
    public int? MonthlyRank { get; }

    /// <summary>
    /// Gets the current annual ladder rank.
    /// </summary>
    public int? AnnualRank { get; }

    /// <summary>
    /// Gets the daily ladder score.
    /// </summary>
    public double DailyScore { get; }

    /// <summary>
    /// Gets the monthly ladder score.
    /// </summary>
    public double MonthlyScore { get; }

    /// <summary>
    /// Gets the annual ladder score.
    /// </summary>
    public double AnnualScore { get; }

    /// <summary>
    /// Gets the total career wins.
    /// </summary>
    public int TotalWins { get; }

    /// <summary>
    /// Gets the total career losses.
    /// </summary>
    public int TotalLosses { get; }

    /// <summary>
    /// Gets when the player was last online.
    /// </summary>
    public DateTime? LastSeen { get; }

    /// <summary>
    /// Gets the list of recent match IDs.
    /// </summary>
    public IReadOnlyList<int> RecentMatches { get; }

    /// <summary>
    /// Gets the total number of games played.
    /// </summary>
    public int TotalGames => TotalWins + TotalLosses;

    /// <summary>
    /// Gets the overall win rate percentage.
    /// </summary>
    public double WinRate => TotalGames > 0 ? (double)TotalWins / TotalGames * 100 : 0;

    /// <summary>
    /// Gets a value indicating whether the player is currently ranked in the daily ladder.
    /// </summary>
    public bool IsRankedDaily => DailyRank.HasValue && DailyRank.Value > 0;
}