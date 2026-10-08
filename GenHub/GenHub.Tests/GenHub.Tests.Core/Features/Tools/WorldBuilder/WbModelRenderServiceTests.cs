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
/// Tests for the CPU model draw builder.
/// </summary>
public sealed class WbModelRenderServiceTests
{
    /// <summary>
    /// Verifies a placed object bakes world-space draws with resolved textures.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task BuildAsync_PlacedObject_BakesWorldDrawAsync()
    {
        var service = new WbModelRenderService(
            new StubLoader(CreateModel()),
            new StubThingCatalog(),
            new StubTextureCache(),
            NullLogger<WbModelRenderService>.Instance);
        var map = new WorldBuilderMap();
        map.Objects.Add(new MapObjectEntry { X = 10, Y = 20, Z = 5, Angle = 0, Name = "Tank" });

        var result = await service.BuildAsync(map, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var draw = Assert.Single(result.Data!.Draws);
        Assert.Equal(3 * WbModelDraw.StrideFloats, draw.Vertices.Length);
        Assert.Equal([0u, 1u, 2u], draw.Indices.ToArray());
        Assert.Equal(10.0f, draw.Vertices.Span[0]);
        Assert.Equal(20.0f, draw.Vertices.Span[1]);
        Assert.Equal(5.0f, draw.Vertices.Span[2]);
        Assert.Equal(1.0f, draw.Vertices.Span[5]);
        Assert.Equal("Tank.tga", draw.TextureName);
        Assert.NotNull(draw.Texture);
        Assert.False(draw.State.BlendEnabled);
        Assert.False(draw.TwoSided);
    }

    /// <summary>
    /// Verifies bind-pose skinning places HLOD meshes through their bones.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task BuildAsync_HlodBone_TransformsThroughBoneAsync()
    {
        var pivot = new W3dPivot("ROOT", -1, new Vector3(0, 5, 0), Quaternion.Identity);
        var sub = new W3dHlodSubObject(0, "TANK.TURRET", "TURRET");
        var model = CreateModel() with
        {
            Hierarchies = [new W3dHierarchy("H", [pivot])],
            Hlods = [new W3dHlod("TANK", "H", [[sub]], [], [])],
        };
        var service = new WbModelRenderService(
            new StubLoader(model),
            new StubThingCatalog(),
            new StubTextureCache(),
            NullLogger<WbModelRenderService>.Instance);
        var map = new WorldBuilderMap();
        map.Objects.Add(new MapObjectEntry { X = 10, Y = 20, Z = 0, Angle = 0, Name = "Tank" });

        var result = await service.BuildAsync(map, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var draw = Assert.Single(result.Data!.Draws);
        Assert.Equal(25.0f, draw.Vertices.Span[1]);
    }

    /// <summary>
    /// Verifies placed objects sit on the terrain surface: the stored Z is a
    /// height offset above ground, not an absolute elevation.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task BuildAsync_GroundRelativeZ_SnapsToTerrainAsync()
    {
        var service = new WbModelRenderService(
            new StubLoader(CreateModel()),
            new StubThingCatalog(),
            new StubTextureCache(),
            NullLogger<WbModelRenderService>.Instance);
        var map = new WorldBuilderMap();
        map.Terrain.Width = 2;
        map.Terrain.Height = 2;
        map.Terrain.BorderSize = 0;
        map.Terrain.Heights = [100, 100, 100, 100];
        map.Objects.Add(new MapObjectEntry { X = 5, Y = 5, Z = 0, Angle = 0, Name = "Tank" });
        map.Objects.Add(new MapObjectEntry { X = 15, Y = 5, Z = 10, Angle = 0, Name = "Tank" });

        var result = await service.BuildAsync(map, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var draws = result.Data!.Draws;
        Assert.Equal(2, draws.Count);
        Assert.Equal(62.5f, draws[0].Vertices.Span[2], 3);
        Assert.Equal(72.5f, draws[1].Vertices.Span[2], 3);
    }

    /// <summary>
    /// Verifies hidden meshes and missing art are skipped without failing.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task BuildAsync_MissingAndHidden_SkipsQuietlyAsync()
    {
        var hidden = CreateModel().Meshes[0] with { Attributes = 0x1000 };
        var model = CreateModel() with { Meshes = [hidden] };
        var service = new WbModelRenderService(
            new StubLoader(model),
            new StubThingCatalog(),
            new StubTextureCache(),
            NullLogger<WbModelRenderService>.Instance);
        var map = new WorldBuilderMap();
        map.Objects.Add(new MapObjectEntry { X = 0, Y = 0, Z = 0, Angle = 0, Name = "Tank" });
        map.Objects.Add(new MapObjectEntry { X = 0, Y = 0, Z = 0, Angle = 0, Name = "Missing" });

        var result = await service.BuildAsync(map, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data!.Draws);
    }

    private static W3DModel CreateModel()
    {
        var white = new W3dRgba(255, 255, 255, 255);
        var material = new W3dVertexMaterial("M", 0, white, white, white, white, 1, 1, 0);
        var pass = new W3dMaterialPass(
            [0],
            [0],
            [white, white, white],
            [new W3dTextureStage([0], [new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1)])]);
        var mesh = new W3dMesh(
            "TURRET",
            "TANK",
            0,
            [new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0)],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [new W3dTriangle(0, 1, 2, 0, Vector3.UnitZ, 0)],
            [material],
            [new W3dShader(3, 1, 0, 0, 0, 1, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0)],
            ["Tank.tga"],
            [pass]);
        return new W3DModel("TANK.w3d", [mesh], [], []);
    }

    private sealed class StubLoader(W3DModel? model) : IW3DAssetLoader
    {
        public Task<OperationResult<W3DModel>> LoadAsync(string fileName, CancellationToken cancellationToken = default)
        {
            if (model == null || fileName.StartsWith("MISSING", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(OperationResult<W3DModel>.CreateFailure("Missing."));
            }

            return Task.FromResult(OperationResult<W3DModel>.CreateSuccess(model));
        }
    }

    private sealed class StubThingCatalog : IThingTemplateCatalog
    {
        private static readonly ThingTemplateInfo Tank = new(
            "Tank", "Object", null, null, "USA", string.Empty, [], null, null, null, null, null, null, null, [], "TANK.TURRET", 1.0f);

        public IReadOnlyList<ThingTemplateInfo> GetAll()
        {
            return [Tank];
        }

        public ThingTemplateInfo? FindByName(string name)
        {
            return string.Equals(name, "Tank", StringComparison.OrdinalIgnoreCase) ? Tank : null;
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

    private sealed class StubTextureCache : ITextureCache
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
