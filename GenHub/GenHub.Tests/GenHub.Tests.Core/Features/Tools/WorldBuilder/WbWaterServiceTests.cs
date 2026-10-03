// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Rendering;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for the standing-water quad builder.
/// </summary>
public sealed class WbWaterServiceTests
{
    /// <summary>
    /// Verifies submerged cells emit quads at the water level.
    /// </summary>
    [Fact]
    public void Build_SubmergedCells_EmitsQuadsAtLevel()
    {
        var map = CreateMap([5, 5, 5, 20]);

        var water = WbWaterService.Build(map, 10);

        Assert.Equal(4 * WbWaterData.StrideFloats, water.Vertices.Length);
        Assert.Equal(6, water.Indices.Length);
        Assert.Equal(10 * (10.0f / 16.0f), water.Vertices.Span[2]);
        Assert.Equal(0.5f, water.Vertices.Span[6]);
    }

    /// <summary>
    /// Verifies dry maps emit nothing.
    /// </summary>
    [Fact]
    public void Build_DryMap_EmitsNothing()
    {
        var water = WbWaterService.Build(CreateMap([20, 20, 20, 20]), 10);

        Assert.Equal(0, water.Vertices.Length);
        Assert.Equal(0, water.Indices.Length);
    }

    /// <summary>
    /// Verifies water-area polygons triangulate as fans.
    /// </summary>
    [Fact]
    public void Build_WaterArea_FansPolygon()
    {
        var map = CreateMap([20, 20, 20, 20]);
        var trigger = new MapTrigger { Name = "Water", IsWaterArea = true };
        trigger.Points.Add((0, 0, 0));
        trigger.Points.Add((20, 0, 0));
        trigger.Points.Add((20, 20, 0));
        trigger.Points.Add((0, 20, 0));
        map.Triggers.Add(trigger);

        var water = WbWaterService.Build(map, 10);

        Assert.Equal(4 * WbWaterData.StrideFloats, water.Vertices.Length);
        Assert.Equal(6, water.Indices.Length);
    }

    /// <summary>
    /// Verifies rivers ribbon along their polylines.
    /// </summary>
    [Fact]
    public void Build_River_RibbonsPolyline()
    {
        var map = CreateMap([20, 20, 20, 20]);
        var trigger = new MapTrigger { Name = "River", IsRiver = true };
        trigger.Points.Add((0, 0, 0));
        trigger.Points.Add((0, 40, 0));
        map.Triggers.Add(trigger);

        var water = WbWaterService.Build(map, 10);

        Assert.Equal(4 * WbWaterData.StrideFloats, water.Vertices.Length);
        Assert.Equal(10.0f, water.Vertices.Span[7] - water.Vertices.Span[0]);
    }

    private static WorldBuilderMap CreateMap(byte[] heights)
    {
        var map = new WorldBuilderMap();
        map.Terrain.Width = 2;
        map.Terrain.Height = 2;
        map.Terrain.BorderSize = 0;
        map.Terrain.Heights = heights;
        return map;
    }
}
