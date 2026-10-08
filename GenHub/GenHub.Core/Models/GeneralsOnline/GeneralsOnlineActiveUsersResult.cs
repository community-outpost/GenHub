using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Result payload of the Users/Active endpoint.
/// </summary>
public sealed class GeneralsOnlineActiveUsersResult
{
    /// <summary>
    /// Gets or sets the active users.
    /// </summary>
    [JsonPropertyName("active_users")]
    public IReadOnlyList<GeneralsOnlineActiveUser> ActiveUsers { get; set; } = [];
}
