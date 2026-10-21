// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Rendering;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for the terrain UV, lighting, and mesh ports.
/// </summary>
public sealed class WbTerrainTests
{
    /// <summary>
    /// Verifies quadrant selection: bit 1 flips V, bit 0 flips U.
    /// </summary>
    [Fact]
    public void GetTileUv_Quadrant_SelectsHalves()
    {
        var atlas = CreateAtlas();

        Assert.True(WbTerrainUv.GetTileUv(atlas, 0, out var minU0, out var minV0, out var maxU0, out var maxV0));
        Assert.True(WbTerrainUv.GetTileUv(atlas, 3, out var minU3, out var minV3, out var maxU3, out var maxV3));

        Assert.Equal(0.0f, minU0, 5);
        Assert.Equal(0.015625f, maxU0, 5);
        Assert.Equal(0.03125f, minV0, 5);
        Assert.Equal(0.0625f, maxV0, 5);
        Assert.Equal(0.015625f, minU3, 5);
        Assert.Equal(0.03125f, maxU3, 5);
        Assert.Equal(0.0f, minV3, 5);
        Assert.Equal(0.03125f, maxV3, 5);
    }

    /// <summary>
    /// Verifies missing tiles report no texture.
    /// </summary>
    [Fact]
    public void GetTileUv_MissingTile_ReturnsFalse()
    {
        Assert.False(WbTerrainUv.GetTileUv(CreateAtlas(), 9999, out _, out _, out _, out _));
    }

    /// <summary>
    /// Verifies missing tiles zero the cell UVs so they sample the fallback atlas origin.
    /// </summary>
    [Fact]
    public void GetCellUv_MissingTile_ZeroesUvs()
    {
        var terrain = CreateTerrain();
        terrain.TileIndices[0] = 9999;
        var u = new[] { 0.5f, 0.5f, 0.5f, 0.5f };
        var v = new[] { 0.5f, 0.5f, 0.5f, 0.5f };

        var (flip, hasTexture) = WbTerrainUv.GetCellUv(terrain, CreateAtlas(), 0, 0, u, v);

        Assert.False(flip);
        Assert.False(hasTexture);
        Assert.Equal([0.0f, 0.0f, 0.0f, 0.0f], u);
        Assert.Equal([0.0f, 0.0f, 0.0f, 0.0f], v);
    }

    /// <summary>
    /// Verifies the horizontal alpha directions.
    /// </summary>
    /// <param name="inverted">The inverted bits.</param>
    /// <param name="a0">Expected alpha 0.</param>
    /// <param name="a1">Expected alpha 1.</param>
    /// <param name="a2">Expected alpha 2.</param>
    /// <param name="a3">Expected alpha 3.</param>
    [Theory]
    [InlineData(0, 0, 255, 255, 0)]
    [InlineData(1, 255, 0, 0, 255)]
    public void GetCellAlpha_Horizontal_EncodesDirection(byte inverted, byte a0, byte a1, byte a2, byte a3)
    {
        var terrain = CreateTerrain();
        terrain.BlendTiles.Add(new MapBlendTile(4, 1, 0, 0, 0, inverted, 0, -1));
        terrain.BlendTileIndices[0] = 1;
        terrain.NumBlendedTiles = 2;
        var u = new float[4];
        var v = new float[4];
        var alpha = new byte[4];

        WbTerrainUv.GetCellAlpha(terrain, CreateAtlas(), 0, 0, u, v, alpha);

        Assert.Equal([a0, a1, a2, a3], alpha);
    }

    /// <summary>
    /// Verifies the diagonal alpha directions and flip tracking.
    /// </summary>
    [Fact]
    public void GetCellAlpha_Diagonals_EncodeAndFlip()
    {
        var u = new float[4];
        var v = new float[4];

        var right = CreateBlendedTerrain(0, 0, 1, 0, 0, 0);
        var alpha = new byte[4];
        Assert.True(WbTerrainUv.GetCellAlpha(right, CreateAtlas(), 0, 0, u, v, alpha));
        Assert.Equal([0, 0, 255, 0], alpha);

        var left = CreateBlendedTerrain(0, 0, 0, 1, 1, 1);
        alpha = new byte[4];
        Assert.True(WbTerrainUv.GetCellAlpha(left, CreateAtlas(), 0, 0, u, v, alpha));
        Assert.Equal([255, 255, 0, 255], alpha);
    }

