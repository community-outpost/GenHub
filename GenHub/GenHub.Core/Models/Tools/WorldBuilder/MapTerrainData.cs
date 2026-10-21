namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Terrain data: heights, tile layers, texture classes, blends, and cliffs.
/// </summary>
public sealed class MapTerrainData
{
    /// <summary>Gets or sets the width in cells.</summary>
    public int Width { get; set; }

    /// <summary>Gets or sets the height in cells.</summary>
    public int Height { get; set; }

    /// <summary>Gets or sets the pre-halving width for legacy V1 chunks; 0 when unknown.</summary>
    public int OriginalWidth { get; set; }

    /// <summary>Gets or sets the pre-halving height for legacy V1 chunks; 0 when unknown.</summary>
    public int OriginalHeight { get; set; }

    /// <summary>Gets or sets the border size in cells.</summary>
    public int BorderSize { get; set; }

    /// <summary>Gets the playable boundary rectangles.</summary>
    public List<MapBoundary> Boundaries { get; } = [];

    /// <summary>Gets or sets the raw height bytes (Width * Height).</summary>
    public IList<byte> Heights { get; set; } = [];

    /// <summary>Gets or sets the base tile indices.</summary>
    public IList<short> TileIndices { get; set; } = [];

    /// <summary>Gets or sets the blend tile indices.</summary>
    public IList<short> BlendTileIndices { get; set; } = [];

    /// <summary>Gets or sets the extra (three-way) blend tile indices.</summary>
    public IList<short> ExtraBlendTileIndices { get; set; } = [];

    /// <summary>Gets or sets the cliff info indices.</summary>
    public IList<short> CliffInfoIndices { get; set; } = [];

    /// <summary>Gets or sets the packed cliff-state bits.</summary>
    public IList<byte> CliffState { get; set; } = [];

    /// <summary>Gets or sets the bitmap tile count.</summary>
    public int NumBitmapTiles { get; set; }

    /// <summary>Gets or sets the blend tile count.</summary>
    public int NumBlendedTiles { get; set; }

    /// <summary>Gets or sets the cliff info count.</summary>
    public int NumCliffInfo { get; set; }

    /// <summary>Gets the terrain texture classes.</summary>
    public List<MapTextureClass> TextureClasses { get; } = [];

    /// <summary>Gets or sets the edge tile count.</summary>
    public int NumEdgeTiles { get; set; }

    /// <summary>Gets the edge texture classes.</summary>
    public List<MapEdgeTextureClass> EdgeTextureClasses { get; } = [];

    /// <summary>Gets the blend tiles (index 0 is the implicit default).</summary>
    public List<MapBlendTile> BlendTiles { get; } = [];

    /// <summary>Gets the cliff infos (index 0 is the implicit default).</summary>
    public List<MapCliffInfo> CliffInfos { get; } = [];
}
