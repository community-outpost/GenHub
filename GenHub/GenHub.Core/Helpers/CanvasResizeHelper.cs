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
    /// <param name="left">The original left edge.</param>
    /// <param name="top">The original top edge.</param>
    /// <param name="right">The original right edge.</param>
    /// <param name="bottom">The original bottom edge.</param>
    /// <param name="direction">The dragged handle direction.</param>
    /// <param name="deltaX">The horizontal pointer delta in content pixels.</param>
    /// <param name="deltaY">The vertical pointer delta in content pixels.</param>
    /// <param name="minSize">The minimum width and height in content pixels.</param>
    /// <returns>The resized edges.</returns>
    public static (int Left, int Top, int Right, int Bottom) Resize(
        int left,
        int top,
        int right,
        int bottom,
        CanvasResizeDirection direction,
        int deltaX,
        int deltaY,
        int minSize)
    {
        int minimum = Math.Max(1, minSize);
        return direction switch
        {
            CanvasResizeDirection.East => (left, top, Math.Max(left + minimum, right + deltaX), bottom),
            CanvasResizeDirection.West => (Math.Min(right - minimum, left + deltaX), top, right, bottom),
            CanvasResizeDirection.South => (left, top, right, Math.Max(top + minimum, bottom + deltaY)),
            CanvasResizeDirection.North => (left, Math.Min(bottom - minimum, top + deltaY), right, bottom),
            CanvasResizeDirection.SouthEast => (left, top, Math.Max(left + minimum, right + deltaX), Math.Max(top + minimum, bottom + deltaY)),
            CanvasResizeDirection.NorthEast => (left, Math.Min(bottom - minimum, top + deltaY), Math.Max(left + minimum, right + deltaX), bottom),
            CanvasResizeDirection.SouthWest => (Math.Min(right - minimum, left + deltaX), top, right, Math.Max(top + minimum, bottom + deltaY)),
            CanvasResizeDirection.NorthWest => (Math.Min(right - minimum, left + deltaX), Math.Min(bottom - minimum, top + deltaY), right, bottom),
            _ => (left, top, right, bottom),
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
