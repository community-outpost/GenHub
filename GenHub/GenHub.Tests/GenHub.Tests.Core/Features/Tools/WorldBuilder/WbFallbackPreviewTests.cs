// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Rendering;
using System;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for <see cref="WbFallbackPreview"/>: water, grid, boundary, and dots.
/// </summary>
public sealed class WbFallbackPreviewTests
{
    /// <summary>
    /// Verifies cells at or below the water table render as water.
    /// </summary>
    [Fact]
    public void RenderTopDown_BelowWaterLevel_DrawsWater()
    {
        var map = CreateMap();
        map.Terrain.Heights[(5 * 10) + 5] = 5;
        var options = new MapCanvasRenderOptions { WaterLevel = 10, GridStep = 0 };

        var (pixels, width, height) = WbFallbackPreview.RenderTopDown(map, options);

        Assert.Equal(10, width);
        Assert.Equal(10, height);
        Assert.Equal(unchecked((int)0xFF1D4E89), pixels[(5 * 10) + 5]);
        Assert.NotEqual(unchecked((int)0xFF1D4E89), pixels[0]);
    }

    /// <summary>
    /// Verifies grid lines draw on the grid step.
    /// </summary>
    [Fact]
    public void RenderTopDown_GridStep_DrawsGrid()
    {
        var map = CreateMap();
        var options = new MapCanvasRenderOptions { WaterLevel = -1, GridStep = 5 };

        var (pixels, _, _) = WbFallbackPreview.RenderTopDown(map, options);

        Assert.Equal(unchecked((int)0xFF3A3F4A), pixels[(3 * 10) + 5]);
        Assert.NotEqual(unchecked((int)0xFF3A3F4A), pixels[(3 * 10) + 3]);
    }

    /// <summary>
    /// Verifies objects render as dots at their cells.
    /// </summary>
    [Fact]
    public void RenderTopDown_Object_DrawsDot()
    {
        var map = CreateMap();
        map.Objects.Add(new MapObjectEntry { X = 54, Y = 54, Name = "Tank" });
        var options = new MapCanvasRenderOptions { WaterLevel = -1, GridStep = 0 };

        var (pixels, _, _) = WbFallbackPreview.RenderTopDown(map, options);

        Assert.Equal(unchecked((int)0xFFF2F2F2), pixels[(5 * 10) + 5]);
    }

    /// <summary>
    /// Verifies layers can be switched off.
    /// </summary>
    [Fact]
    public void RenderTopDown_LayersOff_SkipsFeatures()
    {
        var map = CreateMap();
        map.Terrain.Heights[(5 * 10) + 5] = 5;
        map.Objects.Add(new MapObjectEntry { X = 25, Y = 25, Name = "Tank" });
        var options = new MapCanvasRenderOptions
        {
            WaterLevel = 10,
            GridStep = 5,
            Layers = MapCanvasLayers.Terrain,
        };

        var (pixels, _, _) = WbFallbackPreview.RenderTopDown(map, options);

        Assert.NotEqual(unchecked((int)0xFF1D4E89), pixels[(5 * 10) + 5]);
        Assert.NotEqual(unchecked((int)0xFF3A3F4A), pixels[(3 * 10) + 5]);
        Assert.NotEqual(unchecked((int)0xFFF2F2F2), pixels[(2 * 10) + 2]);
    }

    private static WorldBuilderMap CreateMap()
    {
        var map = new WorldBuilderMap();
        map.Terrain.Width = 10;
        map.Terrain.Height = 10;
        var heights = new byte[100];
        Array.Fill(heights, (byte)20);
        map.Terrain.Heights = heights;
        return map;
    }
}
