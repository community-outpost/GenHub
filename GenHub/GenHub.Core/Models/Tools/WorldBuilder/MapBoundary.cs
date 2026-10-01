namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A playable-boundary rectangle (top-right corner; lower-left is always 0,0).
/// </summary>
/// <param name="X">Top-right corner X cell.</param>
/// <param name="Y">Top-right corner Y cell.</param>
public sealed record MapBoundary(int X, int Y);
