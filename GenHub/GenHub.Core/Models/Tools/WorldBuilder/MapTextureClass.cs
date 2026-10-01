namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A terrain texture class: a run of tiles in the bitmap tile table.
/// </summary>
/// <param name="FirstTile">First tile index.</param>
/// <param name="NumTiles">Tile count.</param>
/// <param name="Width">Tile sheet width.</param>
/// <param name="Name">Terrain type name.</param>
public sealed record MapTextureClass(int FirstTile, int NumTiles, int Width, string Name);