    /// <summary>
    /// Verifies a sunlit upward normal saturates and a sideways normal keeps ambient.
    /// </summary>
    [Fact]
    public void LightVertex_SunOverhead_Saturates()
    {
        var ambient = new Vector3(0.2f, 0.2f, 0.2f);
        var dirs = new List<Vector3> { new(0, 0, 1) };
        var diffuse = new List<Vector3> { new(1, 1, 1) };

        var lit = WbTerrainLighting.LightVertex(Vector3.UnitZ, ambient, dirs, diffuse);
        var side = WbTerrainLighting.LightVertex(Vector3.UnitX, ambient, dirs, diffuse);

        Assert.Equal(new Vector3(1, 1, 1), lit);
        Assert.Equal(ambient, side);
        Assert.Equal(Vector3.UnitZ, WbTerrainLighting.CornerNormal(10, 10, 10, 10));
    }

    /// <summary>
    /// Verifies mesh vertex positions, lighting, and index winding.
    /// </summary>
    [Fact]
    public void Build_FlatTerrain_EmitsQuads()
    {
        var terrain = CreateTerrain();
        var lights = new List<Vector3> { new(0, 0, 1) };
        var diffuse = new List<Vector3> { new(1, 1, 1) };

        var (vertices, indices, extraVertices, extraIndices) = WbTerrainMesh.Build(
            terrain, CreateAtlas(), new Vector3(0.2f, 0.2f, 0.2f), lights, diffuse);

        Assert.Equal(4 * 4 * WbTerrainMesh.StrideFloats, vertices.Length);
        Assert.Equal([0u, 1u, 2u, 0u, 2u, 3u, 4u, 5u, 6u, 4u, 6u, 7u, 8u, 9u, 10u, 8u, 10u, 11u, 12u, 13u, 14u, 12u, 14u, 15u], indices);
        Assert.Empty(extraVertices);
        Assert.Empty(extraIndices);
        Assert.Equal(0.0f, vertices[0], 3);
        Assert.Equal(0.0f, vertices[1], 3);
        Assert.Equal(12.5f, vertices[2], 3);
        Assert.Equal(1.0f, vertices[3], 3);
    }

    private static WbTileAtlas CreateAtlas()
    {
        var tileUv = new Dictionary<int, WbAtlasRect> { [0] = new WbAtlasRect(0, 0, 64.0f / 2048.0f, 64.0f / 1024.0f), [1] = new WbAtlasRect(64.0f / 2048.0f, 0, 128.0f / 2048.0f, 64.0f / 1024.0f) };
        var classUv = new Dictionary<string, WbAtlasRect> { ["Dirt"] = new WbAtlasRect(0, 0, 128.0f / 2048.0f, 64.0f / 1024.0f) };
        return new WbTileAtlas(2048, 1024, tileUv, classUv);
    }

    private static MapTerrainData CreateTerrain()
    {
        var terrain = new MapTerrainData
        {
            Width = 2,
            Height = 2,
            Heights = [20, 20, 20, 20],
            TileIndices = [0, 1, 4, 5],
            BlendTileIndices = [0, 0, 0, 0],
            ExtraBlendTileIndices = [0, 0, 0, 0],
            CliffInfoIndices = [0, 0, 0, 0],
        };
        terrain.TextureClasses.Add(new MapTextureClass(0, 16, 4, "Dirt"));
        terrain.NumBitmapTiles = 16;
        terrain.BlendTiles.Add(new MapBlendTile(0, 0, 0, 0, 0, 0, 0, -1));
        terrain.NumBlendedTiles = 1;
        return terrain;
    }

    private static MapTerrainData CreateBlendedTerrain(byte horiz, byte vert, byte rightDiag, byte leftDiag, byte inverted, byte longDiag)
    {
        var terrain = CreateTerrain();
        terrain.BlendTiles.Add(new MapBlendTile(4, horiz, vert, rightDiag, leftDiag, inverted, longDiag, -1));
        terrain.BlendTileIndices[0] = 1;
        terrain.NumBlendedTiles = 2;
        return terrain;
    }
}
