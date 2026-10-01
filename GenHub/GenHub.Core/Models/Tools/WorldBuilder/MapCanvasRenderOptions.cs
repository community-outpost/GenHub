using GenHub.Core.Constants;

namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Options controlling map canvas rendering.
/// </summary>
public sealed class MapCanvasRenderOptions
{
    /// <summary>Gets or sets the enabled layers.</summary>
    public MapCanvasLayers Layers { get; set; } = MapCanvasLayers.All;

    /// <summary>Gets or sets the water table height. Cells at or below render as water.</summary>
    public int WaterLevel { get; set; } = WorldBuilderConstants.Canvas.DefaultWaterLevel;

    /// <summary>Gets or sets the grid spacing in cells.</summary>
    public int GridStep { get; set; } = WorldBuilderConstants.Canvas.DefaultGridStep;

    /// <summary>Gets or sets the projection view mode (Isometric 3D vs TopDown 2D).</summary>
    public MapCanvasViewMode ViewMode { get; set; } = MapCanvasViewMode.TopDown2D;

    /// <summary>Gets or sets a value indicating whether terrain wireframe is rendered.</summary>
    public bool Wireframe { get; set; }

    /// <summary>Gets or sets the camera pitch angle in degrees (15° to 90°).</summary>
    public float CameraPitch { get; set; } = 45f;

    /// <summary>Gets or sets the camera yaw angle in degrees (0° to 360°).</summary>
    public float CameraYaw { get; set; } = 45f;

    /// <summary>Gets or sets the sun pitch angle in degrees.</summary>
    public float SunPitch { get; set; } = 45f;

    /// <summary>Gets or sets the sun yaw angle in degrees.</summary>
    public float SunYaw { get; set; } = 45f;

    /// <summary>Gets or sets the name of the selected object, highlighted when set.</summary>
    public string? SelectedObjectName { get; set; }

    /// <summary>Gets or sets the name of the selected team, highlighted when set.</summary>
    public string? SelectedTeamName { get; set; }

    /// <summary>Gets or sets a value indicating whether all object names are labeled on the map.</summary>
    public bool ShowAllObjectLabels { get; set; }
}
