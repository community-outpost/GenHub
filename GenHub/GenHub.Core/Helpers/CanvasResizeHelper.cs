using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.Common;

namespace GenHub.Core.Helpers;

/// <summary>
/// Shared rectangle resize math for the editor canvases.
/// The WND and Texture editors both route handle drags through this helper so
/// corner and edge resizing behaves identically in every tool.
/// </summary>
public static class CanvasResizeHelper
{
    /// <summary>
    /// Resizes a rectangle from its original edges by a pointer delta.
    /// </summary>
    /// <param name="edges">The original edges.</param>
    /// <param name="direction">The dragged handle direction.</param>
    /// <param name="deltaX">The horizontal pointer delta in content pixels.</param>
    /// <param name="deltaY">The vertical pointer delta in content pixels.</param>
    /// <param name="minSize">The minimum width and height in content pixels.</param>
    /// <returns>The resized edges.</returns>
    public static (int Left, int Top, int Right, int Bottom) Resize(
        CanvasResizeEdges edges,
        CanvasResizeDirection direction,
        int deltaX,
        int deltaY,
        int minSize)
    {
        int minimum = Math.Max(1, minSize);
        return direction switch
        {
            CanvasResizeDirection.East => (edges.Left, edges.Top, Math.Max(edges.Left + minimum, edges.Right + deltaX), edges.Bottom),
            CanvasResizeDirection.West => (Math.Min(edges.Right - minimum, edges.Left + deltaX), edges.Top, edges.Right, edges.Bottom),
            CanvasResizeDirection.South => (edges.Left, edges.Top, edges.Right, Math.Max(edges.Top + minimum, edges.Bottom + deltaY)),
            CanvasResizeDirection.North => (edges.Left, Math.Min(edges.Bottom - minimum, edges.Top + deltaY), edges.Right, edges.Bottom),
            CanvasResizeDirection.SouthEast => (edges.Left, edges.Top, Math.Max(edges.Left + minimum, edges.Right + deltaX), Math.Max(edges.Top + minimum, edges.Bottom + deltaY)),
            CanvasResizeDirection.NorthEast => (edges.Left, Math.Min(edges.Bottom - minimum, edges.Top + deltaY), Math.Max(edges.Left + minimum, edges.Right + deltaX), edges.Bottom),
            CanvasResizeDirection.SouthWest => (Math.Min(edges.Right - minimum, edges.Left + deltaX), edges.Top, edges.Right, Math.Max(edges.Top + minimum, edges.Bottom + deltaY)),
            CanvasResizeDirection.NorthWest => (Math.Min(edges.Right - minimum, edges.Left + deltaX), Math.Min(edges.Bottom - minimum, edges.Top + deltaY), edges.Right, edges.Bottom),
            _ => (edges.Left, edges.Top, edges.Right, edges.Bottom),
        };
    }

    /// <summary>
    /// Gets the handle offset for a centered (north, south, west, east) handle.
    /// </summary>
    /// <param name="extent">The display width or height the handle is centered on.</param>
    /// <returns>The handle origin offset in device-independent pixels.</returns>
    public static double CenterHandleOffset(double extent) =>
        Math.Max(0, (extent / 2.0) - EditorConstants.ResizeHandleHalfSize);

    /// <summary>
    /// Gets the handle offset for a trailing (east, south, corner) handle.
    /// </summary>
    /// <param name="extent">The display width or height the handle trails.</param>
    /// <returns>The handle origin offset in device-independent pixels.</returns>
    public static double EndHandleOffset(double extent) =>
        Math.Max(0, extent - EditorConstants.ResizeHandleHalfSize);
}
