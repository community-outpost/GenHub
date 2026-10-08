namespace GenHub.Core.Models.Tools.Common;

/// <summary>
/// Specifies the handle direction when resizing a rectangle on an editor canvas.
/// Shared by the WND and Texture editors so resize handles behave identically.
/// </summary>
public enum CanvasResizeDirection
{
    /// <summary>No direction.</summary>
    None,

    /// <summary>North (top) edge.</summary>
    North,

    /// <summary>South (bottom) edge.</summary>
    South,

    /// <summary>West (left) edge.</summary>
    West,

    /// <summary>East (right) edge.</summary>
    East,

    /// <summary>North-West (top-left) corner.</summary>
    NorthWest,

    /// <summary>North-East (top-right) corner.</summary>
    NorthEast,

    /// <summary>South-West (bottom-left) corner.</summary>
    SouthWest,

    /// <summary>South-East (bottom-right) corner.</summary>
    SouthEast,
}
