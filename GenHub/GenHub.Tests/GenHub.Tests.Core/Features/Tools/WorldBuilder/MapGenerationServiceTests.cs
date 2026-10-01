using FluentAssertions;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Service tests for <see cref="MapGenerationService"/>.
/// </summary>
public sealed class MapGenerationServiceTests
{
    private readonly MapGenerationService sut = new(NullLogger<MapGenerationService>.Instance);

    /// <summary>
    /// Tests that generation is deterministic for a fixed seed.
    /// </summary>
    [Fact]
    public void Generate_SameSeed_ProducesSameHeights()
    {
        // Arrange
        var settings = new MapGenSettings { Seed = 1234, PlayableWidth = 150, PlayableHeight = 150, NumPlayers = 2 };

        // Act
        var first = sut.Generate(settings);
        var second = sut.Generate(settings);

        // Assert
        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue();
        first.Data!.Terrain.Heights.Should().Equal(second.Data!.Terrain.Heights);
    }

    /// <summary>
    /// Tests that starts, supplies, sides, and scripts land correctly.
    /// </summary>
    [Fact]
    public void Generate_TwoPlayers_BuildsPlayableMap()
    {
        // Arrange
        var settings = new MapGenSettings
        {
            Seed = 42,
            PlayableWidth = 150,
            PlayableHeight = 150,
            Border = 10,
            NumPlayers = 2,
        };
        settings.TreeTemplates.Add("Tree1");
        settings.RockTemplates.Add("Rock1");

        // Act
        var result = sut.Generate(settings);

        // Assert
        result.Success.Should().BeTrue();
        var map = result.Data!;
        map.Terrain.Width.Should().Be(170);
        map.Terrain.Height.Should().Be(170);
        map.Terrain.Boundaries.Should().ContainSingle().Which.Should().Be(new MapBoundary(150, 150));
        var starts = map.Objects.Where(o => o.Name.StartsWith("Player_", StringComparison.Ordinal)).ToList();
        starts.Should().HaveCount(2);
        foreach (var start in starts)
        {
            start.Properties.GetString("waypointName").Should().StartWith("Player_");
        }

        map.Objects.Where(o => o.Name == "SupplyDock").Should().HaveCount(4);
        map.Sides.Should().NotBeEmpty();
        map.Sides[0].Properties.GetString("playerName").Should().Be("PlyrCivilian");
        map.Objects.Should().Contain(o => o.Name == "Tree1");
        map.Objects.Should().Contain(o => o.Name == "Rock1");
        var validation = new MapValidationService(NullLogger<MapValidationService>.Instance).Validate(map);
        validation.Success.Should().BeTrue();
    }

    /// <summary>
    /// Tests that tiny requests grow to the minimum playable size.
    /// </summary>
    [Fact]
    public void Generate_TinyMap_GrowsToMinimum()
    {
        // Arrange
        var settings = new MapGenSettings { Seed = 7, PlayableWidth = 40, PlayableHeight = 40, NumPlayers = 2 };

        // Act
        var result = sut.Generate(settings);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Terrain.Width.Should().BeGreaterOrEqualTo(150 + (2 * settings.Border));
    }

    /// <summary>
    /// Tests that different seeds produce different terrain.
    /// </summary>
    [Fact]
    public void Generate_DifferentSeeds_Differ()
    {
        // Arrange
        var first = new MapGenSettings { Seed = 1, PlayableWidth = 150, PlayableHeight = 150 };
        var second = new MapGenSettings { Seed = 2, PlayableWidth = 150, PlayableHeight = 150 };

        // Act
        var a = sut.Generate(first);
        var b = sut.Generate(second);

        // Assert
        a.Data!.Terrain.Heights.Should().NotEqual(b.Data!.Terrain.Heights);
    }

    /// <summary>
    /// Tests that texturing paints cliff and ground classes across every cell.
    /// </summary>
    [Fact]
    public void Generate_Textures_PaintsCliffAndGround()
    {
        // Arrange
        var settings = new MapGenSettings { Seed = 42, PlayableWidth = 150, PlayableHeight = 150, NumPlayers = 2 };

        // Act
        var result = sut.Generate(settings);

        // Assert
        result.Success.Should().BeTrue();
        var map = result.Data!;
        map.Terrain.TextureClasses.Should().Contain(c => c.Name == "Grass");
        map.Terrain.TextureClasses.Should().Contain(c => c.Name == "Cliff");
        var cliffCells = 0;
        for (var y = 0; y < map.Terrain.Height; y++)
        {
            for (var x = 0; x < map.Terrain.Width; x++)
            {
                var classIndex = MapTerrainTools.GetTextureClass(map.Terrain, x, y, baseClassOnly: true);
                classIndex.Should().BeGreaterOrEqualTo(0);
                var name = map.Terrain.TextureClasses[classIndex].Name;
                name.Should().BeOneOf("Grass", "Cliff");
                if (name == "Cliff")
                {
                    cliffCells++;
                }
            }
        }

        cliffCells.Should().BePositive();
        var validation = new MapValidationService(NullLogger<MapValidationService>.Instance).Validate(map);
        validation.Success.Should().BeTrue();
    }

