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
/// Builds translucent standing-water quads: one per cell at or below the
/// water level, plus triangle fans over water-area polygons and one-cell
/// ribbons along river polylines. River width is an editor approximation;
/// the engine draws trapezoid water there.
/// </summary>
public static class WbWaterService
{
    private const float WaterRed = 29.0f / 255.0f;
    private const float WaterGreen = 78.0f / 255.0f;
    private const float WaterBlue = 137.0f / 255.0f;
    private const float WaterAlpha = 0.5f;

    /// <summary>
    /// Builds water quads for a map at the given water level.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="waterLevel">The water level in height units.</param>
    /// <returns>The water render data.</returns>
    public static WbWaterData Build(WorldBuilderMap map, int waterLevel)
    {
        ArgumentNullException.ThrowIfNull(map);
        var terrain = map.Terrain;
        var levelFeet = waterLevel * WorldBuilderConstants.Terrain.HeightScale;
        var vertices = new List<float>();
        var indices = new List<uint>();
        BuildStandingWater(terrain, waterLevel, levelFeet, vertices, indices);
        foreach (var trigger in map.Triggers)
        {
            if (trigger.IsRiver)
            {
                BuildRiver(trigger, levelFeet, vertices, indices);
            }
            else if (trigger.IsWaterArea)
            {
                BuildArea(trigger, levelFeet, vertices, indices);
            }
        }

        return new WbWaterData(vertices.ToArray(), indices.ToArray());
    }

    private static void BuildStandingWater(
        MapTerrainData terrain,
        int waterLevel,
        float levelFeet,
        List<float> vertices,
        List<uint> indices)
    {
        if (terrain.Width <= 0 || terrain.Height <= 0 || terrain.Heights.Count == 0)
        {
            return;
        }

        for (var y = 0; y < terrain.Height - 1; y++)
        {
            for (var x = 0; x < terrain.Width - 1; x++)
            {
                if (!IsSubmerged(terrain, waterLevel, x, y))
                {
                    continue;
                }

                AddQuad(
                    vertices,
                    indices,
                    WorldX(terrain.BorderSize, x),
                    WorldY(terrain.BorderSize, y),
                    WorldX(terrain.BorderSize, x + 1),
                    WorldY(terrain.BorderSize, y + 1),
                    levelFeet);
            }
        }
    }

    private static bool IsSubmerged(MapTerrainData terrain, int waterLevel, int x, int y)
    {
        var index = (y * terrain.Width) + x;
        return index < terrain.Heights.Count && terrain.Heights[index] <= waterLevel;
    }

    private static void BuildArea(
        MapTrigger trigger,
        float levelFeet,
        List<float> vertices,
        List<uint> indices)
    {
        if (trigger.Points.Count < 3)
        {
            return;
        }

        var baseIndex = (uint)(vertices.Count / WbWaterData.StrideFloats);
        foreach (var point in trigger.Points)
        {
            AddVertex(vertices, point.X, point.Y, levelFeet);
        }

        for (var i = 1; i + 1 < trigger.Points.Count; i++)
        {
            indices.Add(baseIndex);
            indices.Add(baseIndex + (uint)i);
            indices.Add(baseIndex + (uint)(i + 1));
        }
    }

    private static void BuildRiver(
        MapTrigger trigger,
        float levelFeet,
        List<float> vertices,
        List<uint> indices)
    {
        if (trigger.Points.Count < 2)
        {
            return;
        }

        var halfWidth = WorldBuilderConstants.Terrain.CellSize / 2.0f;
        for (var i = 0; i + 1 < trigger.Points.Count; i++)
        {
            var (x1, y1) = ((float)trigger.Points[i].X, (float)trigger.Points[i].Y);
            var (x2, y2) = ((float)trigger.Points[i + 1].X, (float)trigger.Points[i + 1].Y);
            var dx = x2 - x1;
            var dy = y2 - y1;
            var length = MathF.Sqrt((dx * dx) + (dy * dy));
            if (length <= float.Epsilon)
            {
                continue;
            }

            var nx = (-dy / length) * halfWidth;
            var ny = (dx / length) * halfWidth;
            AddQuad(
                vertices,
                indices,
                new Vector2(x1 + nx, y1 + ny),
                new Vector2(x1 - nx, y1 - ny),
                new Vector2(x2 + nx, y2 + ny),
                new Vector2(x2 - nx, y2 - ny),
                levelFeet);
        }
    }

    private static void AddQuad(
        List<float> vertices,
        List<uint> indices,
        float minX,
        float minY,
        float maxX,
        float maxY,
        float z)
    {
        AddQuad(
            vertices,
            indices,
            new Vector2(minX, minY),
            new Vector2(maxX, minY),
            new Vector2(minX, maxY),
            new Vector2(maxX, maxY),
            z);
    }

    private static void AddQuad(
        List<float> vertices,
        List<uint> indices,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Vector2 d,
        float z)
    {
        var baseIndex = (uint)(vertices.Count / WbWaterData.StrideFloats);
        AddVertex(vertices, a.X, a.Y, z);
        AddVertex(vertices, b.X, b.Y, z);
        AddVertex(vertices, c.X, c.Y, z);
        AddVertex(vertices, d.X, d.Y, z);
        indices.Add(baseIndex);
        indices.Add(baseIndex + 1);
        indices.Add(baseIndex + 2);
        indices.Add(baseIndex + 1);
        indices.Add(baseIndex + 3);
        indices.Add(baseIndex + 2);
    }

    private static void AddVertex(List<float> vertices, float x, float y, float z)
    {
        vertices.Add(x);
        vertices.Add(y);
        vertices.Add(z);
        vertices.Add(WaterRed);
        vertices.Add(WaterGreen);
        vertices.Add(WaterBlue);
        vertices.Add(WaterAlpha);
    }

    private static float WorldX(int borderSize, int cellX)
    {
        return (cellX - borderSize) * WorldBuilderConstants.Terrain.CellSize;
    }

    private static float WorldY(int borderSize, int cellY)
    {
        return (cellY - borderSize) * WorldBuilderConstants.Terrain.CellSize;
    }
}
