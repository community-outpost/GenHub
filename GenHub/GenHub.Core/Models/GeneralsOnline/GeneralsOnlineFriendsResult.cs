using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Result payload of the backend GET Social/Friends endpoint.
/// </summary>
public sealed class GeneralsOnlineFriendsResult
{
    /// <summary>
    /// Gets or sets the confirmed friends.
    /// </summary>
    [JsonPropertyName("friends")]
    public IReadOnlyList<GeneralsOnlineFriend> Friends { get; set; } = [];

    /// <summary>
    /// Gets or sets the pending friend requests.
    /// </summary>
    [JsonPropertyName("pending_requests")]
    public IReadOnlyList<GeneralsOnlineFriend> PendingRequests { get; set; } = [];
}
