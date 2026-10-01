namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// An edge (cliff/road blend) texture class.
/// </summary>
/// <param name="FirstTile">First tile index.</param>
/// <param name="NumTiles">Tile count.</param>
/// <param name="Width">Tile sheet width.</param>
/// <param name="Name">Edge texture name.</param>
public sealed record MapEdgeTextureClass(int FirstTile, int NumTiles, int Width, string Name);
