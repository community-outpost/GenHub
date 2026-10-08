using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Unit tests for <see cref="MapMiscCodec"/> (triggers, waypoints, lighting,
/// world dictionary, preview, and tiling probe).
/// </summary>
public sealed class MapMiscCodecTests
{
    /// <summary>
    /// Tests that Zero Hour triggers round trip with layer names.
    /// </summary>
    [Fact]
    public void Triggers_RoundTrip_PreservesFields()
    {
        // Arrange
        var trigger = new MapTrigger
        {
            Name = "Area1",
            LayerName = "Layer",
            Id = 12,
            IsWaterArea = true,
            IsRiver = true,
            RiverStart = 2,
        };
        trigger.Points.Add((100, 200, 0));
        trigger.Points.Add((300, 400, 0));

        // Act
        var writer = new MapChunkWriter();
        MapMiscCodec.WriteTriggers(writer, [trigger]);
        var reader = new MapChunkReader(writer.ToFileBytes());
        var decoded = MapMiscCodec.ReadTriggers(reader, reader.TopLevel[0]);

        // Assert
        decoded.Should().ContainSingle();
        decoded[0].Name.Should().Be("Area1");
        decoded[0].LayerName.Should().Be("Layer");
        decoded[0].Id.Should().Be(12);
        decoded[0].IsWaterArea.Should().BeTrue();
        decoded[0].IsRiver.Should().BeTrue();
        decoded[0].RiverStart.Should().Be(2);
        decoded[0].Points.Should().BeEquivalentTo([(100, 200, 0), (300, 400, 0)]);
    }

    /// <summary>
    /// Tests that legacy triggers without a layer name read with an empty layer.
    /// </summary>
    [Fact]
    public void Triggers_Version3_ReadsWithoutLayer()
    {
        // Arrange
        var writer = new MapChunkWriter();
        writer.OpenChunk(WorldBuilderConstants.Chunks.PolygonTriggers, 3);
        writer.WriteInt(1);
        writer.WriteAscii("Area1");
        writer.WriteInt(5);
        writer.WriteByte(0);
        writer.WriteByte(0);
        writer.WriteInt(0);
        writer.WriteInt(0);
        writer.CloseChunk();

        // Act
        var reader = new MapChunkReader(writer.ToFileBytes());
        var decoded = MapMiscCodec.ReadTriggers(reader, reader.TopLevel[0]);

        // Assert
        decoded.Should().ContainSingle();
        decoded[0].LayerName.Should().BeEmpty();
        decoded[0].Id.Should().Be(5);
    }

    /// <summary>
    /// Tests that waypoint links round trip.
    /// </summary>
    [Fact]
    public void Waypoints_RoundTrip_PreservesLinks()
    {
        // Arrange
        var writer = new MapChunkWriter();

        // Act
        MapMiscCodec.WriteWaypoints(writer, [new MapWaypointLink(1, 2), new MapWaypointLink(3, 4)]);
        var reader = new MapChunkReader(writer.ToFileBytes());
        var decoded = MapMiscCodec.ReadWaypoints(reader, reader.TopLevel[0]);

        // Assert
        decoded.Should().BeEquivalentTo([new MapWaypointLink(1, 2), new MapWaypointLink(3, 4)]);
    }

    /// <summary>
    /// Tests that full version 3 lighting round trips.
    /// </summary>
    [Fact]
    public void Lighting_RoundTrip_PreservesLights()
    {
        // Arrange
        var lighting = new MapLightingData { TimeOfDay = 2, ShadowColor = unchecked((int)0xFF112233) };
        for (var i = 0; i < WorldBuilderConstants.Limits.TimeOfDayCount; i++)
        {
            var slot = new MapTimeOfDayLighting();
            for (var j = 0; j < WorldBuilderConstants.Limits.MaxGlobalLights; j++)
            {
                slot.TerrainLights.Add(new MapLight { AmbientR = i + 0.1f, DiffuseB = j + 0.2f, PosZ = -1f });
                slot.ObjectLights.Add(new MapLight { AmbientG = i + 0.3f, PosX = j });
            }

            lighting.TimesOfDay.Add(slot);
        }

        // Act
        var writer = new MapChunkWriter();
        MapMiscCodec.WriteLighting(writer, lighting);
        var reader = new MapChunkReader(writer.ToFileBytes());
        var decoded = MapMiscCodec.ReadLighting(reader, reader.TopLevel[0]);

        // Assert
        decoded.TimeOfDay.Should().Be(2);
        decoded.ShadowColor.Should().Be(unchecked((int)0xFF112233));
        decoded.TimesOfDay.Should().HaveCount(4);
        decoded.TimesOfDay[1].TerrainLights.Should().HaveCount(3);
        decoded.TimesOfDay[1].TerrainLights[2].DiffuseB.Should().Be(2.2f);
        decoded.TimesOfDay[1].ObjectLights.Should().HaveCount(3);
        decoded.TimesOfDay[1].ObjectLights[0].AmbientG.Should().Be(1.3f);
    }

    /// <summary>
    /// Tests that version 1 lighting pads missing extra lights with defaults.
    /// </summary>
    [Fact]
    public void Lighting_Version1_PadsExtraLights()
    {
        // Arrange
        var writer = new MapChunkWriter();
        writer.OpenChunk(WorldBuilderConstants.Chunks.GlobalLighting, 1);
        writer.WriteInt(1);
        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 18; j++)
            {
                writer.WriteReal(0.5f);
            }
        }

        writer.CloseChunk();

        // Act
        var reader = new MapChunkReader(writer.ToFileBytes());
        var decoded = MapMiscCodec.ReadLighting(reader, reader.TopLevel[0]);

        // Assert
        decoded.TimesOfDay.Should().HaveCount(4);
        decoded.TimesOfDay[0].TerrainLights.Should().HaveCount(3);
        decoded.TimesOfDay[0].ObjectLights.Should().HaveCount(3);
        decoded.TimesOfDay[0].TerrainLights[1].PosZ.Should().Be(-1f);
        decoded.ShadowColor.Should().Be(0);
    }

    /// <summary>
    /// Tests that the world dictionary round trips.
    /// </summary>
    [Fact]
    public void World_RoundTrip_PreservesDict()
    {
        // Arrange
        var world = new MapDict();
        world.Add(new MapDictValue(WorldBuilderConstants.DictKeys.Weather, WorldBuilderConstants.DictValueType.AsciiString, StringValue: "Normal"));
        world.Add(new MapDictValue(WorldBuilderConstants.DictKeys.MapName, WorldBuilderConstants.DictValueType.AsciiString, StringValue: "My Map"));

        // Act
        var writer = new MapChunkWriter();
        MapMiscCodec.WriteWorld(writer, world);
        var reader = new MapChunkReader(writer.ToFileBytes());
        var decoded = MapMiscCodec.ReadWorld(reader, reader.TopLevel[0]);

        // Assert
        decoded.GetString(WorldBuilderConstants.DictKeys.Weather).Should().Be("Normal");
        decoded.GetString(WorldBuilderConstants.DictKeys.MapName).Should().Be("My Map");
    }

    /// <summary>
    /// Tests that embedded preview pixels round trip.
    /// </summary>
    [Fact]
    public void Preview_RoundTrip_PreservesPixels()
    {
        // Arrange
        var preview = new MapPreviewData { Width = 2, Height = 1, Pixels = [unchecked((int)0xFFAABBCC), 7] };

        // Act
        var writer = new MapChunkWriter();
        MapMiscCodec.WritePreview(writer, preview);
        var reader = new MapChunkReader(writer.ToFileBytes());
        var decoded = MapMiscCodec.ReadPreview(reader, reader.TopLevel[0]);

        // Assert
        decoded.Width.Should().Be(2);
        decoded.Height.Should().Be(1);
        decoded.Pixels.Should().Equal(unchecked((int)0xFFAABBCC), 7);
    }
}
