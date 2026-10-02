// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for texture-class-aware terrain painting: class allocation, sub-tile
/// parity, blend records, auto edge-out, and tile optimization.
/// </summary>
public sealed class MapTexturePaintTests
{
    /// <summary>
    /// Verifies the sub-tile encoding class = tileNdx right-shift 2 and
    /// subtile = tileNdx and 3 across parities.
    /// </summary>
    [Fact]
    public void SetTileNdx_Parity_EncodesClassAndSubtile()
    {
        var terrain = CreateTerrain();
        var classIndex = MapTerrainTools.EnsureTextureClass(terrain, new MapTextureClass(0, 16, 4, "Dirt"));

        Assert.True(MapTerrainTools.SetTileNdx(terrain, 4, 4, classIndex));
        Assert.True(MapTerrainTools.SetTileNdx(terrain, 5, 4, classIndex));
        Assert.True(MapTerrainTools.SetTileNdx(terrain, 4, 5, classIndex));
        Assert.True(MapTerrainTools.SetTileNdx(terrain, 5, 5, classIndex));

        Assert.Equal(40, terrain.TileIndices[(4 * 10) + 4]);
        Assert.Equal(41, terrain.TileIndices[(4 * 10) + 5]);
        Assert.Equal(42, terrain.TileIndices[(5 * 10) + 4]);
        Assert.Equal(43, terrain.TileIndices[(5 * 10) + 5]);
        foreach (var tile in new[] { 40, 41, 42, 43 })
        {
            Assert.Equal(0, MapTerrainTools.GetTextureClassFromNdx(terrain, tile));
            Assert.Equal(10, tile >> 2);
            Assert.Equal(tile & 3, tile - ((tile >> 2) << 2));
        }
    }

    /// <summary>
    /// Verifies a second paint of the same class reuses the allocation and a
    /// second class appends after the first tile run.
    /// </summary>
    [Fact]
    public void EnsureTextureClass_SecondClass_AppendsTileRun()
    {
        var terrain = CreateTerrain();

        var dirt = MapTerrainTools.EnsureTextureClass(terrain, new MapTextureClass(0, 16, 4, "Dirt"));
        var again = MapTerrainTools.EnsureTextureClass(terrain, new MapTextureClass(99, 16, 4, "Dirt"));
        var grass = MapTerrainTools.EnsureTextureClass(terrain, new MapTextureClass(0, 4, 2, "Grass"));

        Assert.Equal(0, dirt);
        Assert.Equal(0, again);
        Assert.Equal(1, grass);
        Assert.Equal(16, terrain.TextureClasses[1].FirstTile);
        Assert.Equal(20, terrain.NumBitmapTiles);
    }

    /// <summary>
    /// Verifies painting clears stale blend and cliff indices on the cell.
    /// </summary>
    [Fact]
    public void SetTileNdx_PaintedCell_ClearsBlendsAndCliffs()
    {
        var terrain = CreateTerrain();
        var classIndex = MapTerrainTools.EnsureTextureClass(terrain, new MapTextureClass(0, 16, 4, "Dirt"));
        var index = (5 * 10) + 5;
        terrain.BlendTileIndices[index] = 3;
        terrain.ExtraBlendTileIndices[index] = 2;
        terrain.CliffInfoIndices[index] = 1;

        MapTerrainTools.SetTileNdx(terrain, 5, 5, classIndex);

        Assert.Equal(0, terrain.BlendTileIndices[index]);
        Assert.Equal(0, terrain.ExtraBlendTileIndices[index]);
        Assert.Equal(0, terrain.CliffInfoIndices[index]);
    }

    /// <summary>
    /// Verifies blending a boundary cell creates a blend record and keeps the
    /// blend table count consistent.
    /// </summary>
    [Fact]
    public void BlendTile_BoundaryCell_CreatesBlendRecord()
    {
        var terrain = CreateTerrain();
        MapTerrainTools.EnsureTextureClass(terrain, new MapTextureClass(0, 16, 4, "Dirt"));
        MapTerrainTools.EnsureTextureClass(terrain, new MapTextureClass(0, 16, 4, "Grass"));
        for (var y = 0; y < 10; y++)
        {
            for (var x = 0; x < 5; x++)
            {
                MapTerrainTools.SetTileNdx(terrain, x, y, 0);
            }

            for (var x = 5; x < 10; x++)
            {
                MapTerrainTools.SetTileNdx(terrain, x, y, 1);
            }
        }

        MapTerrainTools.BlendTile(terrain, 5, 5, 4, 5, null);

        var index = (5 * 10) + 5;
        Assert.True(terrain.BlendTileIndices[index] > 0);
        Assert.Equal(terrain.BlendTiles.Count, terrain.NumBlendedTiles);
        var record = terrain.BlendTiles[terrain.BlendTileIndices[index]];
        Assert.Equal(1, record.Horizontal);
        Assert.Equal(0, record.Vertical);
        var blendedClass = MapTerrainTools.GetTextureClassFromNdx(terrain, record.BlendIndex);
        Assert.Equal(0, blendedClass);
    }

