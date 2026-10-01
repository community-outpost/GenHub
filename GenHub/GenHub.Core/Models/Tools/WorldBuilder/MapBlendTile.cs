namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A blended tile record: per-edge blend indices plus edge class.
/// </summary>
/// <param name="BlendIndex">Blend tile index.</param>
/// <param name="Horizontal">Horizontal blend flags.</param>
/// <param name="Vertical">Vertical blend flags.</param>
/// <param name="RightDiagonal">Right diagonal blend flags.</param>
/// <param name="LeftDiagonal">Left diagonal blend flags.</param>
/// <param name="Inverted">Inverted blend flags.</param>
/// <param name="LongDiagonal">Long diagonal blend flag.</param>
/// <param name="CustomBlendEdgeClass">Custom edge class, or -1.</param>
public sealed record MapBlendTile(
    int BlendIndex,
    byte Horizontal,
    byte Vertical,
    byte RightDiagonal,
    byte LeftDiagonal,
    byte Inverted,
    byte LongDiagonal,
    int CustomBlendEdgeClass);
