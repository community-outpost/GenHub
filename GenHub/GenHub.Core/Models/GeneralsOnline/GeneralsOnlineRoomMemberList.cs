using System.Collections.Generic;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// A network room occupant list pushed over the WebSocket.
/// </summary>
public sealed class GeneralsOnlineRoomMemberList
{
    /// <summary>
    /// Gets the room occupants.
    /// </summary>
    public required IReadOnlyList<GeneralsOnlineRoomOccupant> Occupants { get; init; }
}
