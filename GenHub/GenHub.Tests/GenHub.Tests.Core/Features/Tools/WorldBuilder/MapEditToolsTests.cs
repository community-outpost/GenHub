using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using System;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for <see cref="MapTerrainTools"/> and <see cref="MapOverlayTools"/>.
/// </summary>
public sealed class MapEditToolsTests
{
    /// <summary>
    /// Tests that the mound raises the center more than the edge.
    /// </summary>
    [Fact]
    public void Mound_RaisesCenterMoreThanEdge()
    {
        var map = CreateMap();

        MapTerrainTools.ApplyMound(map, 5, 5, 3, 20);

        var center = map.Terrain.Heights[(5 * 10) + 5];
        var edge = map.Terrain.Heights[(5 * 10) + 8];
        Assert.True(center > edge);
        Assert.True(edge >= 20);
    }

    /// <summary>
    /// Tests that the mound clamps to the valid height range.
    /// </summary>
    [Fact]
    public void Mound_ClampsHeight()
    {
        var map = CreateMap();

        MapTerrainTools.ApplyMound(map, 5, 5, 2, 500);

        Assert.Equal(255, map.Terrain.Heights[(5 * 10) + 5]);
        Assert.All(map.Terrain.Heights, h => Assert.True(h <= 255));

        MapTerrainTools.ApplyMound(map, 5, 5, 2, -500);
        Assert.Equal(0, map.Terrain.Heights[(5 * 10) + 5]);
        Assert.All(map.Terrain.Heights, h => Assert.True(h >= 0));
    }

    /// <summary>
    /// Tests that smoothing pulls a spike toward its neighbors.
    /// </summary>
    [Fact]
    public void Smooth_ReducesSpike()
    {
        var map = CreateMap();
        map.Terrain.Heights[(5 * 10) + 5] = 200;

        MapTerrainTools.ApplySmooth(map, 5, 5, 1);

        Assert.True(map.Terrain.Heights[(5 * 10) + 5] < 200);
    }

    /// <summary>
    /// Tests that the plateau sets a flat height.
    /// </summary>
    [Fact]
    public void Plateau_SetsFlatHeight()
    {
        var map = CreateMap();

        MapTerrainTools.ApplyPlateau(map, 5, 5, 1, 42);

        Assert.Equal(42, map.Terrain.Heights[(5 * 10) + 5]);
    }

    /// <summary>
    /// Tests that tile painting allocates the texture class and writes
    /// parity-correct sub-tiles instead of raw indices.
    /// </summary>
    [Fact]
    public void PaintTile_AllocatesClassAndSubTiles()
    {
        var map = CreateMap();

        MapTerrainTools.PaintTile(map, 5, 5, 1, new MapTextureClass(0, 16, 4, "Dirt"));

        Assert.Single(map.Terrain.TextureClasses);
        Assert.Equal("Dirt", map.Terrain.TextureClasses[0].Name);
        Assert.Equal(0, map.Terrain.TextureClasses[0].FirstTile);
        Assert.Equal(16, map.Terrain.NumBitmapTiles);
        Assert.Equal(43, map.Terrain.TileIndices[(5 * 10) + 5]);
        Assert.Equal(0, map.Terrain.TileIndices[0]);
    }

    /// <summary>
    /// Tests that the grove scatters the requested objects with radian facings.
    /// </summary>
    [Fact]
    public void Grove_ScattersObjects()
    {
        var map = CreateMap();

        var placed = MapTerrainTools.ApplyGrove(map, new WbRandom(7), 5, 5, 3, "Tree", 5);

        Assert.Equal(5, placed.Count);
        Assert.Equal(5, map.Objects.Count);
        Assert.All(placed, obj => Assert.InRange(obj.Angle, 0.0f, MathF.PI * 2.0f));
    }

