using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// A friend presence change from the Generals Online WebSocket.
/// </summary>
public sealed class GeneralsOnlineFriendPresence
{
    /// <summary>
    /// Gets or sets the friend user id, or -1 when not provided by the payload.
    /// </summary>
    [JsonPropertyName("user_id")]
    public long UserId { get; set; } = -1;

    /// <summary>
    /// Gets or sets the friend display name.
    /// </summary>
    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the friend is online.
    /// </summary>
    [JsonPropertyName("online")]
    public bool IsOnline { get; set; }
}
