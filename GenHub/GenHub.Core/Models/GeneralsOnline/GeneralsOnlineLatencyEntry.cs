using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Per-player latency entry from the backend lobby list.
/// </summary>
public sealed class GeneralsOnlineLatencyEntry
{
    /// <summary>
    /// Gets or sets the user id.
    /// </summary>
    [JsonPropertyName("user_id")]
    public long UserId { get; set; } = -1;

    /// <summary>
    /// Gets or sets the estimated latency in milliseconds.
    /// </summary>
    [JsonPropertyName("latency")]
    public int Latency { get; set; }
}
