using GenHub.Core.Constants;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using System.Text;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Reads DataChunk files into chunk nodes.
/// </summary>
public sealed class MapChunkReader
{
    private readonly List<MapChunkNode> topLevel = [];
    private readonly Dictionary<uint, string> names = [];
    private readonly List<KeyValuePair<uint, string>> labelTable = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="MapChunkReader"/> class.
    /// </summary>
    /// <param name="data">File bytes.</param>
    public MapChunkReader(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Parse(data);
    }

    /// <summary>Gets a value indicating whether the table of contents was valid.</summary>
    public bool IsValidFileType { get; private set; }

    /// <summary>Gets the top-level chunks.</summary>
    public IReadOnlyList<MapChunkNode> TopLevel => topLevel;

    /// <summary>Gets the id-to-label map.</summary>
    public IReadOnlyDictionary<uint, string> Names => names;

    /// <summary>Gets the table-of-contents entries in file order.</summary>
    public IReadOnlyList<KeyValuePair<uint, string>> LabelTable => labelTable;

    /// <summary>
    /// Tries to parse file bytes, reporting truncation cleanly.
    /// </summary>
    /// <param name="data">File bytes.</param>
    /// <returns>The reader on success.</returns>
    public static OperationResult<MapChunkReader> TryParse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        try
        {
            var reader = new MapChunkReader(data);
            if (!reader.IsValidFileType)
            {
                return OperationResult<MapChunkReader>.CreateFailure("Missing DataChunk table of contents.");
            }

            return OperationResult<MapChunkReader>.CreateSuccess(reader);
        }
        catch (InvalidDataException ex)
        {
            return OperationResult<MapChunkReader>.CreateFailure($"Truncated chunk file: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses a payload as a sequence of child chunks.
    /// </summary>
    /// <param name="payload">Payload bytes.</param>
    /// <returns>The child chunks.</returns>
    public IReadOnlyList<MapChunkNode> ParseChildren(IList<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var children = new List<MapChunkNode>();
        var cursor = new ChunkHeaderCursor(payload is byte[] bytes ? bytes : [.. payload]);
        while (!cursor.AtEnd)
        {
            children.Add(cursor.ReadNode(names));
        }

        return children;
    }

    /// <summary>
    /// Creates a primitive cursor over a node payload.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The cursor.</returns>
    public MapChunkCursor Cursor(MapChunkNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new MapChunkCursor(node.Data, names);
    }

    private void Parse(byte[] data)
    {
        var cursor = new RawCursor(data);
        if (!cursor.TryReadMagic())
        {
            return;
        }

        var count = cursor.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            var length = cursor.ReadByte();
            var name = cursor.ReadAscii(length);
            var id = cursor.ReadUInt32();
            names[id] = name;
            labelTable.Add(new KeyValuePair<uint, string>(id, name));
        }

        IsValidFileType = count > 0 && !cursor.AtEnd;
        if (!IsValidFileType)
        {
            return;
        }

        var headers = new ChunkHeaderCursor(cursor.Remaining());
        while (!headers.AtEnd)
        {
            topLevel.Add(headers.ReadNode(names));
        }
    }

    private sealed class RawCursor(byte[] data)
    {
        private int position;

        public bool AtEnd => position >= data.Length;

        public byte[] Remaining()
        {
            var rest = new byte[data.Length - position];
            Array.Copy(data, position, rest, 0, rest.Length);
            return rest;
        }

        public bool TryReadMagic()
        {
            var magic = WorldBuilderConstants.Framing.TableOfContentsMagic;
            if (data.Length - position < magic.Length + 4)
            {
                return false;
            }

            for (var i = 0; i < magic.Length; i++)
            {
                if (data[position + i] != magic[i])
                {
                    return false;
                }
            }

            position += magic.Length;
            return true;
        }

        public int ReadInt32()
        {
            return BitConverter.ToInt32(ReadRaw(4), 0);
        }

        public uint ReadUInt32()
        {
            return BitConverter.ToUInt32(ReadRaw(4), 0);
        }

        public byte ReadByte()
        {
            return ReadRaw(1)[0];
        }

        public string ReadAscii(int length)
        {
            return Encoding.Latin1.GetString(ReadRaw(length));
        }

        private byte[] ReadRaw(int count)
        {
            if (count < 0 || count > data.Length - position)
            {
                throw new InvalidDataException("Truncated chunk file.");
            }

            var result = new byte[count];
            Array.Copy(data, position, result, 0, count);
            position += count;
            return result;
        }
    }

    private sealed class ChunkHeaderCursor(byte[] data)
    {
        private int position;

        public bool AtEnd => position >= data.Length;

        public MapChunkNode ReadNode(IReadOnlyDictionary<uint, string> table)
        {
            if (data.Length - position < WorldBuilderConstants.Framing.ChunkHeaderSize)
            {
                throw new InvalidDataException("Truncated chunk header.");
            }

            var id = BitConverter.ToUInt32(data, position);
            var version = BitConverter.ToUInt16(data, position + 4);
            var size = BitConverter.ToInt32(data, position + 6);
            position += WorldBuilderConstants.Framing.ChunkHeaderSize;
            if (size < 0 || size > data.Length - position)
            {
                throw new InvalidDataException("Truncated chunk payload.");
            }

            if (!table.TryGetValue(id, out var label))
            {
                throw new InvalidDataException($"Chunk id {id} is missing from the table of contents.");
            }

            var payload = new byte[size];
            Array.Copy(data, position, payload, 0, size);
            position += size;
            return new MapChunkNode(label, version, payload);
        }
    }
}
