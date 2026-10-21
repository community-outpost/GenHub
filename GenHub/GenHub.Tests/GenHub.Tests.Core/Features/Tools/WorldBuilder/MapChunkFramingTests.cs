using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Services.Tools.WorldBuilder;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Unit tests for <see cref="MapChunkWriter"/> and <see cref="MapChunkReader"/>.
/// Layout mirrors the engine DataChunk framing: table of contents first,
/// then chunk headers (u32 id, u16 version, i32 size) with nested payloads.
/// </summary>
public sealed class MapChunkFramingTests
{
    /// <summary>
    /// Tests that an empty document matches engine semantics: no chunks, invalid file type.
    /// </summary>
    [Fact]
    public void RoundTrip_EmptyDocument_MatchesEngineSemantics()
    {
        // Arrange
        var writer = new MapChunkWriter();

        // Act
        var bytes = writer.ToFileBytes();
        var reader = new MapChunkReader(bytes);

        // Assert
        reader.IsValidFileType.Should().BeFalse();
        reader.TopLevel.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that chunk labels survive a write/read round trip.
    /// </summary>
    [Fact]
    public void RoundTrip_SingleChunk_PreservesLabelAndVersion()
    {
        // Arrange
        var writer = new MapChunkWriter();
        writer.OpenChunk(WorldBuilderConstants.Chunks.WorldInfo, WorldBuilderConstants.Versions.WorldInfo);
        writer.CloseChunk();

        // Act
        var reader = new MapChunkReader(writer.ToFileBytes());

        // Assert
        reader.IsValidFileType.Should().BeTrue();
        reader.TopLevel.Should().ContainSingle();
        reader.TopLevel[0].Label.Should().Be(WorldBuilderConstants.Chunks.WorldInfo);
        reader.TopLevel[0].Version.Should().Be(WorldBuilderConstants.Versions.WorldInfo);
    }

    /// <summary>
    /// Tests that nested chunks keep their parent scope.
    /// </summary>
    [Fact]
    public void RoundTrip_NestedChunks_PreservesHierarchy()
    {
        // Arrange
        var writer = new MapChunkWriter();
        writer.OpenChunk(WorldBuilderConstants.Chunks.ObjectsList, WorldBuilderConstants.Versions.ObjectsList);
        writer.OpenChunk(WorldBuilderConstants.Chunks.Object, WorldBuilderConstants.Versions.ObjectsList);
        writer.WriteInt(42);
        writer.CloseChunk();
        writer.OpenChunk(WorldBuilderConstants.Chunks.Object, WorldBuilderConstants.Versions.ObjectsList);
        writer.WriteInt(43);
        writer.CloseChunk();
        writer.CloseChunk();

        // Act
        var reader = new MapChunkReader(writer.ToFileBytes());
        var children = reader.ParseChildren(reader.TopLevel[0].Data);

        // Assert
        reader.TopLevel.Should().ContainSingle();
        children.Should().HaveCount(2);
        new MapChunkCursor(children[0].Data).ReadInt().Should().Be(42);
        new MapChunkCursor(children[1].Data).ReadInt().Should().Be(43);
    }

    /// <summary>
    /// Tests that all primitive types round trip exactly.
    /// </summary>
    [Fact]
    public void RoundTrip_Primitives_PreservesValues()
    {
        // Arrange
        var writer = new MapChunkWriter();
        writer.OpenChunk("Probe", 1);
        writer.WriteInt(-123456);
        writer.WriteReal(3.5f);
        writer.WriteByte(0xAB);
        writer.WriteAscii("MapName");
        writer.WriteUnicode("UnicodeName");
        writer.WriteBytes([0x01, 0x02, 0x03]);
        writer.CloseChunk();

        // Act
        var reader = new MapChunkReader(writer.ToFileBytes());
        var cursor = new MapChunkCursor(reader.TopLevel[0].Data);

        // Assert
        cursor.ReadInt().Should().Be(-123456);
        cursor.ReadReal().Should().Be(3.5f);
        cursor.ReadByte().Should().Be(0xAB);
        cursor.ReadAscii().Should().Be("MapName");
        cursor.ReadUnicode().Should().Be("UnicodeName");
        cursor.ReadBytes(3).Should().Equal(0x01, 0x02, 0x03);
        cursor.AtEnd.Should().BeTrue();
    }

    /// <summary>
    /// Tests that dictionaries round trip with types and order preserved.
    /// </summary>
    [Fact]
    public void RoundTrip_Dict_PreservesEntries()
    {
        // Arrange
        var writer = new MapChunkWriter();
        writer.OpenChunk("Probe", 1);
        writer.WriteDict(
        [
            new("flag", WorldBuilderConstants.DictValueType.Bool, IntValue: 1),
            new("count", WorldBuilderConstants.DictValueType.Int, IntValue: 7),
            new("ratio", WorldBuilderConstants.DictValueType.Real, RealValue: 1.5f),
            new("name", WorldBuilderConstants.DictValueType.AsciiString, StringValue: "Player_1"),
            new("title", WorldBuilderConstants.DictValueType.UnicodeString, StringValue: "Titre"),
        ]);
        writer.CloseChunk();

        // Act
        var reader = new MapChunkReader(writer.ToFileBytes());
        var cursor = reader.Cursor(reader.TopLevel[0]);
        var dict = cursor.ReadDict();

        // Assert
        dict.Values.Should().HaveCount(5);
        dict.GetBool("flag").Should().BeTrue();
        dict.GetInt("count").Should().Be(7);
        dict.GetReal("ratio").Should().Be(1.5f);
        dict.GetString("name").Should().Be("Player_1");
        dict.GetString("title").Should().Be("Titre");
    }

    /// <summary>
    /// Tests that name keys resolve through the shared table of contents.
    /// </summary>
    [Fact]
    public void RoundTrip_NameKey_ResolvesName()
    {
        // Arrange
        var writer = new MapChunkWriter();
        writer.OpenChunk("Probe", 1);
        writer.WriteNameKey("SomeAction");
        writer.CloseChunk();

        // Act
        var reader = new MapChunkReader(writer.ToFileBytes());
        var cursor = reader.Cursor(reader.TopLevel[0]);

        // Assert
        cursor.ReadNameKey().Should().Be("SomeAction");
    }

    /// <summary>
    /// Tests that files without the magic are rejected.
    /// </summary>
    [Fact]
    public void Reader_BadMagic_IsNotValidFileType()
    {
        // Arrange
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };

        // Act
        var reader = new MapChunkReader(bytes);

        // Assert
        reader.IsValidFileType.Should().BeFalse();
        reader.TopLevel.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that truncated files fail cleanly.
    /// </summary>
    [Fact]
    public void Reader_TruncatedFile_ReturnsFailure()
    {
        // Arrange
        var writer = new MapChunkWriter();
        writer.OpenChunk(WorldBuilderConstants.Chunks.WorldInfo, WorldBuilderConstants.Versions.WorldInfo);
        writer.WriteInt(1);
        writer.CloseChunk();
        var bytes = writer.ToFileBytes();

        // Act
        var result = MapChunkReader.TryParse(bytes[..^3]);

        // Assert
        result.Success.Should().BeFalse();
    }

    /// <summary>
    /// Tests that reading past the end of a payload fails cleanly.
    /// </summary>
    [Fact]
    public void Cursor_ReadPastEnd_ThrowsInvalidData()
    {
        // Arrange
        var cursor = new MapChunkCursor([0x01]);

        // Act
        Func<int> act = cursor.ReadInt;

        // Assert
        act.Should().Throw<InvalidDataException>();
    }
}
