// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for the terrain render-data builder (atlas plus mesh upload payload).
/// </summary>
public sealed class WbTerrainRenderServiceTests
{
    /// <summary>
    /// Verifies a single textured class produces a valid atlas and full mesh.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task BuildAsync_TexturedClass_BuildsAtlasAndMeshAsync()
    {
        var service = new WbTerrainRenderService(new StubTextureCache(), new StubTerrainCatalog(), NullLogger<WbTerrainRenderService>.Instance);

        var result = await service.BuildAsync(
            CreateMap("Dirt"),
            new Vector3(0.2f, 0.2f, 0.2f),
            [new Vector3(0, 0, 1)],
            [new Vector3(1, 1, 1)],
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var data = result.Data!;
        Assert.Equal(WbTerrainRenderService.AtlasWidth, data.AtlasWidth);
        Assert.Equal(data.AtlasPixels.Length, data.AtlasWidth * data.AtlasHeight * 4);
        Assert.Equal(4 * 4 * WbTerrainMesh.StrideFloats, data.Vertices.Length);
        Assert.Equal(24, data.Indices.Length);
        Assert.True(data.Atlas.TileUv.ContainsKey(0));
    }

    /// <summary>
    /// Verifies an unresolvable class still succeeds with a zero-UV fallback.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task BuildAsync_MissingClass_SucceedsWithFallbackAsync()
    {
        var service = new WbTerrainRenderService(new StubTextureCache(), new MissingTerrainCatalog(), NullLogger<WbTerrainRenderService>.Instance);

        var result = await service.BuildAsync(
            CreateMap("Unknown"),
            new Vector3(0.2f, 0.2f, 0.2f),
            [new Vector3(0, 0, 1)],
            [new Vector3(1, 1, 1)],
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var data = result.Data!;
        Assert.Equal(24, data.Indices.Length);
        Assert.True(data.Atlas.TileUv.ContainsKey(0));
        Assert.Equal(0.0f, data.Atlas.TileUv[0].MinU);
        Assert.Equal(0.0f, data.Atlas.TileUv[0].MinV);
        Assert.Equal(60, data.AtlasPixels.Span[0]);
        Assert.Equal(100, data.AtlasPixels.Span[1]);
        Assert.Equal(130, data.AtlasPixels.Span[2]);
        Assert.Equal(255, data.AtlasPixels.Span[3]);
    }

    /// <summary>
    /// Verifies the per-class atlas rect starts where the class tiles were packed.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task BuildAsync_SecondClass_ClassRectStartsAtPackedSlotAsync()
    {
        var service = new WbTerrainRenderService(new StubTextureCache(), new TwoClassCatalog(), NullLogger<WbTerrainRenderService>.Instance);
        var map = CreateMap("Dirt");
        map.Terrain.TextureClasses.Clear();
        map.Terrain.TextureClasses.Add(new MapTextureClass(0, 16, 4, "Dirt"));
        map.Terrain.TextureClasses.Add(new MapTextureClass(16, 16, 4, "Grass"));

        var result = await service.BuildAsync(
            map,
            new Vector3(0.2f, 0.2f, 0.2f),
            [new Vector3(0, 0, 1)],
            [new Vector3(1, 1, 1)],
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var atlas = result.Data!.Atlas;
        Assert.Equal(0.0f, atlas.ClassUv["Dirt"].MinU);
        Assert.Equal(0.5f, atlas.ClassUv["Grass"].MinU, 5);
    }

    private static WorldBuilderMap CreateMap(string className)
    {
        var map = new WorldBuilderMap();
        map.Terrain.Width = 2;
        map.Terrain.Height = 2;
        map.Terrain.Heights = [20, 20, 20, 20];
        map.Terrain.TileIndices = [0, 0, 0, 0];
        map.Terrain.BlendTileIndices = [0, 0, 0, 0];
        map.Terrain.ExtraBlendTileIndices = [0, 0, 0, 0];
        map.Terrain.CliffInfoIndices = [0, 0, 0, 0];
        map.Terrain.TextureClasses.Add(new MapTextureClass(0, 1, 1, className));
        map.Terrain.NumBitmapTiles = 1;
        map.Terrain.BlendTiles.Add(new MapBlendTile(0, 0, 0, 0, 0, 0, 0, -1));
        map.Terrain.NumBlendedTiles = 1;
        return map;
    }

    private sealed class StubTextureCache : ITextureCache
    {
        public int Count => 1;

        public Task<OperationResult<DecodedTexture>> GetAsync(string textureName, CancellationToken cancellationToken = default)
        {
            var pixels = new byte[64 * 64 * 4];
            Array.Fill(pixels, (byte)255);
            return Task.FromResult(OperationResult<DecodedTexture>.CreateSuccess(new DecodedTexture(64, 64, pixels)));
        }

        public void Clear()
        {
        }
    }

    private sealed class StubTerrainCatalog : ITerrainTypeCatalog
    {
        public IReadOnlyList<TerrainTypeInfo> GetAll()
        {
            return [new TerrainTypeInfo("Dirt", "Dirt.tga", false, null, false, 0, 0)];
        }

        public IReadOnlyList<TerrainTypeInfo> GetPaletteEntries()
        {
            return GetAll();
        }

        public TerrainTypeInfo? FindByName(string name)
        {
            return string.Equals(name, "Dirt", StringComparison.OrdinalIgnoreCase) ? GetAll()[0] : null;
        }
    }

    private sealed class TwoClassCatalog : ITerrainTypeCatalog
    {
        public IReadOnlyList<TerrainTypeInfo> GetAll()
        {
            return
            [
                new TerrainTypeInfo("Dirt", "Dirt.tga", false, null, false, 0, 0),
                new TerrainTypeInfo("Grass", "Grass.tga", false, null, false, 0, 0),
            ];
        }

        public IReadOnlyList<TerrainTypeInfo> GetPaletteEntries()
        {
            return GetAll();
        }

        public TerrainTypeInfo? FindByName(string name)
        {
            return GetAll().FirstOrDefault(info => string.Equals(info.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class MissingTerrainCatalog : ITerrainTypeCatalog
    {
        public IReadOnlyList<TerrainTypeInfo> GetAll()
        {
            return [];
        }

        public IReadOnlyList<TerrainTypeInfo> GetPaletteEntries()
        {
            return [];
        }

        public TerrainTypeInfo? FindByName(string name)
        {
            return null;
        }
    }
}
