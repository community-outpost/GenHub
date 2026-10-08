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
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for bridge deck and tower rendering.
/// </summary>
public sealed class WbBridgeServiceTests
{
    /// <summary>
    /// Verifies a bridge emits one deck plus two tower draws.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task BuildBridgesAsync_Bridge_EmitsDeckAndTowersAsync()
    {
        var service = new WbBridgeService(
            new StubRoads(),
            new StubThings(),
            new WbModelRenderService(new StubLoader(), new StubThings(), new StubTextures(), NullLogger<WbModelRenderService>.Instance),
            NullLogger<WbBridgeService>.Instance);

        var result = await service.BuildBridgesAsync(CreateMap(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(3, result.Data!.Count);
    }

    /// <summary>
    /// Verifies decks and towers ride on the terrain surface plus the stored offset.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task BuildBridgesAsync_GroundRelativeZ_SnapsToTerrainAsync()
    {
        var service = new WbBridgeService(
            new StubRoads(),
            new StubThings(),
            new WbModelRenderService(new StubLoader(), new StubThings(), new StubTextures(), NullLogger<WbModelRenderService>.Instance),
            NullLogger<WbBridgeService>.Instance);
        var map = CreateMap();
        map.Terrain.Width = 4;
        map.Terrain.Height = 4;
        map.Terrain.BorderSize = 0;
        map.Terrain.Heights = [100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100];

        var result = await service.BuildBridgesAsync(map, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var draws = result.Data!;
        Assert.Equal(3, draws.Count);
        Assert.Equal(66.5f, draws[0].Vertices.Span[2], 3);
        Assert.Equal(66.5f, draws[1].Vertices.Span[2], 3);
        Assert.Equal(66.5f, draws[2].Vertices.Span[2], 3);
    }

    /// <summary>
    /// Verifies unknown bridge templates are skipped without failing.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task BuildBridgesAsync_UnknownTemplate_SkipsQuietlyAsync()
    {
        var service = new WbBridgeService(
            new StubRoads(),
            new StubThings(),
            new WbModelRenderService(new StubLoader(), new StubThings(), new StubTextures(), NullLogger<WbModelRenderService>.Instance),
            NullLogger<WbBridgeService>.Instance);
        var map = new WorldBuilderMap();
        map.Objects.Add(new MapObjectEntry
        {
            X = 0, Y = 0, Z = 0, Name = "Nope",
            Flags = WorldBuilderConstants.ObjectFlags.BridgePoint1,
        });
        map.Objects.Add(new MapObjectEntry
        {
            X = 30, Y = 0, Z = 0, Name = "Nope",
            Flags = WorldBuilderConstants.ObjectFlags.BridgePoint2,
        });

        var result = await service.BuildBridgesAsync(map, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data!);
    }

    private static WorldBuilderMap CreateMap()
    {
        var map = new WorldBuilderMap();
        map.Objects.Add(new MapObjectEntry
        {
            X = 0, Y = 0, Z = 4, Name = "Wooden",
            Flags = WorldBuilderConstants.ObjectFlags.BridgePoint1,
        });
        map.Objects.Add(new MapObjectEntry
        {
            X = 30, Y = 0, Z = 4, Name = "Wooden",
            Flags = WorldBuilderConstants.ObjectFlags.BridgePoint2,
        });
        return map;
    }

    private sealed class StubRoads : IRoadCatalog
    {
        private static readonly BridgeInfo Wooden = new(
            "Wooden",
            1.0f,
            "Scaffold",
            "Tower",
            null,
            null,
            null,
            "BRDG.WOOD",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            null,
            null,
            [],
            [],
            null);

        public IReadOnlyList<RoadInfo> GetRoads()
        {
            return [];
        }

        public IReadOnlyList<BridgeInfo> GetBridges()
        {
            return [Wooden];
        }

        public RoadInfo? FindRoad(string name)
        {
            return null;
        }

        public BridgeInfo? FindBridge(string name)
        {
            return string.Equals(name, "Wooden", StringComparison.OrdinalIgnoreCase) ? Wooden : null;
        }
    }

    private sealed class StubThings : IThingTemplateCatalog
    {
        private static readonly ThingTemplateInfo Tower = new(
            "Tower", "Object", null, null, "USA", string.Empty, [], null, null, null, null, null, null, null, [], "TWR.TWR", 1.0f);

        public IReadOnlyList<ThingTemplateInfo> GetAll()
        {
            return [Tower];
        }

        public ThingTemplateInfo? FindByName(string name)
        {
            return string.Equals(name, "Tower", StringComparison.OrdinalIgnoreCase) ? Tower : null;
        }

        public ObjectTreeNode BuildObjectTree(IEnumerable<string>? legacyModelNames = null)
        {
            throw new NotSupportedException();
        }

        public MappedImageDefinition? FindButtonImage(ThingTemplateInfo template)
        {
            return null;
        }

        public MappedImageDefinition? FindSelectPortrait(ThingTemplateInfo template)
        {
            return null;
        }
    }

    private sealed class StubLoader : IW3DAssetLoader
    {
        public Task<OperationResult<W3DModel>> LoadAsync(string fileName, CancellationToken cancellationToken = default)
        {
            var white = new W3dRgba(255, 255, 255, 255);
            var mesh = new W3dMesh(
                "WOOD",
                "BRDG",
                0,
                [new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0)],
                [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
                [new W3dTriangle(0, 1, 2, 0, Vector3.UnitZ, 0)],
                [new W3dVertexMaterial("M", 0, white, white, white, white, 1, 1, 0)],
                [new W3dShader(3, 1, 0, 0, 0, 1, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0)],
                [],
                [new W3dMaterialPass([0], [0], [white, white, white], [])]);
            return Task.FromResult(OperationResult<W3DModel>.CreateSuccess(new W3DModel(fileName, [mesh], [], [])));
        }
    }

    private sealed class StubTextures : ITextureCache
    {
        public int Count => 0;

        public Task<OperationResult<DecodedTexture>> GetAsync(string textureName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<DecodedTexture>.CreateFailure("Missing."));
        }

        public void Clear()
        {
        }
    }
}
