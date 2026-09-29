namespace GenHub.Core.Models.Tools.Common;

/// <summary>
/// Represents the edges of a rectangle being resized on an editor canvas.
/// </summary>
/// <param name="Left">The left edge.</param>
/// <param name="Top">The top edge.</param>
/// <param name="Right">The right edge.</param>
/// <param name="Bottom">The bottom edge.</param>
public readonly record struct CanvasResizeEdges(int Left, int Top, int Right, int Bottom);
