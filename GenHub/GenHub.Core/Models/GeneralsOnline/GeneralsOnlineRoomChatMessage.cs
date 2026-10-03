using System;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// A network room chat message from the Generals Online WebSocket.
/// </summary>
public sealed class GeneralsOnlineRoomChatMessage
{
    /// <summary>
    /// Gets or sets the raw formatted message text including the sender prefix.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the sender is staff.
    /// </summary>
    [JsonPropertyName("admin")]
    public bool IsAdmin { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the message is a /me action.
    /// </summary>
    [JsonPropertyName("action")]
    public bool IsAction { get; set; }

    /// <summary>
    /// Gets or sets the receive time in UTC.
    /// </summary>
    [JsonIgnore]
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
}