    /// <summary>
    /// Verifies blending a cell into its own tiling clears the blend instead.
    /// </summary>
    [Fact]
    public void BlendTile_SameTiling_ClearsBlend()
    {
        var terrain = CreateTerrain();
        MapTerrainTools.EnsureTextureClass(terrain, new MapTextureClass(0, 16, 4, "Dirt"));
        MapTerrainTools.SetTileNdx(terrain, 5, 5, 0);
        MapTerrainTools.SetTileNdx(terrain, 4, 5, 0);

        MapTerrainTools.BlendTile(terrain, 5, 5, 4, 5, null);

        Assert.Equal(0, terrain.BlendTileIndices[(5 * 10) + 5]);
    }

    /// <summary>
    /// Verifies auto edge-out blends the border of a painted region.
    /// </summary>
    [Fact]
    public void AutoBlendOut_PaintedBlock_BlendsBorder()
    {
        var terrain = CreateTerrain();
        MapTerrainTools.EnsureTextureClass(terrain, new MapTextureClass(0, 16, 4, "Dirt"));
        MapTerrainTools.EnsureTextureClass(terrain, new MapTextureClass(0, 16, 4, "Grass"));
        for (var y = 0; y < 10; y++)
        {
            for (var x = 0; x < 10; x++)
            {
                MapTerrainTools.SetTileNdx(terrain, x, y, 1);
            }
        }

        for (var y = 3; y < 7; y++)
        {
            for (var x = 3; x < 7; x++)
            {
                MapTerrainTools.SetTileNdx(terrain, x, y, 0);
            }
        }

        MapTerrainTools.AutoBlendOut(terrain, 5, 5);

        var blended = 0;
        for (var i = 0; i < 100; i++)
        {
            if (terrain.BlendTileIndices[i] > 0)
            {
                blended++;
            }
        }

        Assert.True(blended > 0);
        Assert.Equal(terrain.BlendTiles.Count, terrain.NumBlendedTiles);
        AssertValidTerrain(terrain);
    }

    /// <summary>
    /// Verifies optimization keeps every tile and blend resolvable.
    /// </summary>
    [Fact]
    public void OptimizeTiles_PaintedAndBlended_StaysValid()
    {
        var terrain = CreateTerrain();
        MapTerrainTools.EnsureTextureClass(terrain, new MapTextureClass(0, 16, 4, "Dirt"));
        MapTerrainTools.EnsureTextureClass(terrain, new MapTextureClass(0, 16, 4, "Grass"));
        for (var y = 0; y < 10; y++)
        {
            for (var x = 0; x < 10; x++)
            {
                MapTerrainTools.SetTileNdx(terrain, x, y, x < 5 ? 0 : 1);
            }
        }

        MapTerrainTools.BlendTile(terrain, 5, 5, 4, 5, null);

        MapTerrainTools.OptimizeTiles(terrain);

        Assert.Equal(2, terrain.TextureClasses.Count);
        AssertValidTerrain(terrain);
        Assert.Equal(0, MapTerrainTools.GetTextureClass(terrain, 2, 2, false));
        Assert.Equal(1, MapTerrainTools.GetTextureClass(terrain, 7, 7, false));
    }

    private static MapTerrainData CreateTerrain()
    {
        return new MapTerrainData
        {
            Width = 10,
            Height = 10,
            Heights = new byte[100],
            TileIndices = new short[100],
            BlendTileIndices = new short[100],
            ExtraBlendTileIndices = new short[100],
            CliffInfoIndices = new short[100],
            CliffState = new byte[100],
        };
    }

    private static void AssertValidTerrain(MapTerrainData terrain)
    {
        Assert.Equal(terrain.BlendTiles.Count, terrain.NumBlendedTiles);
        for (var i = 0; i < terrain.TileIndices.Count; i++)
        {
            Assert.True(MapTerrainTools.GetTextureClassFromNdx(terrain, terrain.TileIndices[i]) >= 0);
        }

        for (var i = 0; i < terrain.BlendTileIndices.Count; i++)
        {
            Assert.InRange(terrain.BlendTileIndices[i], 0, terrain.BlendTiles.Count - 1);
            Assert.InRange(terrain.ExtraBlendTileIndices[i], 0, terrain.BlendTiles.Count - 1);
        }
    }
}
