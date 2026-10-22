using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Result payload of the backend GET Rooms endpoint.
/// </summary>
public sealed class GeneralsOnlineRoomsResult
{
    /// <summary>
    /// Gets or sets the network rooms.
    /// </summary>
    [JsonPropertyName("rooms")]
    public IReadOnlyList<GeneralsOnlineRoom> Rooms { get; set; } = [];
}
