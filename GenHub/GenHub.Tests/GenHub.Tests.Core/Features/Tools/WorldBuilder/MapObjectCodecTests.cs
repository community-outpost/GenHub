using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Unit tests for <see cref="MapObjectCodec"/>.
/// Field order mirrors SidesList, MapObject, and team serializers.
/// </summary>
public sealed class MapObjectCodecTests
{
    /// <summary>
    /// Tests that objects round trip with position, angle, flags, name, and properties.
    /// </summary>
    [Fact]
    public void Objects_RoundTrip_PreservesEntries()
    {
        // Arrange
        var objects = new List<MapObjectEntry>
        {
            new()
            {
                X = 10.5f,
                Y = 20.5f,
                Z = 0f,
                Angle = 90f,
                Flags = WorldBuilderConstants.ObjectFlags.RoadPoint1,
                Name = "Waypoint_1",
            },
        };
        objects[0].Properties.Add(new MapDictValue(WorldBuilderConstants.DictKeys.WaypointId, WorldBuilderConstants.DictValueType.Int, IntValue: 3));

        // Act
        var writer = new MapChunkWriter();
        MapObjectCodec.WriteObjects(writer, objects);
        var reader = new MapChunkReader(writer.ToFileBytes());
        var decoded = MapObjectCodec.ReadObjects(reader, reader.TopLevel[0]);

        // Assert
        decoded.Should().ContainSingle();
        decoded[0].X.Should().Be(10.5f);
        decoded[0].Y.Should().Be(20.5f);
        decoded[0].Angle.Should().Be(90f);
        decoded[0].Flags.Should().Be(WorldBuilderConstants.ObjectFlags.RoadPoint1);
        decoded[0].Name.Should().Be("Waypoint_1");
        decoded[0].Properties.GetInt(WorldBuilderConstants.DictKeys.WaypointId).Should().Be(3);
    }

    /// <summary>
    /// Tests that sides round trip with build lists and teams.
    /// </summary>
    [Fact]
    public void Sides_RoundTrip_PreservesSidesAndTeams()
    {
        // Arrange
        var side = new MapSideEntry();
        side.Properties.Add(new MapDictValue(WorldBuilderConstants.DictKeys.PlayerName, WorldBuilderConstants.DictValueType.AsciiString, StringValue: "P America"));
        side.BuildList.Add(new MapBuildListEntry
        {
            BuildingName = "HQ",
            TemplateName = "AmericaCommandCenter",
            X = 1f,
            Y = 2f,
            Z = 0f,
            Angle = 45f,
            InitiallyBuilt = true,
            NumRebuilds = 2,
            Script = "BuildScript",
            Health = 100,
            Whiner = true,
            Unsellable = false,
            Repairable = true,
        });
        var team = new MapTeamEntry();
        team.Properties.Add(new MapDictValue(WorldBuilderConstants.DictKeys.TeamName, WorldBuilderConstants.DictValueType.AsciiString, StringValue: "Team1"));
        team.Properties.Add(new MapDictValue(WorldBuilderConstants.DictKeys.TeamOwner, WorldBuilderConstants.DictValueType.AsciiString, StringValue: "P America"));

        // Act
        var writer = new MapChunkWriter();
        MapObjectCodec.WriteSides(writer, [side], [team], [new ScriptListModel()]);
        var reader = new MapChunkReader(writer.ToFileBytes());
        var (sides, teams, _) = MapObjectCodec.ReadSides(reader, reader.TopLevel[0]);

        // Assert
        sides.Should().ContainSingle();
        sides[0].Properties.GetString(WorldBuilderConstants.DictKeys.PlayerName).Should().Be("P America");
        sides[0].BuildList.Should().ContainSingle();
        var build = sides[0].BuildList[0];
        build.BuildingName.Should().Be("HQ");
        build.TemplateName.Should().Be("AmericaCommandCenter");
        build.X.Should().Be(1f);
        build.Angle.Should().Be(45f);
        build.InitiallyBuilt.Should().BeTrue();
        build.NumRebuilds.Should().Be(2);
        build.Script.Should().Be("BuildScript");
        build.Health.Should().Be(100);
        build.Whiner.Should().BeTrue();
        build.Repairable.Should().BeTrue();
        teams.Should().ContainSingle();
        teams[0].Properties.GetString(WorldBuilderConstants.DictKeys.TeamName).Should().Be("Team1");
    }

    /// <summary>
    /// Tests that the sides chunk carries the Zero Hour version.
    /// </summary>
    [Fact]
    public void Sides_Bytes_CarryVersion3()
    {
        // Arrange
        var writer = new MapChunkWriter();

        // Act
        MapObjectCodec.WriteSides(writer, [], [], []);
        var reader = new MapChunkReader(writer.ToFileBytes());

        // Assert
        reader.TopLevel[0].Label.Should().Be(WorldBuilderConstants.Chunks.SidesList);
        reader.TopLevel[0].Version.Should().Be(WorldBuilderConstants.Versions.SidesList);
    }

    /// <summary>
    /// Tests that team dictionaries read until end of chunk without a count prefix.
    /// </summary>
    [Fact]
    public void Teams_RoundTrip_ReadsUntilEndOfChunk()
    {
        // Arrange
        var first = new MapTeamEntry();
        first.Properties.Add(new MapDictValue(WorldBuilderConstants.DictKeys.TeamName, WorldBuilderConstants.DictValueType.AsciiString, StringValue: "A"));
        var second = new MapTeamEntry();
        second.Properties.Add(new MapDictValue(WorldBuilderConstants.DictKeys.TeamName, WorldBuilderConstants.DictValueType.AsciiString, StringValue: "B"));

        // Act
        var writer = new MapChunkWriter();
        MapObjectCodec.WriteTeams(writer, [first, second]);
        var reader = new MapChunkReader(writer.ToFileBytes());
        var decoded = MapObjectCodec.ReadTeams(reader, reader.TopLevel[0]);

        // Assert
        decoded.Should().HaveCount(2);
        decoded[0].Properties.GetString(WorldBuilderConstants.DictKeys.TeamName).Should().Be("A");
        decoded[1].Properties.GetString(WorldBuilderConstants.DictKeys.TeamName).Should().Be("B");
    }
}