    /// <summary>
    /// Tests that disabled texturing keeps the single ground class.
    /// </summary>
    [Fact]
    public void Generate_NoTextures_KeepsSingleClass()
    {
        // Arrange
        var settings = new MapGenSettings { Seed = 42, PlayableWidth = 150, PlayableHeight = 150, NumPlayers = 2, DoTextures = false };

        // Act
        var result = sut.Generate(settings);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Terrain.TextureClasses.Should().ContainSingle().Which.Name.Should().Be("Grass");
    }

    /// <summary>
    /// Tests that start-ring roads connect consecutive starts in a loop.
    /// </summary>
    [Fact]
    public void Generate_RoadsBetweenStarts_CreatesRingSegments()
    {
        // Arrange
        var settings = new MapGenSettings { Seed = 42, PlayableWidth = 150, PlayableHeight = 150, NumPlayers = 2, RoadMode = MapGenRoadMode.Starts };

        // Act
        var result = sut.Generate(settings);

        // Assert
        result.Success.Should().BeTrue();
        var segments = MapOverlayTools.GetRoadSegments(result.Data!);
        segments.Should().HaveCount(2);
        segments.Should().OnlyContain(s => s.RoadType == "DirtRoad");
        var validation = new MapValidationService(NullLogger<MapValidationService>.Instance).Validate(result.Data!);
        validation.Success.Should().BeTrue();
    }

    /// <summary>
    /// Tests that via-supply roads route each start through its supply docks.
    /// </summary>
    [Fact]
    public void Generate_RoadsViaSupplies_RoutesThroughDocks()
    {
        // Arrange
        var settings = new MapGenSettings { Seed = 42, PlayableWidth = 150, PlayableHeight = 150, NumPlayers = 2, RoadMode = MapGenRoadMode.Supplies };

        // Act
        var result = sut.Generate(settings);

        // Assert
        result.Success.Should().BeTrue();
        var map = result.Data!;
        var dockCount = map.Objects.Count(o => o.Name == "SupplyDock");
        dockCount.Should().BePositive();
        MapOverlayTools.GetRoadSegments(map).Should().HaveCount(2 + dockCount);
        var validation = new MapValidationService(NullLogger<MapValidationService>.Instance).Validate(map);
        validation.Success.Should().BeTrue();
    }

    /// <summary>
    /// Tests that the full pipeline with textures and roads is seed-reproducible.
    /// </summary>
    [Fact]
    public void Generate_FullMap_Reproducible()
    {
        // Arrange
        var settings = new MapGenSettings { Seed = 99, PlayableWidth = 150, PlayableHeight = 150, NumPlayers = 2, RoadMode = MapGenRoadMode.Supplies };
        settings.TreeTemplates.Add("Tree1");
        settings.RockTemplates.Add("Rock1");

        // Act
        var first = sut.Generate(settings);
        var second = sut.Generate(settings);

        // Assert
        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue();
        first.Data!.Terrain.TileIndices.Should().Equal(second.Data!.Terrain.TileIndices);
        first.Data!.Objects.Select(o => (o.Name, o.X, o.Y)).Should().Equal(second.Data!.Objects.Select(o => (o.Name, o.X, o.Y)));
    }

    /// <summary>
    /// Tests that generated tiling and road objects survive a codec round trip.
    /// </summary>
    [Fact]
    public void Generate_FullMap_RoundTripsWithValidTiling()
    {
        // Arrange
        var settings = new MapGenSettings { Seed = 7, PlayableWidth = 150, PlayableHeight = 150, NumPlayers = 2, RoadMode = MapGenRoadMode.Starts };

        // Act
        var result = sut.Generate(settings);

        // Assert
        result.Success.Should().BeTrue();
        var map = result.Data!;
        var writer = new MapChunkWriter();
        MapTerrainCodec.WriteHeightMap(writer, map.Terrain);
        MapTerrainCodec.WriteBlendTile(writer, map.Terrain);
        MapObjectCodec.WriteObjects(writer, map.Objects);
        var reader = new MapChunkReader(writer.ToFileBytes());
        var decodedTerrain = MapTerrainCodec.ReadHeightMap(reader, reader.TopLevel[0]);
        MapTerrainCodec.ReadBlendTile(reader, reader.TopLevel[1], decodedTerrain);
        var decodedObjects = MapObjectCodec.ReadObjects(reader, reader.TopLevel[2]);
        decodedTerrain.TileIndices.Should().Equal(map.Terrain.TileIndices);
        decodedTerrain.TextureClasses.Select(c => c.Name).Should().Equal(map.Terrain.TextureClasses.Select(c => c.Name));
        decodedObjects.Should().HaveCount(map.Objects.Count);
        MapOverlayTools.GetRoadSegments(map).Should().HaveCount(2);
        var decodedMap = new WorldBuilderMap();
        decodedMap.Objects.AddRange(decodedObjects);
        var decodedSegments = MapOverlayTools.GetRoadSegments(decodedMap);
        decodedSegments.Should().HaveCount(2);
        decodedSegments.Should().OnlyContain(s => s.RoadType == "DirtRoad");
    }

    /// <summary>
    /// Tests that absurd dimensions fail cleanly instead of overflowing.
    /// </summary>
    [Fact]
    public void Generate_OversizeDimensions_ReturnsFailure()
    {
        // Arrange
        var settings = new MapGenSettings { Seed = 1, PlayableWidth = int.MaxValue, PlayableHeight = int.MaxValue };

        // Act
        var result = sut.Generate(settings);

        // Assert
        result.Success.Should().BeFalse();
    }
}
