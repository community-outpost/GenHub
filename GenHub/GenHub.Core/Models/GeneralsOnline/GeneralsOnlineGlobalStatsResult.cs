using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Envelope for the GlobalStats endpoint response.
/// </summary>
public sealed class GeneralsOnlineGlobalStatsResult
{
    /// <summary>
    /// Gets or sets today's aggregate statistics, or null when absent.
    /// </summary>
    [JsonPropertyName("globalstats")]
    public GeneralsOnlineDailyStats? GlobalStats { get; set; }
}
