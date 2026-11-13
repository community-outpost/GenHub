using System.Linq;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Career statistics for one Generals Online player. Only the headline
/// fields are mapped; the backend carries many more per-general counters.
/// </summary>
public sealed class GeneralsOnlinePlayerStats
{
    /// <summary>
    /// Gets or sets the owning user id.
    /// </summary>
    [JsonPropertyName("userID")]
    public long UserId { get; set; } = -1;

    /// <summary>
    /// Gets or sets the current Elo rating.
    /// </summary>
    [JsonPropertyName("EloRating")]
    public int EloRating { get; set; }

    /// <summary>
    /// Gets or sets the number of rated Elo matches played.
    /// </summary>
    [JsonPropertyName("EloMatches")]
    public int EloMatches { get; set; }

    /// <summary>
    /// Gets or sets the monthly Elo rating.
    /// </summary>
    [JsonPropertyName("MonthlyEloRating")]
    public int MonthlyEloRating { get; set; }

    /// <summary>
    /// Gets or sets the wins per general.
    /// </summary>
    [JsonPropertyName("wins")]
    public int[] Wins { get; set; } = []; // skipcq: CS-W1096

    /// <summary>
    /// Gets or sets the losses per general.
    /// </summary>
    [JsonPropertyName("losses")]
    public int[] Losses { get; set; } = []; // skipcq: CS-W1096

    /// <summary>
    /// Gets or sets the games played per general.
    /// </summary>
    [JsonPropertyName("games")]
    public int[] Games { get; set; } = []; // skipcq: CS-W1096

    /// <summary>
    /// Gets the total wins across all generals.
    /// </summary>
    [JsonIgnore]
    public int TotalWins => Wins.Sum();

    /// <summary>
    /// Gets the total losses across all generals.
    /// </summary>
    [JsonIgnore]
    public int TotalLosses => Losses.Sum();

    /// <summary>
    /// Gets the total games played across all generals.
    /// </summary>
    [JsonIgnore]
    public int TotalGames => Games.Sum();
}
