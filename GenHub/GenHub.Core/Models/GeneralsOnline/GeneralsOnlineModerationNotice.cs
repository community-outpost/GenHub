using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// A moderation notice pushed to the session over the WebSocket.
/// </summary>
public sealed class GeneralsOnlineModerationNotice
{
    /// <summary>
    /// Gets or sets the moderation action type.
    /// </summary>
    [JsonPropertyName("action_type")]
    public string ActionType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the moderation reason.
    /// </summary>
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}
