using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Envelope for the PlayerStats endpoint response.
/// </summary>
public sealed class GeneralsOnlinePlayerStatsResult
{
    /// <summary>
    /// Gets or sets the player statistics, or null when absent.
    /// </summary>
    [JsonPropertyName("stats")]
    public GeneralsOnlinePlayerStats? Stats { get; set; }
}
