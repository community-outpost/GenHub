// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using System;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Degraded top-down CPU preview used only when no GPU context is available
/// (macOS v1, software-only hosts). Deliberately honest: heightfield slope
/// shading from real heights, water where cells sit at or below the water
/// table, the grid, the playable boundary, and one dot per object. It never
/// guesses terrain colors from texture names and never draws procedural
/// buildings, trees, or units.
/// </summary>
public static class WbFallbackPreview
{
    private const int GridColor = unchecked((int)0xFF3A3F4A);
    private const int WaterColor = unchecked((int)0xFF1D4E89);
    private const int BoundaryColor = unchecked((int)0xFFE0B23C);
    private const int ObjectDotColor = unchecked((int)0xFFF2F2F2);

    /// <summary>
    /// Renders the map top-down at one pixel per cell in BGRA8888 order.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="options">The render options.</param>
    /// <returns>The pixel buffer, width, and height.</returns>
    public static (int[] Pixels, int Width, int Height) RenderTopDown(WorldBuilderMap map, MapCanvasRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(options);
        var terrain = map.Terrain;
        var width = Math.Max(1, terrain.Width);
        var height = Math.Max(1, terrain.Height);
        var pixels = new int[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = CellColor(terrain, x, y, options);
            }
        }

        DrawBoundary(pixels, width, height, terrain);
        DrawObjectDots(pixels, width, height, map, options);
        return (pixels, width, height);
    }

    private static int CellColor(MapTerrainData terrain, int x, int y, MapCanvasRenderOptions options)
    {
        if (options.GridStep > 0 && (x % options.GridStep == 0 || y % options.GridStep == 0) &&
            options.Layers.HasFlag(MapCanvasLayers.Grid))
        {
            return GridColor;
        }

        var height = SampleHeightByte(terrain, x, y);
        if (options.Layers.HasFlag(MapCanvasLayers.Water) && height <= options.WaterLevel)
        {
            return WaterColor;
        }

        var slope = SlopeMagnitude(terrain, x, y);
        var shade = Math.Clamp(1.0f - (slope * 0.12f), 0.35f, 1.0f);
        var base_ = 0.16f + (0.5f * height / WorldBuilderConstants.Terrain.MaxHeight);
        var level = (int)(255.0f * Math.Clamp(base_ * shade, 0.0f, 1.0f));
        return Pack(level, level, level);
    }

    private static float SlopeMagnitude(MapTerrainData terrain, int x, int y)
    {
        var dx = SampleHeightByte(terrain, x + 1, y) - SampleHeightByte(terrain, x - 1, y);
        var dy = SampleHeightByte(terrain, x, y + 1) - SampleHeightByte(terrain, x, y - 1);
        return MathF.Sqrt((dx * dx) + (dy * dy)) / 2.0f;
    }

    private static int SampleHeightByte(MapTerrainData terrain, int x, int y)
    {
        if (terrain.Width <= 0 || terrain.Height <= 0 || terrain.Heights.Count == 0)
        {
            return 0;
        }

        var clampedX = Math.Clamp(x, 0, terrain.Width - 1);
        var clampedY = Math.Clamp(y, 0, terrain.Height - 1);
        var index = (clampedY * terrain.Width) + clampedX;
        return index < terrain.Heights.Count ? terrain.Heights[index] : 0;
    }

    private static void DrawBoundary(int[] pixels, int width, int height, MapTerrainData terrain)
    {
        if (terrain.Boundaries.Count == 0)
        {
            return;
        }

        var boundary = terrain.Boundaries[0];
        var min = Math.Max(0, terrain.BorderSize);
        var maxX = Math.Min(width - 1, boundary.X);
        var maxY = Math.Min(height - 1, boundary.Y);
        for (var x = min; x <= maxX; x++)
        {
            pixels[(min * width) + x] = BoundaryColor;
            pixels[(maxY * width) + x] = BoundaryColor;
        }

        for (var y = min; y <= maxY; y++)
        {
            pixels[(y * width) + min] = BoundaryColor;
            pixels[(y * width) + maxX] = BoundaryColor;
        }
    }

    private static void DrawObjectDots(int[] pixels, int width, int height, WorldBuilderMap map, MapCanvasRenderOptions options)
    {
        if (!options.Layers.HasFlag(MapCanvasLayers.Objects))
        {
            return;
        }

        foreach (var entry in map.Objects)
        {
            var cell = WbPicking.WorldToCell(
                new System.Numerics.Vector3(entry.X, entry.Y, 0.0f),
                map.Terrain.BorderSize,
                width,
                height);
            pixels[(cell.Y * width) + cell.X] = ObjectDotColor;
        }
    }

    private static int Pack(int r, int g, int b)
    {
        return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
    }
}
