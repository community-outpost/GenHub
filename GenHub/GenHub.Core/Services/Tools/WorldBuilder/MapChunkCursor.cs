using GenHub.Core.Constants;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using System.Text;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Bounds-checked primitive reader over a chunk payload.
/// Overruns throw <see cref="InvalidDataException"/> as contract violations.
/// </summary>
public sealed class MapChunkCursor
{
    private readonly IList<byte> data;
    private readonly IReadOnlyDictionary<uint, string> names;
    private int position;

    /// <summary>
    /// Initializes a new instance of the <see cref="MapChunkCursor"/> class.
    /// </summary>
    /// <param name="payload">Payload bytes.</param>
    /// <param name="nameTable">Table-of-contents id to name map for name keys.</param>
    public MapChunkCursor(IList<byte> payload, IReadOnlyDictionary<uint, string>? nameTable = null)
    {
        data = payload;
        names = nameTable ?? new Dictionary<uint, string>();
    }

    /// <summary>Gets the bytes remaining.</summary>
    public int Remaining => data.Count - position;

    /// <summary>Copies the unread payload bytes.</summary>
    /// <returns>The remaining bytes.</returns>
    public byte[] RemainingBytes()
    {
        var rest = new byte[Remaining];
        CopyRange(position, rest, rest.Length);
        return rest;
    }

    /// <summary>Gets a value indicating whether the payload is fully consumed.</summary>
    public bool AtEnd => Remaining <= 0;

    /// <summary>Reads a 32-bit integer.</summary>
    /// <returns>The value.</returns>
    public int ReadInt()
    {
        return BitConverter.ToInt32(ReadRaw(4), 0);
    }

    /// <summary>Reads a 32-bit float.</summary>
    /// <returns>The value.</returns>
    public float ReadReal()
    {
        return BitConverter.ToSingle(ReadRaw(4), 0);
    }

    /// <summary>Reads one byte.</summary>
    /// <returns>The value.</returns>
    public byte ReadByte()
    {
        return ReadRaw(1)[0];
    }

    /// <summary>Reads a length-prefixed ASCII string.</summary>
    /// <returns>The value.</returns>
    public string ReadAscii()
    {
        var length = BitConverter.ToUInt16(ReadRaw(2), 0);
        return Encoding.Latin1.GetString(ReadRaw(length));
    }

    /// <summary>Reads a length-prefixed UTF-16 string.</summary>
    /// <returns>The value.</returns>
    public string ReadUnicode()
    {
        var length = BitConverter.ToUInt16(ReadRaw(2), 0);
        return Encoding.Unicode.GetString(ReadRaw(length * 2));
    }

    /// <summary>Reads raw bytes.</summary>
    /// <param name="count">Byte count.</param>
    /// <returns>The bytes.</returns>
    public byte[] ReadBytes(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return ReadRaw(count);
    }

    /// <summary>Reads a dictionary.</summary>
    /// <returns>The dictionary.</returns>
    public MapDict ReadDict()
    {
        var count = BitConverter.ToUInt16(ReadRaw(2), 0);
        var dict = new MapDict();
        for (var i = 0; i < count; i++)
        {
            var keyAndType = ReadInt();
            var type = (WorldBuilderConstants.DictValueType)(keyAndType & 0xFF);
            var key = ResolveName((uint)(keyAndType >> 8));
            dict.Add(ReadTypedValue(key, type));
        }

        return dict;
    }

    /// <summary>Reads a name key resolving through the table of contents.</summary>
    /// <returns>The resolved name.</returns>
    public string ReadNameKey()
    {
        var keyAndType = ReadInt();
        return ResolveName((uint)(keyAndType >> 8));
    }

    private MapDictValue ReadTypedValue(string key, WorldBuilderConstants.DictValueType type)
    {
        return type switch
        {
            WorldBuilderConstants.DictValueType.Bool => new MapDictValue(key, type, IntValue: ReadByte()),
            WorldBuilderConstants.DictValueType.Int => new MapDictValue(key, type, IntValue: ReadInt()),
            WorldBuilderConstants.DictValueType.Real => new MapDictValue(key, type, RealValue: ReadReal()),
            WorldBuilderConstants.DictValueType.AsciiString => new MapDictValue(key, type, StringValue: ReadAscii()),
            WorldBuilderConstants.DictValueType.UnicodeString => new MapDictValue(key, type, StringValue: ReadUnicode()),
            _ => throw new InvalidDataException($"Unknown dict value type {(int)type} for key '{key}'."),
        };
    }

    private string ResolveName(uint id)
    {
        if (names.TryGetValue(id, out var name))
        {
            return name;
        }

        throw new InvalidDataException($"Chunk id {id} is missing from the table of contents.");
    }

    private byte[] ReadRaw(int count)
    {
        if (count < 0 || count > data.Count - position)
        {
            throw new InvalidDataException($"Read of {count} bytes at {position} exceeds payload of {data.Count} bytes.");
        }

        var result = new byte[count];
        CopyRange(position, result, count);
        position += count;
        return result;
    }

    private void CopyRange(int sourceIndex, byte[] destination, int count)
    {
        if (data is byte[] bytes)
        {
            Array.Copy(bytes, sourceIndex, destination, 0, count);
            return;
        }

        for (var i = 0; i < count; i++)
        {
            destination[i] = data[sourceIndex + i];
        }
    }
}
