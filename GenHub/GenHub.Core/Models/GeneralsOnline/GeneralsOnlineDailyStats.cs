using System.Linq;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Today's per-faction match and win totals from the GlobalStats endpoint.
/// Array positions follow the backend army order, which has no published
/// faction mapping, so only aggregates are surfaced.
/// </summary>
public sealed class GeneralsOnlineDailyStats
{
    /// <summary>
    /// Gets or sets the matches played per faction side today.
    /// </summary>
    [JsonPropertyName("matches")]
    public int[] Matches { get; set; } = []; // skipcq: CS-W1096

    /// <summary>
    /// Gets or sets the wins recorded per faction side today.
    /// </summary>
    [JsonPropertyName("wins")]
    public int[] Wins { get; set; } = []; // skipcq: CS-W1096

    /// <summary>
    /// Gets the total matches played today across all faction sides.
    /// </summary>
    [JsonIgnore]
    public int TotalMatches => Matches.Sum();

    /// <summary>
    /// Gets the total wins recorded today across all faction sides.
    /// </summary>
    [JsonIgnore]
    public int TotalWins => Wins.Sum();
}