    /// <summary>
    /// Tests that placing an object stores a zero ground offset: the stored Z
    /// is height above the terrain surface, resolved at render time.
    /// </summary>
    [Fact]
    public void PlaceObject_StoresZeroGroundOffset()
    {
        var map = CreateMap();

        var obj = MapOverlayTools.PlaceObject(map, "Bunker", 55, 55);

        Assert.Equal(0, obj.Z);
        Assert.Equal("Bunker", obj.Name);
    }

    /// <summary>
    /// Tests that placing a duplicate name gets a unique suffix.
    /// </summary>
    [Fact]
    public void PlaceObject_DuplicateName_GetsSuffix()
    {
        var map = CreateMap();
        MapOverlayTools.PlaceObject(map, "Bunker", 55, 55);

        var second = MapOverlayTools.PlaceObject(map, "Bunker", 65, 65);

        Assert.Equal("Bunker 2", second.Name);
    }

    /// <summary>
    /// Tests waypoint add and link round-trip.
    /// </summary>
    [Fact]
    public void Waypoints_AddAndLink()
    {
        var map = CreateMap();

        var first = MapOverlayTools.AddWaypoint(map, 55, 55);
        var second = MapOverlayTools.AddWaypoint(map, 155, 155);
        var firstId = first.Properties.GetInt("waypointID", 0);
        var secondId = second.Properties.GetInt("waypointID", 0);

        Assert.True(firstId != secondId);
        Assert.True(MapOverlayTools.LinkWaypoints(map, firstId, secondId));
        Assert.False(MapOverlayTools.LinkWaypoints(map, firstId, secondId));
        Assert.True(MapOverlayTools.UnlinkWaypoints(map, firstId, secondId));
    }

    /// <summary>
    /// Tests trigger add and delete round-trip.
    /// </summary>
    [Fact]
    public void Triggers_AddAndDelete()
    {
        var map = CreateMap();

        var trigger = MapOverlayTools.AddTrigger(map, "Area", [(1, 1, 0), (5, 1, 0), (5, 5, 0)]);

        Assert.Single(map.Triggers);
        Assert.True(MapOverlayTools.DeleteTrigger(map, trigger.Id));
        Assert.Empty(map.Triggers);
    }

    /// <summary>
    /// Tests that smoothing feathers edges by the radial falloff.
    /// </summary>
    [Fact]
    public void Smooth_FeathersEdges()
    {
        var map = CreateMap();
        map.Terrain.Heights[(5 * 10) + 5] = 200;

        MapTerrainTools.ApplySmooth(map, 5, 5, 3);

        Assert.True(map.Terrain.Heights[(5 * 10) + 5] < 200);
        Assert.Equal(20, map.Terrain.Heights[(5 * 10) + 8]);
    }

    /// <summary>
    /// Tests that the plateau eases toward the target with feathered edges.
    /// </summary>
    [Fact]
    public void Plateau_FeathersEdges()
    {
        var map = CreateMap();

        MapTerrainTools.ApplyPlateau(map, 5, 5, 3, 42);

        Assert.Equal(42, map.Terrain.Heights[(5 * 10) + 5]);
        var middle = map.Terrain.Heights[(5 * 10) + 6];
        var edge = map.Terrain.Heights[(5 * 10) + 8];
        Assert.InRange(middle, 30, 40);
        Assert.InRange(edge, 21, 30);
        Assert.True(middle > edge);
    }

    /// <summary>
    /// Tests that flood fill paints the connected region and stops at barriers.
    /// </summary>
    [Fact]
    public void FloodFill_FillsRegionStopsAtBarrier()
    {
        var map = CreateMap();
        var dirt = map.Terrain.TextureClasses.Count;
        MapTerrainTools.EnsureTextureClass(map.Terrain, new MapTextureClass(0, 16, 4, "Dirt"));
        MapTerrainTools.EnsureTextureClass(map.Terrain, new MapTextureClass(16, 16, 4, "Grass"));
        for (var y = 0; y < 10; y++)
        {
            for (var x = 0; x < 10; x++)
            {
                MapTerrainTools.SetTileNdx(map.Terrain, x, y, x < 5 ? dirt : dirt + 1);
            }
        }

        var filled = MapTerrainTools.ApplyFloodFill(map, 0, 0, new MapTextureClass(16, 16, 4, "Grass"));

        Assert.Equal(50, filled);
        Assert.Equal(dirt + 1, MapTerrainTools.GetTextureClass(map.Terrain, 0, 0, true));
        Assert.Equal(dirt + 1, MapTerrainTools.GetTextureClass(map.Terrain, 9, 9, true));
    }

