namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Overlay layers rendered by the map canvas.
/// </summary>
[Flags]
public enum MapCanvasLayers
{
    /// <summary>No layers.</summary>
    None = 0,

    /// <summary>Base terrain tiles with height shading.</summary>
    Terrain = 1,

    /// <summary>Water table and water areas.</summary>
    Water = 2,

    /// <summary>Blend tile overlays.</summary>
    Blend = 4,

    /// <summary>Cliff shading.</summary>
    Cliffs = 8,

    /// <summary>Cell grid lines.</summary>
    Grid = 16,

    /// <summary>Map objects.</summary>
    Objects = 32,

    /// <summary>Waypoints and waypoint links.</summary>
    Waypoints = 64,

    /// <summary>Area triggers.</summary>
    Triggers = 128,

    /// <summary>Team membership highlights.</summary>
    Teams = 256,

    /// <summary>Playable boundary outline.</summary>
    Boundary = 512,

    /// <summary>Road networks.</summary>
    Roads = 1024,

    /// <summary>Bridges.</summary>
    Bridges = 2048,

    /// <summary>All layers.</summary>
    All = Terrain | Water | Blend | Cliffs | Grid | Objects | Waypoints | Triggers | Teams | Boundary | Roads | Bridges,
}
