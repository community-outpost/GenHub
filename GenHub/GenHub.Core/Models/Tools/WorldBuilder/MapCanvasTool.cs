namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Canvas editing tools mirroring the upstream WorldBuilder toolbox.
/// </summary>
public enum MapCanvasTool
{
    /// <summary>Select and move objects.</summary>
    Pointer,

    /// <summary>Raise terrain (MoundTool).</summary>
    MoundUp,

    /// <summary>Lower terrain (MoundTool).</summary>
    MoundDown,

    /// <summary>Smooth terrain (FeatherTool).</summary>
    Smooth,

    /// <summary>Flatten terrain to a height (MeshMoldTool).</summary>
    Plateau,

    /// <summary>Paint tile textures (TileTool).</summary>
    TilePaint,

    /// <summary>Flood-fill tile textures (TileTool flood fill).</summary>
    TileFloodFill,

    /// <summary>Place fence post lines (FenceTool).</summary>
    Fence,

    /// <summary>Paint blend edges (BlendEdgeTool).</summary>
    BlendPaint,

    /// <summary>Erase blend edges.</summary>
    BlendErase,

    /// <summary>Scatter objects (GroveTool).</summary>
    Grove,

    /// <summary>Place objects (ObjectTool).</summary>
    ObjectPlace,

    /// <summary>Delete objects.</summary>
    ObjectErase,

    /// <summary>Pick tile height and texture (EyedropperTool).</summary>
    Eyedropper,

    /// <summary>Measure distances (RulerTool).</summary>
    Ruler,

    /// <summary>Place waypoints (WaypointTool).</summary>
    Waypoint,

    /// <summary>Link two waypoints (WaypointTool).</summary>
    WaypointLink,

    /// <summary>Draw area trigger polygons (PolygonTool).</summary>
    Trigger,

    /// <summary>Place and connect road segments (RoadTool).</summary>
    Road,

    /// <summary>Place bridges across spans (BridgeTool).</summary>
    Bridge,

    /// <summary>Construct terrain ramps between elevations (RampTool).</summary>
    Ramp,

    /// <summary>Set playable boundary rectangles (BorderTool).</summary>
    Border,

    /// <summary>Place water area polygons (WaterTool).</summary>
    WaterArea,
}
