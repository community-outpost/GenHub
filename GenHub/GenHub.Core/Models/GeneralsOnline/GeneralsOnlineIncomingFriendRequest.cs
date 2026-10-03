using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// An incoming friend request pushed over the WebSocket.
/// </summary>
public sealed class GeneralsOnlineIncomingFriendRequest
{
    /// <summary>
    /// Gets or sets the requester display name.
    /// </summary>
    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;
}
