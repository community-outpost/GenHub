namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A road segment defined by paired start and end centerline points.
/// </summary>
/// <param name="RoadType">The road template type name (e.g. PavedRoad, DirtRoad).</param>
/// <param name="X1">World X start.</param>
/// <param name="Y1">World Y start.</param>
/// <param name="Z1">World Z start.</param>
/// <param name="X2">World X end.</param>
/// <param name="Y2">World Y end.</param>
/// <param name="Z2">World Z end.</param>
/// <param name="IsAngled">True when the corner is angled rather than curved.</param>
/// <param name="IsTight">True when the corner has a tight turn radius.</param>
public sealed record RoadSegment(
    string RoadType,
    float X1,
    float Y1,
    float Z1,
    float X2,
    float Y2,
    float Z2,
    bool IsAngled = false,
    bool IsTight = false);
