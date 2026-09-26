namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// Specifies the handle direction when resizing a window on the editor canvas.
/// </summary>
public enum WndResizeDirection
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
