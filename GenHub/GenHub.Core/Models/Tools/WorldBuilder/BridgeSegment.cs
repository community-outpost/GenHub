namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A bridge defined by paired start and end span points.
/// </summary>
/// <param name="Template">The bridge object template name (e.g. BridgeWood, BridgeConcrete).</param>
/// <param name="X1">World X start.</param>
/// <param name="Y1">World Y start.</param>
/// <param name="Z1">World Z start.</param>
/// <param name="X2">World X end.</param>
/// <param name="Y2">World Y end.</param>
/// <param name="Z2">World Z end.</param>
public sealed record BridgeSegment(
    string Template,
    float X1,
    float Y1,
    float Z1,
    float X2,
    float Y2,
    float Z2);
