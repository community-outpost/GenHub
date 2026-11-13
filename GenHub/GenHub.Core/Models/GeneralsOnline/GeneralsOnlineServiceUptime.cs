using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Service start time and uptime from the Monitoring Uptime endpoint.
/// </summary>
public sealed class GeneralsOnlineServiceUptime
{
    /// <summary>
    /// Gets or sets the service start time as reported by the backend.
    /// </summary>
    [JsonPropertyName("start_time")]
    public string StartTime { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable uptime as reported by the backend.
    /// </summary>
    [JsonPropertyName("uptime")]
    public string Uptime { get; set; } = string.Empty;
}