    /// <summary>
    /// Tests that filling with the region's own class is a no-op.
    /// </summary>
    [Fact]
    public void FloodFill_SameClass_Noop()
    {
        var map = CreateMap();
        MapTerrainTools.EnsureTextureClass(map.Terrain, new MapTextureClass(0, 16, 4, "Dirt"));
        MapTerrainTools.SetTileNdx(map.Terrain, 5, 5, 0);

        Assert.Equal(0, MapTerrainTools.ApplyFloodFill(map, 5, 5, new MapTextureClass(0, 16, 4, "Dirt")));
    }

    /// <summary>
    /// Tests trigger polygon vertex move, insert, and delete.
    /// </summary>
    [Fact]
    public void TriggerPoints_MoveInsertDelete()
    {
        var map = CreateMap();
        var trigger = MapOverlayTools.AddTrigger(map, "Area", [(0, 0, 0), (10, 0, 0), (10, 10, 0)]);

        Assert.True(MapOverlayTools.MoveTriggerPoint(map, trigger.Id, 1, 20, 0));
        Assert.Equal((20, 0, 0), trigger.Points[1]);
        Assert.True(MapOverlayTools.InsertTriggerPoint(map, trigger.Id, 1, 5, 5));
        Assert.Equal(4, trigger.Points.Count);
        Assert.Equal((5, 5, 0), trigger.Points[1]);
        Assert.True(MapOverlayTools.DeleteTriggerPoint(map, trigger.Id, 1));
        Assert.Equal(3, trigger.Points.Count);
        Assert.False(MapOverlayTools.MoveTriggerPoint(map, 9999, 0, 0, 0));
        Assert.False(MapOverlayTools.DeleteTriggerPoint(map, trigger.Id, 99));
    }

    /// <summary>
    /// Tests that the fence tool places evenly spaced posts along the line.
    /// </summary>
    [Fact]
    public void Fence_PlacesSpacedPosts()
    {
        var map = CreateMap();

        var posts = MapOverlayTools.ApplyFence(map, "Post", 0, 0, 100, 0, 25);

        Assert.Equal(5, posts.Count);
        Assert.Equal(0.0f, posts[0].X);
        Assert.Equal(50.0f, posts[2].X);
        Assert.Equal(100.0f, posts[4].X);
        Assert.Equal(0.0f, posts[0].Angle);
    }

    /// <summary>
    /// Tests that new segments snap to nearby network endpoints.
    /// </summary>
    [Fact]
    public void SnapRoadPoint_NearEndpoint_JoinsNetwork()
    {
        var map = CreateMap();
        MapOverlayTools.AddRoadSegment(map, new RoadSegment("Paved", 0, 0, 0, 100, 0, 0, false, false));

        var joined = MapOverlayTools.SnapRoadPoint(map, 102, 3, 10);
        var far = MapOverlayTools.SnapRoadPoint(map, 500, 500, 10);

        Assert.Equal((100.0f, 0.0f), joined);
        Assert.Equal((500.0f, 500.0f), far);
    }

    private static WorldBuilderMap CreateMap()
    {
        var map = new WorldBuilderMap();
        map.Terrain.Width = 10;
        map.Terrain.Height = 10;
        var heights = new byte[100];
        Array.Fill(heights, (byte)20);
        map.Terrain.Heights = heights;
        map.Terrain.TileIndices = new short[100];
        map.Terrain.BlendTileIndices = new short[100];
        map.Terrain.ExtraBlendTileIndices = new short[100];
        map.Terrain.CliffInfoIndices = new short[100];
        map.Terrain.CliffState = new byte[100];
        return map;
    }
}
