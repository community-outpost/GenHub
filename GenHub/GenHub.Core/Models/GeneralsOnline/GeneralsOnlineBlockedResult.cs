using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Result payload of the backend GET Social/Blocked endpoint.
/// </summary>
public sealed class GeneralsOnlineBlockedResult
{
    /// <summary>
    /// Gets or sets the blocked users.
    /// </summary>
    [JsonPropertyName("blocked")]
    public IReadOnlyList<GeneralsOnlineFriend> Blocked { get; set; } = [];
}
