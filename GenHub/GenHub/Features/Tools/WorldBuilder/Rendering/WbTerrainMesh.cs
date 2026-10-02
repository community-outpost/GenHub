// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// CPU terrain mesh builder. Ports HeightMapRenderObjClass::updateVB: one
/// quad per cell with border-relative world positions, neighbor-delta corner
/// normals, baked doTheLight vertex colors, base-tile UVs, blend-tile UVs,
/// and per-corner blend alpha. Cells needing a flipped diagonal swap the
/// split. Three-way extra blends emit a second mesh drawn as its own pass
/// like renderExtraBlendTiles.
/// </summary>
public static class WbTerrainMesh
{
    private sealed record CellUvs(float[] U1, float[] V1, float[] U2, float[] V2, byte[] Alpha);

    private sealed record MeshLight(Vector3 Ambient, IReadOnlyList<Vector3> Directions, IReadOnlyList<Vector3> Diffuse);

    /// <summary>
    /// Floats per interleaved vertex: XYZ, RGB, UV1, UV2, alpha, extra alpha.
    /// </summary>
    public const int StrideFloats = 12;

    /// <summary>
    /// Builds the interleaved vertex and index arrays for a terrain.
    /// </summary>
    /// <param name="terrain">The terrain data.</param>
    /// <param name="atlas">The tile atlas layout.</param>
    /// <param name="ambient">The global ambient color.</param>
    /// <param name="lightDirections">Directions toward each global light.</param>
    /// <param name="lightDiffuse">Diffuse color per global light.</param>
    /// <returns>The primary and extra-blend meshes.</returns>
    public static (float[] Vertices, uint[] Indices, float[] ExtraVertices, uint[] ExtraIndices) Build(
        MapTerrainData terrain,
        WbTileAtlas atlas,
        Vector3 ambient,
        IReadOnlyList<Vector3> lightDirections,
        IReadOnlyList<Vector3> lightDiffuse)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(atlas);
        var vertices = new List<float>(terrain.Width * terrain.Height * 4 * StrideFloats);
        var indices = new List<uint>(terrain.Width * terrain.Height * 6);
        var extraVertices = new List<float>();
        var extraIndices = new List<uint>();
        var uvs = new CellUvs(new float[4], new float[4], new float[4], new float[4], new byte[4]);
        var light = new MeshLight(ambient, lightDirections, lightDiffuse);
        for (var y = 0; y < terrain.Height; y++)
        {
            for (var x = 0; x < terrain.Width; x++)
            {
                var baseVertex = (uint)(vertices.Count / StrideFloats);
                WbTerrainUv.GetCellUv(terrain, atlas, x, y, uvs.U1, uvs.V1);
                var flip = WbTerrainUv.GetCellAlpha(terrain, atlas, x, y, uvs.U2, uvs.V2, uvs.Alpha);
                for (var corner = 0; corner < 4; corner++)
                {
                    AppendVertex(vertices, terrain, x, y, corner, uvs, light);
                }

                if (flip)
                {
                    indices.Add(baseVertex + 1);
                    indices.Add(baseVertex + 2);
                    indices.Add(baseVertex + 3);
                    indices.Add(baseVertex + 1);
                    indices.Add(baseVertex + 3);
                    indices.Add(baseVertex);
                }
                else
                {
                    indices.Add(baseVertex);
                    indices.Add(baseVertex + 1);
                    indices.Add(baseVertex + 2);
                    indices.Add(baseVertex);
                    indices.Add(baseVertex + 2);
                    indices.Add(baseVertex + 3);
                }

                AppendExtraBlend(extraVertices, extraIndices, terrain, atlas, x, y, light);
            }
        }

        return ([.. vertices], [.. indices], [.. extraVertices], [.. extraIndices]);
    }

    private static void AppendVertex(
        List<float> vertices,
        MapTerrainData terrain,
        int x,
        int y,
        int corner,
        CellUvs uvs,
        MeshLight light)
    {
        var cx = x + (corner == 1 || corner == 2 ? 1 : 0);
        var cy = y + (corner >= 2 ? 1 : 0);
        var worldX = (cx - terrain.BorderSize) * WorldBuilderConstants.Terrain.CellSize;
        var worldY = (cy - terrain.BorderSize) * WorldBuilderConstants.Terrain.CellSize;
        var worldZ = SampleHeightFeet(terrain, cx, cy);
        var normal = WbTerrainLighting.CornerNormal(
            SampleHeightFeet(terrain, cx - 1, cy),
            SampleHeightFeet(terrain, cx + 1, cy),
            SampleHeightFeet(terrain, cx, cy - 1),
            SampleHeightFeet(terrain, cx, cy + 1));
        var color = WbTerrainLighting.LightVertex(normal, light.Ambient, light.Directions, light.Diffuse);
        vertices.Add(worldX);
        vertices.Add(worldY);
        vertices.Add(worldZ);
        vertices.Add(color.X);
        vertices.Add(color.Y);
        vertices.Add(color.Z);
        vertices.Add(uvs.U1[corner]);
        vertices.Add(uvs.V1[corner]);
        vertices.Add(uvs.U2[corner]);
        vertices.Add(uvs.V2[corner]);
        vertices.Add(uvs.Alpha[corner] / 255.0f);
        vertices.Add(0.0f);
    }

    private static void AppendExtraBlend(
        List<float> extraVertices,
        List<uint> extraIndices,
        MapTerrainData terrain,
        WbTileAtlas atlas,
        int x,
        int y,
        MeshLight light)
    {
        var index = (y * terrain.Width) + x;
        var extraIndex = terrain.ExtraBlendTileIndices[index];
        if (extraIndex <= 0 || extraIndex >= terrain.BlendTiles.Count)
        {
            return;
        }

        var record = terrain.BlendTiles[extraIndex];
        var u = new float[4];
        var v = new float[4];
        if (!WbTerrainUv.GetTileUv(atlas, record.BlendIndex, out var minU, out var minV, out var maxU, out var maxV))
        {
            return;
        }

        u[0] = minU;
        u[1] = maxU;
        u[2] = maxU;
        u[3] = minU;
        v[0] = maxV;
        v[1] = maxV;
        v[2] = minV;
        v[3] = minV;
        var uvs = new CellUvs(u, v, u, v, [255, 255, 255, 255]);
        var baseVertex = (uint)(extraVertices.Count / StrideFloats);
        for (var corner = 0; corner < 4; corner++)
        {
            AppendVertex(extraVertices, terrain, x, y, corner, uvs, light);
        }

        extraIndices.Add(baseVertex);
        extraIndices.Add(baseVertex + 1);
        extraIndices.Add(baseVertex + 2);
        extraIndices.Add(baseVertex);
        extraIndices.Add(baseVertex + 2);
        extraIndices.Add(baseVertex + 3);
    }

    private static float SampleHeightFeet(MapTerrainData terrain, int x, int y)
    {
        var clampedX = Math.Clamp(x, 0, terrain.Width - 1);
        var clampedY = Math.Clamp(y, 0, terrain.Height - 1);
        var index = (clampedY * terrain.Width) + clampedX;
        return index < terrain.Heights.Count
            ? terrain.Heights[index] * WorldBuilderConstants.Terrain.HeightScale
            : 0.0f;
    }
}
