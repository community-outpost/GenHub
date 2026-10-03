using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// A network room occupant from the backend member list update.
/// </summary>
public sealed class GeneralsOnlineRoomOccupant
{
    /// <summary>
    /// Gets or sets the occupant user id.
    /// </summary>
    [JsonPropertyName("UserID")]
    public long UserId { get; set; } = -1;

    /// <summary>
    /// Gets or sets the occupant display name.
    /// </summary>
    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the occupant is staff.
    /// </summary>
    [JsonPropertyName("IsAdmin")]
    public bool IsAdmin { get; set; }
}
