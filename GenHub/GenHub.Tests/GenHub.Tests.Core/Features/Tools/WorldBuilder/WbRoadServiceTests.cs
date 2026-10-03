// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for road quad tessellation.
/// </summary>
public sealed class WbRoadServiceTests
{
    /// <summary>
    /// Verifies a segment tessellates to catalog width with surface UVs.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task BuildRoadsAsync_Segment_TessellatesWidthAsync()
    {
        var service = new WbRoadService(new StubRoads(), new StubTextures(), NullLogger<WbRoadService>.Instance);

        var result = await service.BuildRoadsAsync(CreateMap(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var draw = Assert.Single(result.Data!);
        Assert.Equal(4 * WbModelDraw.StrideFloats, draw.Vertices.Length);
        Assert.Equal([0u, 1u, 2u, 1u, 3u, 2u], draw.Indices.ToArray());
        Assert.Equal(10.0f, draw.Vertices.Span[1] - draw.Vertices.Span[1 + WbModelDraw.StrideFloats]);
        Assert.Equal(40.0f / 128.0f, draw.Vertices.Span[6]);
        Assert.Equal("Road.tga", draw.TextureName);
        Assert.NotNull(draw.Texture);
        Assert.True(draw.TwoSided);
    }

    /// <summary>
    /// Verifies road quads ride on the terrain surface plus the stored offset.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task BuildRoadsAsync_GroundRelativeZ_SnapsToTerrainAsync()
    {
        var service = new WbRoadService(new StubRoads(), new StubTextures(), NullLogger<WbRoadService>.Instance);
        var map = CreateMap();
        map.Terrain.Width = 4;
        map.Terrain.Height = 4;
        map.Terrain.BorderSize = 0;
        map.Terrain.Heights = [100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100];

        var result = await service.BuildRoadsAsync(map, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var draw = Assert.Single(result.Data!);
        Assert.Equal(64.8f, draw.Vertices.Span[2], 3);
    }

    /// <summary>
    /// Verifies unknown road types are skipped without failing.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task BuildRoadsAsync_UnknownType_SkipsQuietlyAsync()
    {
        var service = new WbRoadService(new StubRoads(), new StubTextures(), NullLogger<WbRoadService>.Instance);
        var map = new WorldBuilderMap();
        map.Objects.Add(new MapObjectEntry
        {
            X = 0, Y = 0, Z = 0, Name = "Nope",
            Flags = WorldBuilderConstants.ObjectFlags.RoadPoint1,
        });
        map.Objects.Add(new MapObjectEntry
        {
            X = 10, Y = 0, Z = 0, Name = "Nope",
            Flags = WorldBuilderConstants.ObjectFlags.RoadPoint2,
        });

        var result = await service.BuildRoadsAsync(map, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data!);
    }

    private static WorldBuilderMap CreateMap()
    {
        var map = new WorldBuilderMap();
        map.Objects.Add(new MapObjectEntry
        {
            X = 0, Y = 0, Z = 2, Name = "Paved",
            Flags = WorldBuilderConstants.ObjectFlags.RoadPoint1,
        });
        map.Objects.Add(new MapObjectEntry
        {
            X = 40, Y = 0, Z = 2, Name = "Paved",
            Flags = WorldBuilderConstants.ObjectFlags.RoadPoint2,
        });
        return map;
    }

    private sealed class StubRoads : IRoadCatalog
    {
        public IReadOnlyList<RoadInfo> GetRoads()
        {
            return [new RoadInfo("Paved", "Road.tga", 10.0f, 48.0f)];
        }

        public IReadOnlyList<BridgeInfo> GetBridges()
        {
            return [];
        }

        public RoadInfo? FindRoad(string name)
        {
            return name == "Paved" ? GetRoads()[0] : null;
        }

        public BridgeInfo? FindBridge(string name)
        {
            return null;
        }
    }

    private sealed class StubTextures : ITextureCache
    {
        public int Count => 1;

        public Task<OperationResult<DecodedTexture>> GetAsync(string textureName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<DecodedTexture>.CreateSuccess(new DecodedTexture(4, 4, new byte[4 * 4 * 4])));
        }

        public void Clear()
        {
        }
    }
}
