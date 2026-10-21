// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Tessellates road segments into textured quads: straight quad strips with
/// widths from Roads.ini, sampling the central 48 of 128 authored texture
/// pixels across the road surface. Tees, crossings, and curve subdivision
/// from W3DRoadBuffer are deferred; segments render as straight quads.
/// </summary>
/// <param name="roads">The road template catalog.</param>
/// <param name="textures">The texture cache.</param>
/// <param name="logger">The logger.</param>
public sealed class WbRoadService(
    IRoadCatalog roads,
    ITextureCache textures,
    ILogger<WbRoadService> logger)
{
    private const float TextureSurfaceStart = 40.0f / 128.0f;
    private const float TextureSurfaceEnd = 88.0f / 128.0f;
    private const float LiftFeet = 0.3f;

    private static readonly W3dShader OpaqueTexturedShader = new(3, 1, 0, 0, 0, 1, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0);
    private static readonly W3dGlState GrayDecalState = new(W3dDepthFunction.Lequal, true, false, W3dBlendFactor.One, W3dBlendFactor.Zero, false, false, W3dColorCombine.Decal);
    private static readonly Vector3 Gray = new(0.45f, 0.45f, 0.45f);

    private readonly IRoadCatalog _roads = roads;
    private readonly ITextureCache _textures = textures;
    private readonly ILogger<WbRoadService> _logger = logger;

    /// <summary>
    /// Builds textured road draws for every road segment on the map.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The baked draws.</returns>
    public async Task<OperationResult<IReadOnlyList<WbModelDraw>>> BuildRoadsAsync(WorldBuilderMap map, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        var segments = MapOverlayTools.GetRoadSegments(map);
        var terrain = map.Terrain;
        var draws = await Task.Run(() => BuildCoreAsync(terrain, segments, cancellationToken), cancellationToken).ConfigureAwait(false);
        return OperationResult<IReadOnlyList<WbModelDraw>>.CreateSuccess(draws);
    }

    private static void WriteCorner(float[] vertices, int i, Vector3 position, Vector2 uv, Vector3 color)
    {
        var offset = i * WbModelDraw.StrideFloats;
        vertices[offset] = position.X;
        vertices[offset + 1] = position.Y;
        vertices[offset + 2] = position.Z;
        vertices[offset + 3] = 0.0f;
        vertices[offset + 4] = 0.0f;
        vertices[offset + 5] = 1.0f;
        vertices[offset + 6] = uv.X;
        vertices[offset + 7] = 1.0f - uv.Y;
        vertices[offset + 8] = color.X;
        vertices[offset + 9] = color.Y;
        vertices[offset + 10] = color.Z;
        vertices[offset + 11] = 1.0f;
    }

    private async Task<WbModelDraw?> BuildSegmentAsync(MapTerrainData terrain, RoadSegment segment, CancellationToken cancellationToken)
    {
        var info = _roads.FindRoad(segment.RoadType);
        if (info == null)
        {
            _logger.LogDebug("Skipping road with unknown type {Type}", segment.RoadType);
            return null;
        }

        var width = info.RoadWidth is > 0 ? info.RoadWidth.Value : WorldBuilderConstants.Terrain.CellSize;
        var dx = segment.X2 - segment.X1;
        var dy = segment.Y2 - segment.Y1;
        var length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length <= float.Epsilon)
        {
            return null;
        }

        var nx = (-dy / length) * width / 2.0f;
        var ny = (dx / length) * width / 2.0f;
        var z1 = WbPicking.GroundHeightFeet(terrain, segment.X1, segment.Y1) + segment.Z1 + LiftFeet;
        var z2 = WbPicking.GroundHeightFeet(terrain, segment.X2, segment.Y2) + segment.Z2 + LiftFeet;
        var vRepeat = length / width;
        var corners = new Vector3[4]
        {
            new(segment.X1 + nx, segment.Y1 + ny, z1),
            new(segment.X1 - nx, segment.Y1 - ny, z1),
            new(segment.X2 + nx, segment.Y2 + ny, z2),
            new(segment.X2 - nx, segment.Y2 - ny, z2),
        };
        var uvs = new Vector2[4]
        {
            new(TextureSurfaceStart, 0),
            new(TextureSurfaceEnd, 0),
            new(TextureSurfaceStart, vRepeat),
            new(TextureSurfaceEnd, vRepeat),
        };

        DecodedTexture? texture = null;
        var state = GrayDecalState;
        var color = Gray;
        if (!string.IsNullOrWhiteSpace(info.Texture))
        {
            var resolved = await _textures.GetAsync(info.Texture, cancellationToken).ConfigureAwait(false);
            if (resolved.Success && resolved.Data != null)
            {
                texture = resolved.Data;
                state = W3dShaderMap.Map(OpaqueTexturedShader);
                color = Vector3.One;
            }
        }

        var vertices = new float[4 * WbModelDraw.StrideFloats];
        for (var i = 0; i < 4; i++)
        {
            WriteCorner(vertices, i, corners[i], uvs[i], color);
        }

        return new WbModelDraw(
            vertices,
            new uint[] { 0, 1, 2, 1, 3, 2 },
            info.Texture,
            texture,
            state,
            true);
    }

    private async Task<List<WbModelDraw>> BuildCoreAsync(MapTerrainData terrain, List<RoadSegment> segments, CancellationToken cancellationToken)
    {
        var draws = new List<WbModelDraw>();
        foreach (var segment in segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var draw = await BuildSegmentAsync(terrain, segment, cancellationToken).ConfigureAwait(false);
            if (draw != null)
            {
                draws.Add(draw);
            }
        }

        return draws;
    }
}
