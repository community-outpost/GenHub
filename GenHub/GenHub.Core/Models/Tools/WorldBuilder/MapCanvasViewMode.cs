namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Projection mode for the WorldBuilder viewport canvas.
/// </summary>
public enum MapCanvasViewMode
{
    /// <summary>Isometric 3D perspective with shaded terrain elevation, 3D structures, and depth.</summary>
    Isometric3D = 0,

    /// <summary>Top-down 2D orthographic view.</summary>
    TopDown2D = 1,
}
