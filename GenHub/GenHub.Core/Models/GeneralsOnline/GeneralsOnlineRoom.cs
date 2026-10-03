using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Represents a Generals Online network room from the backend Rooms endpoint.
/// The first room carries the show-all flag and views lobbies from every room.
/// </summary>
public sealed class GeneralsOnlineRoom
{
    /// <summary>
    /// Gets or sets the room id.
    /// </summary>
    [JsonPropertyName("id")]
    public int Id { get; set; } = -1;

    /// <summary>
    /// Gets or sets the room name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the parent room id, or null for top-level rooms.
    /// </summary>
    [JsonPropertyName("parent_id")]
    public int? ParentId { get; set; }

    /// <summary>
    /// Gets or sets the room flags.
    /// </summary>
    [JsonPropertyName("flags")]
    public int Flags { get; set; }
}
