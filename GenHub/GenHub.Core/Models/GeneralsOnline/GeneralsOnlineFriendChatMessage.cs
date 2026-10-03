using System;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// A friend direct message from the Generals Online WebSocket.
/// The server echoes sent messages back with the sender as source.
/// </summary>
public sealed class GeneralsOnlineFriendChatMessage
{
    /// <summary>
    /// Gets or sets the sender user id.
    /// </summary>
    [JsonPropertyName("source_user_id")]
    public long SourceUserId { get; set; } = -1;

    /// <summary>
    /// Gets or sets the recipient user id.
    /// </summary>
    [JsonPropertyName("target_user_id")]
    public long TargetUserId { get; set; } = -1;

    /// <summary>
    /// Gets or sets the formatted message text including the sender prefix.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the signed-in user sent this message.
    /// </summary>
    [JsonIgnore]
    public bool IsOwn { get; set; }

    /// <summary>
    /// Gets or sets the receive time in UTC.
    /// </summary>
    [JsonIgnore]
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
}
