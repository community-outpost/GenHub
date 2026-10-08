using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Unit tests for road networks, bridge spans, ramps, and boundaries.
/// </summary>
public sealed class WorldBuilderRoadsAndToolsTests
{
    /// <summary>
    /// Tests adding, retrieving, and deleting road segments.
    /// </summary>
    [Fact]
    public void Roads_AddGetDelete_RoundTripsCorrectly()
    {
        // Arrange
        var map = CreateTestMap();

        // Act - Add road
        var pair = MapOverlayTools.AddRoadSegment(map, new RoadSegment("PavedRoad", 10f, 20f, 0f, 50f, 60f, 0f, IsAngled: true, IsTight: false));
        var roads = MapOverlayTools.GetRoadSegments(map);

        // Assert
        pair.Start.Name.Should().Be("PavedRoad");
        pair.End.Name.Should().Be("PavedRoad");
        roads.Should().HaveCount(1);
        roads[0].RoadType.Should().Be("PavedRoad");
        roads[0].IsAngled.Should().BeTrue();
        roads[0].IsTight.Should().BeFalse();
        roads[0].X1.Should().Be(10f);
        roads[0].Y1.Should().Be(20f);
        roads[0].X2.Should().Be(50f);
        roads[0].Y2.Should().Be(60f);

        // Act - Delete road
        var deleted = MapOverlayTools.DeleteRoadSegment(map, 0);
        var roadsAfter = MapOverlayTools.GetRoadSegments(map);

        // Assert
        deleted.Should().BeTrue();
        roadsAfter.Should().BeEmpty();
    }

    /// <summary>
    /// Tests adding, retrieving, and deleting bridge segments.
    /// </summary>
    [Fact]
    public void Bridges_AddGetDelete_RoundTripsCorrectly()
    {
        // Arrange
        var map = CreateTestMap();

        // Act - Add bridge
        var pair = MapOverlayTools.AddBridge(map, "BridgeConcrete", 100f, 100f, 200f, 100f);
        var bridges = MapOverlayTools.GetBridges(map);

        // Assert
        pair.Start.Name.Should().Be("BridgeConcrete");
        pair.End.Name.Should().Be("BridgeConcrete");
        bridges.Should().HaveCount(1);
        bridges[0].Template.Should().Be("BridgeConcrete");
        bridges[0].X1.Should().Be(100f);
        bridges[0].X2.Should().Be(200f);

        // Act - Delete bridge
        var deleted = MapOverlayTools.DeleteBridge(map, 0);
        var bridgesAfter = MapOverlayTools.GetBridges(map);

        // Assert
        deleted.Should().BeTrue();
        bridgesAfter.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that ApplyRamp linearly interpolates elevations between two points.
    /// </summary>
    [Fact]
    public void ApplyRamp_InterpolatesElevationsAlongCorridor()
    {
        // Arrange
        var map = CreateTestMap(16, 16);

        // Start height = 10, End height = 90
        map.Terrain.Heights[0] = 10;
        map.Terrain.Heights[(0 * 16) + 10] = 90;

        // Act
        MapTerrainTools.ApplyRamp(map, 0, 0, 10, 0, width: 2);

        // Assert
        map.Terrain.Heights[0].Should().Be(10);
        map.Terrain.Heights[5].Should().BeInRange(45, 55);
        map.Terrain.Heights[10].Should().Be(90);
    }

    /// <summary>
    /// Tests setting playable boundary rectangle on terrain.
    /// </summary>
    [Fact]
    public void SetPlayableBoundary_ConfiguresBoundaries()
    {
        // Arrange
        var map = CreateTestMap(32, 32);

        // Act
        MapTerrainTools.SetPlayableBoundary(map, 4, 4, 28, 28);

        // Assert
        map.Terrain.Boundaries.Should().HaveCount(1);
        map.Terrain.Boundaries[0].X.Should().Be(24);
        map.Terrain.Boundaries[0].Y.Should().Be(24);
        map.Terrain.BorderSize.Should().Be(4);
    }

    /// <summary>
    /// Tests adding water polygon areas.
    /// </summary>
    [Fact]
    public void AddWaterArea_CreatesWaterTriggerPolygon()
    {
        // Arrange
        var map = CreateTestMap(16, 16);
        var points = new (int X, int Y, int Z)[]
        {
            (0, 0, 0),
            (100, 0, 0),
            (100, 100, 0),
            (0, 100, 0),
        };

        // Act
        var trigger = MapOverlayTools.AddWaterArea(map, "LakeSuperior", points, isRiver: false);

        // Assert
        trigger.Name.Should().Be("LakeSuperior");
        trigger.Points.Should().HaveCount(4);
        trigger.IsWaterArea.Should().BeTrue();
        trigger.IsRiver.Should().BeFalse();
    }

    /// <summary>
    /// Tests adding river polygon areas keeps IsWaterArea true.
    /// </summary>
    [Fact]
    public void AddWaterArea_RiverArea_SetsBothWaterAndRiverTrue()
    {
        // Arrange
        var map = CreateTestMap(16, 16);
        var points = new (int X, int Y, int Z)[]
        {
            (0, 0, 0),
            (50, 0, 0),
            (50, 50, 0),
            (0, 50, 0),
        };

        // Act
        var trigger = MapOverlayTools.AddWaterArea(map, "Danube", points, isRiver: true);

        // Assert
        trigger.Name.Should().Be("Danube");
        trigger.IsWaterArea.Should().BeTrue();
        trigger.IsRiver.Should().BeTrue();
    }

    private static WorldBuilderMap CreateTestMap(int width = 16, int height = 16)
    {
        var map = new WorldBuilderMap();
        map.Terrain.Width = width;
        map.Terrain.Height = height;
        map.Terrain.Heights = new byte[width * height];
        map.Terrain.TileIndices = new short[width * height];
        map.Terrain.CliffState = new byte[width * height];
        map.World.Set(new MapDictValue(WorldBuilderConstants.DictKeys.MapName, WorldBuilderConstants.DictValueType.AsciiString, StringValue: "Test"));
        return map;
    }
}
