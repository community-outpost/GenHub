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
        var u1 = new float[4];
        var v1 = new float[4];
        var u2 = new float[4];
        var v2 = new float[4];
        var alpha = new byte[4];
        for (var y = 0; y < terrain.Height; y++)
        {
            for (var x = 0; x < terrain.Width; x++)
            {
                var baseVertex = (uint)(vertices.Count / StrideFloats);
                WbTerrainUv.GetCellUv(terrain, atlas, x, y, u1, v1);
                var flip = WbTerrainUv.GetCellAlpha(terrain, atlas, x, y, u2, v2, alpha);
                for (var corner = 0; corner < 4; corner++)
                {
                    AppendVertex(vertices, terrain, x, y, corner, u1, v1, u2, v2, alpha, ambient, lightDirections, lightDiffuse);
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

                AppendExtraBlend(extraVertices, extraIndices, terrain, atlas, x, y, ambient, lightDirections, lightDiffuse);
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
        float[] u1,
        float[] v1,
        float[] u2,
        float[] v2,
        byte[] alpha,
        Vector3 ambient,
        IReadOnlyList<Vector3> lightDirections,
        IReadOnlyList<Vector3> lightDiffuse)
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
        var color = WbTerrainLighting.LightVertex(normal, ambient, lightDirections, lightDiffuse);
        vertices.Add(worldX);
        vertices.Add(worldY);
        vertices.Add(worldZ);
        vertices.Add(color.X);
        vertices.Add(color.Y);
        vertices.Add(color.Z);
        vertices.Add(u1[corner]);
        vertices.Add(v1[corner]);
        vertices.Add(u2[corner]);
        vertices.Add(v2[corner]);
        vertices.Add(alpha[corner] / 255.0f);
        vertices.Add(0.0f);
    }

    private static void AppendExtraBlend(
        List<float> extraVertices,
        List<uint> extraIndices,
        MapTerrainData terrain,
        WbTileAtlas atlas,
        int x,
        int y,
        Vector3 ambient,
        IReadOnlyList<Vector3> lightDirections,
        IReadOnlyList<Vector3> lightDiffuse)
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
        var alpha = new byte[] { 255, 255, 255, 255 };
        var baseVertex = (uint)(extraVertices.Count / StrideFloats);
        for (var corner = 0; corner < 4; corner++)
        {
            AppendVertex(extraVertices, terrain, x, y, corner, u, v, u, v, alpha, ambient, lightDirections, lightDiffuse);
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
        return index < terrain.Heights.Length
            ? terrain.Heights[index] * WorldBuilderConstants.Terrain.HeightScale
            : 0.0f;
    }
}
