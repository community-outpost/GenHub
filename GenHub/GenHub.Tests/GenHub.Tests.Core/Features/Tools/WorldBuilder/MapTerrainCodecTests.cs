using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Unit tests for <see cref="MapTerrainCodec"/>.
/// Field order mirrors WorldHeightMapEdit.saveToFile and WorldHeightMap parsers.
/// </summary>
public sealed class MapTerrainCodecTests
{
    /// <summary>
    /// Tests that height data round trips with exact field order.
    /// </summary>
    [Fact]
    public void HeightMap_RoundTrip_PreservesFields()
    {
        // Arrange
        var terrain = new MapTerrainData
        {
            Width = 4,
            Height = 3,
            BorderSize = 1,
            Heights = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110],
        };
        terrain.Boundaries.Add(new MapBoundary(2, 1));

        // Act
        var writer = new MapChunkWriter();
        MapTerrainCodec.WriteHeightMap(writer, terrain);
        var reader = new MapChunkReader(writer.ToFileBytes());
        var decoded = MapTerrainCodec.ReadHeightMap(reader, reader.TopLevel[0]);

        // Assert
        decoded.Width.Should().Be(4);
        decoded.Height.Should().Be(3);
        decoded.BorderSize.Should().Be(1);
        decoded.Boundaries.Should().ContainSingle().Which.Should().Be(new MapBoundary(2, 1));
        decoded.Heights.Should().Equal(terrain.Heights);
    }

    /// <summary>
    /// Tests that the height payload starts with width, height, and border size.
    /// </summary>
    [Fact]
    public void HeightMap_Bytes_StartWithDimensions()
    {
        // Arrange
        var terrain = new MapTerrainData { Width = 2, Height = 2, Heights = [1, 2, 3, 4] };

        // Act
        var writer = new MapChunkWriter();
        MapTerrainCodec.WriteHeightMap(writer, terrain);
        var reader = new MapChunkReader(writer.ToFileBytes());

        // Assert
        var payload = reader.TopLevel[0].Data.ToArray();
        BitConverter.ToInt32(payload, 0).Should().Be(2);
        BitConverter.ToInt32(payload, 4).Should().Be(2);
        BitConverter.ToInt32(payload, 8).Should().Be(0);
    }

    /// <summary>
    /// Tests that legacy height maps without borders read with defaults.
    /// </summary>
    [Fact]
    public void HeightMap_Version2_ReadsWithDefaults()
    {
        // Arrange: version 2 has no border size and no boundaries.
        var writer = new MapChunkWriter();
        writer.OpenChunk(WorldBuilderConstants.Chunks.HeightMapData, 2);
        writer.WriteInt(4);
        writer.WriteInt(4);
        writer.WriteInt(16);
        writer.WriteBytes(new byte[16]);
        writer.CloseChunk();

        // Act
        var reader = new MapChunkReader(writer.ToFileBytes());
        var decoded = MapTerrainCodec.ReadHeightMap(reader, reader.TopLevel[0]);

        // Assert
        decoded.Width.Should().Be(4);
        decoded.Height.Should().Be(4);
        decoded.BorderSize.Should().Be(0);
        decoded.Boundaries.Should().ContainSingle().Which.Should().Be(new MapBoundary(4, 4));
    }

    /// <summary>
    /// Tests that blend data round trips including classes, blends, and cliffs.
    /// </summary>
    [Fact]
    public void BlendTile_RoundTrip_PreservesFields()
    {
        // Arrange
        var terrain = new MapTerrainData
        {
            Width = 2,
            Height = 2,
            TileIndices = [1, 2, 3, 4],
            BlendTileIndices = [0, 0, 0, 0],
            ExtraBlendTileIndices = [0, 0, 0, 0],
            CliffInfoIndices = [0, 0, 0, 0],
            CliffState = [0xFF, 0x00],
            NumBitmapTiles = 4,
            NumBlendedTiles = 2,
            NumCliffInfo = 2,
        };
        terrain.TextureClasses.Add(new MapTextureClass(0, 4, 2, "Grass"));
        terrain.EdgeTextureClasses.Add(new MapEdgeTextureClass(0, 2, 2, "Cliff"));
        terrain.BlendTiles.Add(new MapBlendTile(3, 1, 2, 3, 4, 5, 6, -1));
        terrain.CliffInfos.Add(new MapCliffInfo(7, [0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f], 1, 0));

        // Act
        var writer = new MapChunkWriter();
        MapTerrainCodec.WriteBlendTile(writer, terrain);
        var reader = new MapChunkReader(writer.ToFileBytes());
        var decoded = MapTerrainCodec.ReadBlendTile(reader, reader.TopLevel[0], new MapTerrainData { Width = 2, Height = 2 });

        // Assert
        decoded.TileIndices.Should().Equal(terrain.TileIndices);
        decoded.BlendTileIndices.Should().Equal(terrain.BlendTileIndices);
        decoded.ExtraBlendTileIndices.Should().Equal(terrain.ExtraBlendTileIndices);
        decoded.CliffInfoIndices.Should().Equal(terrain.CliffInfoIndices);
        decoded.CliffState.Should().Equal(terrain.CliffState);
        decoded.NumBitmapTiles.Should().Be(4);
        decoded.NumBlendedTiles.Should().Be(2);
        decoded.NumCliffInfo.Should().Be(2);
        decoded.TextureClasses.Should().ContainSingle().Which.Should().Be(new MapTextureClass(0, 4, 2, "Grass"));
        decoded.EdgeTextureClasses.Should().ContainSingle().Which.Should().Be(new MapEdgeTextureClass(0, 2, 2, "Cliff"));
        decoded.BlendTiles.Should().ContainSingle().Which.Should().Be(new MapBlendTile(3, 1, 2, 3, 4, 5, 6, -1));
        decoded.CliffInfos.Should().ContainSingle();
        decoded.CliffInfos[0].TileIndex.Should().Be(7);
        decoded.CliffInfos[0].U.Should().BeEquivalentTo([0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f], options => options.WithStrictOrdering());
    }

    /// <summary>
    /// Tests that every blend record carries the format sentinel.
    /// </summary>
    [Fact]
    public void BlendTile_Bytes_ContainSentinel()
    {
        // Arrange
        var terrain = new MapTerrainData
        {
            Width = 1,
            Height = 1,
            TileIndices = [0],
            BlendTileIndices = [0],
            ExtraBlendTileIndices = [0],
            CliffInfoIndices = [0],
            CliffState = [0],
            NumBitmapTiles = 1,
            NumBlendedTiles = 2,
            NumCliffInfo = 1,
        };
        terrain.BlendTiles.Add(new MapBlendTile(0, 0, 0, 0, 0, 0, 0, -1));

        // Act
        var writer = new MapChunkWriter();
        MapTerrainCodec.WriteBlendTile(writer, terrain);
        var reader = new MapChunkReader(writer.ToFileBytes());

        // Assert
        var payload = reader.TopLevel[0].Data;
        payload.Should().ContainInOrder(BitConverter.GetBytes(WorldBuilderConstants.Limits.BlendSentinel));
    }

    /// <summary>
    /// Tests that V1 odd-dimension full-res blend data downsamples via carried originals.
    /// </summary>
    [Fact]
    public void BlendTile_V1OddDimensions_Downsamples()
    {
        // Arrange: 5x4 full-res data for a heightmap already halved to 3x2.
        var writer = new MapChunkWriter();
        writer.OpenChunk(WorldBuilderConstants.Chunks.BlendTileData, 1);
        writer.WriteInt(20);
        var tiles = new byte[40];
        tiles[0] = 7;
        writer.WriteBytes(tiles);
        writer.WriteBytes(new byte[40]);
        writer.WriteInt(64);
        writer.WriteInt(1);
        writer.WriteInt(1);
        writer.WriteInt(0);
        writer.WriteInt(64);
        writer.WriteInt(8);
        writer.WriteInt(0);
        writer.WriteAscii("Grass");
        writer.CloseChunk();
        var reader = new MapChunkReader(writer.ToFileBytes());
        var terrain = new MapTerrainData { Width = 3, Height = 2, OriginalWidth = 5, OriginalHeight = 4 };

        // Act
        var result = MapTerrainCodec.ReadBlendTile(reader, reader.TopLevel[0], terrain);

        // Assert
        result.Width.Should().Be(3);
        result.Height.Should().Be(2);
        result.TileIndices.Should().HaveCount(6);
        result.TileIndices[0].Should().Be(7);
    }

    /// <summary>
    /// Tests that V1 already-halved blend data loads without a second downsample.
    /// </summary>
    [Fact]
    public void BlendTile_V1AlreadyHalved_LoadsVerbatim()
    {
        // Arrange
        var writer = new MapChunkWriter();
        writer.OpenChunk(WorldBuilderConstants.Chunks.BlendTileData, 1);
        writer.WriteInt(6);
        var tiles = new byte[12];
        tiles[0] = 9;
        tiles[10] = 11;
        writer.WriteBytes(tiles);
        writer.WriteBytes(new byte[12]);
        writer.WriteInt(64);
        writer.WriteInt(1);
        writer.WriteInt(1);
        writer.WriteInt(0);
        writer.WriteInt(64);
        writer.WriteInt(8);
        writer.WriteInt(0);
        writer.WriteAscii("Grass");
        writer.CloseChunk();
        var reader = new MapChunkReader(writer.ToFileBytes());
        var terrain = new MapTerrainData { Width = 3, Height = 2 };

        // Act
        var result = MapTerrainCodec.ReadBlendTile(reader, reader.TopLevel[0], terrain);

        // Assert
        result.TileIndices.Should().HaveCount(6);
        result.TileIndices[0].Should().Be(9);
        result.TileIndices[5].Should().Be(11);
    }

    /// <summary>
    /// Tests that V1 heightmaps record their pre-halving dimensions.
    /// </summary>
    [Fact]
    public void HeightMap_V1_RecordsOriginalDimensions()
    {
        // Arrange
        var writer = new MapChunkWriter();
        writer.OpenChunk(WorldBuilderConstants.Chunks.HeightMapData, 1);
        writer.WriteInt(5);
        writer.WriteInt(4);
        writer.WriteInt(20);
        writer.WriteBytes(new byte[20]);
        writer.CloseChunk();
        var reader = new MapChunkReader(writer.ToFileBytes());

        // Act
        var result = MapTerrainCodec.ReadHeightMap(reader, reader.TopLevel[0]);

        // Assert
        result.Width.Should().Be(3);
        result.Height.Should().Be(2);
        result.OriginalWidth.Should().Be(5);
        result.OriginalHeight.Should().Be(4);
        result.Heights.Should().HaveCount(6);
    }
}
