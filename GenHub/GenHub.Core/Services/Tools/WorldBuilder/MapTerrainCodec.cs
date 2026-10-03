using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Reads and writes HeightMapData and BlendTileData chunks.
/// Layout mirrors WorldHeightMapEdit.saveToFile and WorldHeightMap parsers,
/// including legacy version branches.
/// </summary>
public static class MapTerrainCodec
{
    /// <summary>
    /// Writes the HeightMapData chunk (version 4).
    /// </summary>
    /// <param name="writer">The chunk writer.</param>
    /// <param name="terrain">The terrain data.</param>
    public static void WriteHeightMap(MapChunkWriter writer, MapTerrainData terrain)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(terrain);
        writer.OpenChunk(WorldBuilderConstants.Chunks.HeightMapData, WorldBuilderConstants.Versions.HeightMap);
        writer.WriteInt(terrain.Width);
        writer.WriteInt(terrain.Height);
        writer.WriteInt(terrain.BorderSize);
        writer.WriteInt(terrain.Boundaries.Count);
        foreach (var boundary in terrain.Boundaries)
        {
            writer.WriteInt(boundary.X);
            writer.WriteInt(boundary.Y);
        }

        writer.WriteInt(terrain.Heights.Count);
        writer.WriteBytes(terrain.Heights);
        writer.CloseChunk();
    }

    /// <summary>
    /// Reads the HeightMapData chunk, accepting versions 1 through 4.
    /// </summary>
    /// <param name="reader">The chunk reader.</param>
    /// <param name="node">The chunk node.</param>
    /// <returns>The terrain data with heights filled.</returns>
    public static MapTerrainData ReadHeightMap(MapChunkReader reader, MapChunkNode node)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(node);
        var cursor = reader.Cursor(node);
        var width = cursor.ReadInt();
        var height = cursor.ReadInt();
        var terrain = new MapTerrainData
        {
            Width = width,
            Height = height,
            BorderSize = node.Version >= 3 ? cursor.ReadInt() : 0,
        };
        if (node.Version >= 4)
        {
            var count = cursor.ReadInt();
            for (var i = 0; i < count; i++)
            {
                terrain.Boundaries.Add(new MapBoundary(cursor.ReadInt(), cursor.ReadInt()));
            }
        }
        else
        {
            terrain.Boundaries.Add(new MapBoundary(
                terrain.Width - (2 * terrain.BorderSize),
                terrain.Height - (2 * terrain.BorderSize)));
        }

        if (terrain.Width <= 0 || terrain.Height <= 0 || (long)terrain.Width * terrain.Height > WorldBuilderConstants.Limits.MaxDecompressedSize)
        {
            throw new InvalidDataException($"Invalid terrain dimensions: {terrain.Width}x{terrain.Height}.");
        }

        var dataSize = cursor.ReadInt();
        if (dataSize <= 0 || (long)dataSize != (long)terrain.Width * terrain.Height)
        {
            throw new InvalidDataException($"HeightMapData size {dataSize} does not match {terrain.Width}x{terrain.Height}.");
        }

        var heights = cursor.ReadBytes(dataSize);
        if (node.Version == 1)
        {
            terrain.OriginalWidth = terrain.Width;
            terrain.OriginalHeight = terrain.Height;
            heights = Halve(heights, terrain.Width, terrain.Height);
            terrain.Width = (terrain.Width + 1) / 2;
            terrain.Height = (terrain.Height + 1) / 2;
            terrain.BorderSize = (terrain.BorderSize + 1) / 2;
            for (var i = 0; i < terrain.Boundaries.Count; i++)
            {
                var b = terrain.Boundaries[i];
                terrain.Boundaries[i] = new MapBoundary((b.X + 1) / 2, (b.Y + 1) / 2);
            }
        }

        terrain.Heights = heights;
        return terrain;
    }

    /// <summary>
    /// Writes the BlendTileData chunk (version 8).
    /// </summary>
    /// <param name="writer">The chunk writer.</param>
    /// <param name="terrain">The terrain data.</param>
    public static void WriteBlendTile(MapChunkWriter writer, MapTerrainData terrain)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(terrain);
        var dataSize = terrain.Width * terrain.Height;
        writer.OpenChunk(WorldBuilderConstants.Chunks.BlendTileData, WorldBuilderConstants.Versions.BlendTile);
        writer.WriteInt(dataSize);
        writer.WriteBytes(ToBytes(terrain.TileIndices));
        writer.WriteBytes(ToBytes(terrain.BlendTileIndices));
        writer.WriteBytes(ToBytes(terrain.ExtraBlendTileIndices));
        writer.WriteBytes(ToBytes(terrain.CliffInfoIndices));
        writer.WriteBytes(terrain.CliffState);
        writer.WriteInt(terrain.NumBitmapTiles);
        writer.WriteInt(terrain.NumBlendedTiles);
        writer.WriteInt(terrain.NumCliffInfo);
        writer.WriteInt(terrain.TextureClasses.Count);
        foreach (var textureClass in terrain.TextureClasses)
        {
            writer.WriteInt(textureClass.FirstTile);
            writer.WriteInt(textureClass.NumTiles);
            writer.WriteInt(textureClass.Width);
            writer.WriteInt(0);
            writer.WriteAscii(textureClass.Name);
        }

        writer.WriteInt(terrain.NumEdgeTiles);
        writer.WriteInt(terrain.EdgeTextureClasses.Count);
        foreach (var edgeClass in terrain.EdgeTextureClasses)
        {
            writer.WriteInt(edgeClass.FirstTile);
            writer.WriteInt(edgeClass.NumTiles);
            writer.WriteInt(edgeClass.Width);
            writer.WriteAscii(edgeClass.Name);
        }

        foreach (var blend in terrain.BlendTiles)
        {
            writer.WriteInt(blend.BlendIndex);
            writer.WriteByte(blend.Horizontal);
            writer.WriteByte(blend.Vertical);
            writer.WriteByte(blend.RightDiagonal);
            writer.WriteByte(blend.LeftDiagonal);
            writer.WriteByte(blend.Inverted);
            writer.WriteByte(blend.LongDiagonal);
            writer.WriteInt(blend.CustomBlendEdgeClass);
            writer.WriteInt(WorldBuilderConstants.Limits.BlendSentinel);
        }

        foreach (var cliff in terrain.CliffInfos)
        {
            writer.WriteInt(cliff.TileIndex);
            foreach (var coordinate in cliff.U)
            {
                writer.WriteReal(coordinate);
            }

            writer.WriteByte(cliff.Flip);
            writer.WriteByte(cliff.Mutant);
        }

        writer.CloseChunk();
    }

    /// <summary>
    /// Reads the BlendTileData chunk into existing terrain dimensions.
    /// </summary>
    /// <param name="reader">The chunk reader.</param>
    /// <param name="node">The chunk node.</param>
    /// <param name="terrain">Terrain carrying width and height.</param>
    /// <returns>The same terrain with blend data filled.</returns>
    public static MapTerrainData ReadBlendTile(MapChunkReader reader, MapChunkNode node, MapTerrainData terrain)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(terrain);
        var cursor = reader.Cursor(node);
        var dataSize = cursor.ReadInt();
        var expectedSize = (long)terrain.Width * terrain.Height;
        var fullWidth = terrain.OriginalWidth > 0 ? terrain.OriginalWidth : terrain.Width * 2;
        var fullHeight = terrain.OriginalHeight > 0 ? terrain.OriginalHeight : terrain.Height * 2;
        var fullSize = (long)fullWidth * fullHeight;
        var needsDownsample = node.Version == 1 && dataSize == fullSize && dataSize != expectedSize;
        var unhalvedWidth = needsDownsample ? fullWidth : terrain.Width;
        var unhalvedHeight = needsDownsample ? fullHeight : terrain.Height;

        if (needsDownsample)
        {
            expectedSize = fullSize;
        }

        if (dataSize <= 0 || (long)dataSize != expectedSize)
        {
            throw new InvalidDataException($"BlendTileData size {dataSize} does not match terrain.");
        }

        ReadTileIndices(cursor, node.Version, terrain, dataSize);
        terrain.NumBitmapTiles = cursor.ReadInt();
        terrain.NumBlendedTiles = cursor.ReadInt();
        if (terrain.NumBitmapTiles <= 0 || terrain.NumBitmapTiles >= WorldBuilderConstants.Limits.MaxBitmapTiles)
        {
            throw new InvalidDataException($"Unlikely bitmap tile count {terrain.NumBitmapTiles}.");
        }

        if (terrain.NumBlendedTiles <= 0 || terrain.NumBlendedTiles > WorldBuilderConstants.Limits.MaxBlendTiles + 1)
        {
            throw new InvalidDataException($"Unlikely blend tile count {terrain.NumBlendedTiles}.");
        }

        terrain.NumCliffInfo = node.Version >= 5 ? cursor.ReadInt() : 1;
        terrain.NumEdgeTiles = 0;
        ReadTextureClasses(cursor, terrain);
        if (node.Version >= 4)
        {
            terrain.NumEdgeTiles = cursor.ReadInt();
            ReadEdgeClasses(cursor, terrain);
        }

        ReadBlendTilesAndCliffs(cursor, node.Version, terrain);

        if (needsDownsample)
        {
            DownsampleBlend(terrain, unhalvedWidth, unhalvedHeight);
        }

        return terrain;
    }

    private static void ReadTileIndices(MapChunkCursor cursor, ushort version, MapTerrainData terrain, int dataSize)
    {
        terrain.TileIndices = ToShorts(cursor.ReadBytes(dataSize * 2));
        terrain.BlendTileIndices = ToShorts(cursor.ReadBytes(dataSize * 2));
        terrain.ExtraBlendTileIndices = version >= 6
            ? ToShorts(cursor.ReadBytes(dataSize * 2))
            : new short[dataSize];
        terrain.CliffInfoIndices = version >= 5
            ? ToShorts(cursor.ReadBytes(dataSize * 2))
            : new short[dataSize];
        terrain.CliffState = version >= 7
            ? ReadCliffState(cursor, version, terrain.Width, terrain.Height)
            : [];
    }

    private static void ReadBlendTilesAndCliffs(MapChunkCursor cursor, ushort version, MapTerrainData terrain)
    {
        for (var i = 1; i < terrain.NumBlendedTiles; i++)
        {
            terrain.BlendTiles.Add(ReadBlendTileRecord(cursor, version));
        }

        if (version >= 5)
        {
            for (var i = 1; i < terrain.NumCliffInfo; i++)
            {
                terrain.CliffInfos.Add(ReadCliffInfo(cursor));
            }
        }
    }

    private static void ReadTextureClasses(MapChunkCursor cursor, MapTerrainData terrain)
    {
        var count = cursor.ReadInt();
        if (count <= 0 || count >= WorldBuilderConstants.Limits.MaxTextureClasses)
        {
            throw new InvalidDataException($"Unlikely texture class count {count}.");
        }

        for (var i = 0; i < count; i++)
        {
            var firstTile = cursor.ReadInt();
            var numTiles = cursor.ReadInt();
            var width = cursor.ReadInt();
            cursor.ReadInt();
            var name = cursor.ReadAscii();
            terrain.TextureClasses.Add(new MapTextureClass(firstTile, numTiles, width, name));
        }
    }

    private static void ReadEdgeClasses(MapChunkCursor cursor, MapTerrainData terrain)
    {
        var count = cursor.ReadInt();
        for (var i = 0; i < count; i++)
        {
            terrain.EdgeTextureClasses.Add(new MapEdgeTextureClass(
                cursor.ReadInt(),
                cursor.ReadInt(),
                cursor.ReadInt(),
                cursor.ReadAscii()));
        }
    }

    private static MapBlendTile ReadBlendTileRecord(MapChunkCursor cursor, ushort version)
    {
        var blendIndex = cursor.ReadInt();
        var horizontal = cursor.ReadByte();
        var vertical = cursor.ReadByte();
        var rightDiagonal = cursor.ReadByte();
        var leftDiagonal = cursor.ReadByte();
        var inverted = cursor.ReadByte();
        var longDiagonal = version >= 3 ? cursor.ReadByte() : (byte)0;
        var customEdge = version >= 4 ? cursor.ReadInt() : -1;
        var sentinel = cursor.ReadInt();
        if (sentinel != WorldBuilderConstants.Limits.BlendSentinel)
        {
            throw new InvalidDataException($"Blend record sentinel 0x{sentinel:X8} is invalid.");
        }

        return new MapBlendTile(blendIndex, horizontal, vertical, rightDiagonal, leftDiagonal, inverted, longDiagonal, customEdge);
    }

    private static MapCliffInfo ReadCliffInfo(MapChunkCursor cursor)
    {
        var tileIndex = cursor.ReadInt();
        var uv = new float[8];
        for (var i = 0; i < uv.Length; i++)
        {
            uv[i] = cursor.ReadReal();
        }

        return new MapCliffInfo(tileIndex, uv, cursor.ReadByte(), cursor.ReadByte());
    }

    private static byte[] ReadCliffState(MapChunkCursor cursor, ushort version, int width, int height)
    {
        var flipWidth = ((width + 7) / 8) * height;
        if (version == 7)
        {
            var byteWidth = ((width + 1) / 8) * height;
            var legacy = cursor.ReadBytes(byteWidth);
            var state = new byte[flipWidth];
            var rowOld = (width + 1) / 8;
            var rowNew = (width + 7) / 8;
            for (var row = 0; row < height; row++)
            {
                Array.Copy(legacy, row * rowOld, state, row * rowNew, Math.Min(rowOld, rowNew));
            }

            return state;
        }

        return cursor.ReadBytes(flipWidth);
    }

    private static void DownsampleBlend(MapTerrainData terrain, int width, int height)
    {
        var newWidth = (width + 1) / 2;
        var newHeight = (height + 1) / 2;
        var newSize = newWidth * newHeight;
        terrain.TileIndices = Subsample(terrain.TileIndices, width, height, newSize);
        terrain.BlendTileIndices = new short[newSize];
        terrain.ExtraBlendTileIndices = new short[newSize];
        terrain.CliffInfoIndices = new short[newSize];
        terrain.NumBlendedTiles = 1;
        terrain.Width = newWidth;
        terrain.Height = newHeight;
    }

    private static short[] Subsample(IList<short> source, int width, int height, int newSize)
    {
        var result = new short[newSize];
        var newWidth = (width + 1) / 2;
        var newHeight = (height + 1) / 2;
        for (var row = 0; row < newHeight; row++)
        {
            for (var col = 0; col < newWidth; col++)
            {
                result[(row * newWidth) + col] = source[(2 * row * width) + (2 * col)];
            }
        }

        return result;
    }

    private static byte[] Halve(byte[] source, int width, int height)
    {
        var newWidth = (width + 1) / 2;
        var newHeight = (height + 1) / 2;
        var result = new byte[newWidth * newHeight];
        for (var row = 0; row < newHeight; row++)
        {
            for (var col = 0; col < newWidth; col++)
            {
                result[(row * newWidth) + col] = source[(2 * row * width) + (2 * col)];
            }
        }

        return result;
    }

    private static byte[] ToBytes(IList<short> values)
    {
        var bytes = new byte[values.Count * 2];
        for (var i = 0; i < values.Count; i++)
        {
            var pair = BitConverter.GetBytes(values[i]);
            bytes[i * 2] = pair[0];
            bytes[(i * 2) + 1] = pair[1];
        }

        return bytes;
    }

    private static short[] ToShorts(byte[] bytes)
    {
        var values = new short[bytes.Length / 2];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = BitConverter.ToInt16(bytes, i * 2);
        }

        return values;
    }
}
