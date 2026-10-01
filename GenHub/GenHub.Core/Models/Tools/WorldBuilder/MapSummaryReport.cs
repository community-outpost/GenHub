using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A JSON-friendly map summary for external tooling (MCP-bridge equivalent).
/// </summary>
public sealed class MapSummaryReport
{
    /// <summary>Gets or sets the map name.</summary>
    public string MapName { get; set; } = string.Empty;

    /// <summary>Gets or sets the terrain width in cells.</summary>
    public int Width { get; set; }

    /// <summary>Gets or sets the terrain height in cells.</summary>
    public int Height { get; set; }

    /// <summary>Gets or sets the player side count.</summary>
    public int SideCount { get; set; }

    /// <summary>Gets or sets the object count.</summary>
    public int ObjectCount { get; set; }

    /// <summary>Gets or sets the team count.</summary>
    public int TeamCount { get; set; }

    /// <summary>Gets or sets the trigger count.</summary>
    public int TriggerCount { get; set; }

    /// <summary>Gets or sets the waypoint link count.</summary>
    public int WaypointLinkCount { get; set; }

    /// <summary>Gets or sets the wave track count.</summary>
    public int WaveTrackCount { get; set; }

    /// <summary>Gets or sets the total world cash value.</summary>
    public int TotalWorldCash { get; set; }

    /// <summary>Gets the per-player script counts.</summary>
    public List<int> ScriptsPerPlayer { get; } = [];

    /// <summary>Gets the total number of scripts across all players.</summary>
    public int TotalScripts => ScriptsPerPlayer.Sum();
}
