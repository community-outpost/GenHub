namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Custom cliff UV coordinates.
/// </summary>
/// <param name="TileIndex">Tile index.</param>
/// <param name="U">Eight UV coordinates (u0,v0..u3,v3).</param>
/// <param name="Flip">Flip flags.</param>
/// <param name="Mutant">Mutant flag.</param>
public sealed record MapCliffInfo(int TileIndex, IList<float> U, byte Flip, byte Mutant);
