// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Rendering;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for the 3D scene overlay lines.
/// </summary>
public sealed class WbSceneOverlayServiceTests
{
    /// <summary>
    /// Verifies waypoint links connect resolved waypoint objects.
    /// </summary>
    [Fact]
    public void Build_WaypointLink_ConnectsWaypoints()
    {
        var map = CreateMap();
        map.Objects.Add(Waypoint(1, 10, 20));
        map.Objects.Add(Waypoint(2, 30, 40));
        map.WaypointLinks.Add(new MapWaypointLink(1, 2));

        var lines = WbSceneOverlayService.Build(map, MapCanvasLayers.Waypoints);

        Assert.Equal(2 * WbOverlayLines.StrideFloats, lines.Vertices.Length);
        Assert.Equal(10.0f, lines.Vertices.Span[0]);
        Assert.Equal(20.0f, lines.Vertices.Span[1]);
        Assert.Equal(30.0f, lines.Vertices.Span[WbOverlayLines.StrideFloats]);
        Assert.Equal(1.0f, lines.Vertices.Span[3]);
        Assert.Equal(1.0f, lines.Vertices.Span[4]);
        Assert.Equal(0.0f, lines.Vertices.Span[5]);
    }

    /// <summary>
    /// Verifies dangling links are skipped.
    /// </summary>
    [Fact]
    public void Build_DanglingLink_SkipsQuietly()
    {
        var map = CreateMap();
        map.Objects.Add(Waypoint(1, 10, 20));
        map.WaypointLinks.Add(new MapWaypointLink(1, 99));

        var lines = WbSceneOverlayService.Build(map, MapCanvasLayers.Waypoints);

        Assert.Equal(0, lines.Vertices.Length);
    }

    /// <summary>
    /// Verifies the playable boundary draws as a gold rectangle.
    /// </summary>
    [Fact]
    public void Build_Boundary_DrawsRectangle()
    {
        var map = CreateMap();
        map.Terrain.Boundaries.Add(new MapBoundary(2, 2));

        var lines = WbSceneOverlayService.Build(map, MapCanvasLayers.Boundary);

        Assert.Equal(8 * WbOverlayLines.StrideFloats, lines.Vertices.Length);
        Assert.Equal(0xE0 / 255.0f, lines.Vertices.Span[3]);
    }

    /// <summary>
    /// Verifies trigger polygons close their loops.
    /// </summary>
    [Fact]
    public void Build_Trigger_ClosesPolygon()
    {
        var map = CreateMap();
        var trigger = new MapTrigger { Name = "T" };
        trigger.Points.Add((0, 0, 0));
        trigger.Points.Add((10, 0, 0));
        trigger.Points.Add((10, 10, 0));
        map.Triggers.Add(trigger);

        var lines = WbSceneOverlayService.Build(map, MapCanvasLayers.Triggers);

        Assert.Equal(6 * WbOverlayLines.StrideFloats, lines.Vertices.Length);
        Assert.Equal(1.0f, lines.Vertices.Span[3]);
        Assert.Equal(0.0f, lines.Vertices.Span[4]);
    }

    /// <summary>
    /// Verifies disabled layers emit nothing.
    /// </summary>
    [Fact]
    public void Build_NoLayers_EmitsNothing()
    {
        var map = CreateMap();
        map.Objects.Add(Waypoint(1, 10, 20));

        Assert.Equal(0, WbSceneOverlayService.Build(map, MapCanvasLayers.None).Vertices.Length);
    }

    private static WorldBuilderMap CreateMap()
    {
        var map = new WorldBuilderMap();
        map.Terrain.Width = 4;
        map.Terrain.Height = 4;
        map.Terrain.BorderSize = 0;
        map.Terrain.Heights = new byte[16];
        return map;
    }

    private static MapObjectEntry Waypoint(int id, float x, float y)
    {
        var entry = new MapObjectEntry { X = x, Y = y, Z = 0, Angle = 0, Name = "WP" };
        entry.Properties.Set(new MapDictValue(
            WorldBuilderConstants.DictKeys.WaypointId,
            WorldBuilderConstants.DictValueType.Int,
            IntValue: id));
        return entry;
    }
}
