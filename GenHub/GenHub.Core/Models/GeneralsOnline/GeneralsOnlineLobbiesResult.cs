using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Result payload of the backend GET Lobbies endpoint.
/// </summary>
public sealed class GeneralsOnlineLobbiesResult
{
    /// <summary>
    /// Gets or sets the active lobbies.
    /// </summary>
    [JsonPropertyName("lobbies")]
    public IReadOnlyList<GeneralsOnlineLobby> Lobbies { get; set; } = [];

    /// <summary>
    /// Gets or sets the estimated latency per lobby, aligned with <see cref="Lobbies"/>.
    /// </summary>
    [JsonPropertyName("latencies")]
    public IReadOnlyList<int> Latencies { get; set; } = [];

    /// <summary>
    /// Gets or sets the per-player latency entries for mesh diagnostics.
    /// </summary>
    [JsonPropertyName("playerlatencies")]
    public IReadOnlyList<GeneralsOnlineLatencyEntry> PlayerLatencies { get; set; } = [];
}
