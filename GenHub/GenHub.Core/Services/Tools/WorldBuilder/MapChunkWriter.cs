using GenHub.Core.Constants;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using System.Text;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Writes DataChunk files: nested chunks with a shared label table.
/// </summary>
public sealed class MapChunkWriter
{
    private readonly Dictionary<string, uint> ids = new(StringComparer.Ordinal);
    private readonly Stack<MemoryStream> stack = new();
    private readonly MemoryStream root = new();
    private uint nextId = 1;

    /// <summary>Opens a nested chunk.</summary>
    /// <param name="label">Chunk label.</param>
    /// <param name="version">Chunk version.</param>
    public void OpenChunk(string label, ushort version)
    {
        ArgumentException.ThrowIfNullOrEmpty(label);
        if (!ids.TryGetValue(label, out var id))
        {
            id = nextId++;
            ids[label] = id;
        }

        var buffer = new MemoryStream();
        buffer.Write(BitConverter.GetBytes(id), 0, 4);
        buffer.Write(BitConverter.GetBytes(version), 0, 2);
        buffer.Write(new byte[4], 0, 4);
        stack.Push(buffer);
    }

    /// <summary>Closes the current chunk, patching its size.</summary>
    public void CloseChunk()
    {
        if (stack.Count == 0)
        {
            throw new InvalidOperationException("No open chunk to close.");
        }

        var buffer = stack.Pop();
        var bytes = buffer.ToArray();
        var size = bytes.Length - WorldBuilderConstants.Framing.ChunkHeaderSize;
        Array.Copy(BitConverter.GetBytes(size), 0, bytes, 6, 4);
        var target = stack.Count > 0 ? stack.Peek() : root;
        target.Write(bytes, 0, bytes.Length);
        buffer.Dispose();
    }

    /// <summary>Writes a 32-bit integer.</summary>
    /// <param name="value">The value.</param>
    public void WriteInt(int value)
    {
        Current().Write(BitConverter.GetBytes(value), 0, 4);
    }

    /// <summary>Writes a 32-bit float.</summary>
    /// <param name="value">The value.</param>
    public void WriteReal(float value)
    {
        Current().Write(BitConverter.GetBytes(value), 0, 4);
    }

    /// <summary>Writes one byte.</summary>
    /// <param name="value">The value.</param>
    public void WriteByte(byte value)
    {
        Current().WriteByte(value);
    }

    /// <summary>Writes raw bytes.</summary>
    /// <param name="value">The bytes.</param>
    public void WriteBytes(IList<byte> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is byte[] bytes)
        {
            Current().Write(bytes, 0, bytes.Length);
            return;
        }

        for (var i = 0; i < value.Count; i++)
        {
            Current().WriteByte(value[i]);
        }
    }

    /// <summary>Writes a length-prefixed ASCII string.</summary>
    /// <param name="value">The value.</param>
    public void WriteAscii(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = Encoding.Latin1.GetBytes(value);
        if (bytes.Length > ushort.MaxValue)
        {
            throw new ArgumentException("ASCII string exceeds 65535 bytes.", nameof(value));
        }

        Current().Write(BitConverter.GetBytes((ushort)bytes.Length), 0, 2);
        Current().Write(bytes, 0, bytes.Length);
    }

    /// <summary>Writes a length-prefixed UTF-16 string.</summary>
    /// <param name="value">The value.</param>
    public void WriteUnicode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = Encoding.Unicode.GetBytes(value);
        if (value.Length > ushort.MaxValue)
        {
            throw new ArgumentException("Unicode string exceeds 65535 characters.", nameof(value));
        }

        Current().Write(BitConverter.GetBytes((ushort)value.Length), 0, 2);
        Current().Write(bytes, 0, bytes.Length);
    }

    /// <summary>Writes a dictionary.</summary>
    /// <param name="values">Entries in file order.</param>
    public void WriteDict(IEnumerable<MapDictValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var list = values.ToList();
        if (list.Count > ushort.MaxValue)
        {
            throw new ArgumentException("Dictionary exceeds 65535 entries.", nameof(values));
        }

        Current().Write(BitConverter.GetBytes((ushort)list.Count), 0, 2);
        foreach (var value in list)
        {
            WriteInt((int)(Allocate(value.Key) << 8) | (int)value.Type);
            WriteTypedValue(value);
        }
    }

    /// <summary>Writes a name key resolving through the label table.</summary>
    /// <param name="name">The name.</param>
    public void WriteNameKey(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        WriteInt((int)(Allocate(name) << 8) | (int)WorldBuilderConstants.DictValueType.AsciiString);
    }

    /// <summary>
    /// Seeds the label table with original table-of-contents entries so re-saved
    /// chunk ids match the loaded file. Call before writing any chunks.
    /// </summary>
    /// <param name="table">Original id-to-label entries in file order.</param>
    public void SeedLabelTable(IEnumerable<KeyValuePair<uint, string>> table)
    {
        ArgumentNullException.ThrowIfNull(table);
        foreach (var pair in table)
        {
            ids[pair.Value] = pair.Key;
            if (pair.Key >= nextId)
            {
                nextId = pair.Key + 1;
            }
        }
    }

    /// <summary>Produces the final file bytes: table of contents plus chunks.</summary>
    /// <returns>The file bytes.</returns>
    public byte[] ToFileBytes()
    {
        if (stack.Count > 0)
        {
            throw new InvalidOperationException("Unclosed chunks remain.");
        }

        using var output = new MemoryStream();
        output.Write(WorldBuilderConstants.Framing.TableOfContentsMagic, 0, 4);
        output.Write(BitConverter.GetBytes(ids.Count), 0, 4);
        foreach (var pair in ids)
        {
            var nameBytes = Encoding.Latin1.GetBytes(pair.Key);
            output.WriteByte((byte)nameBytes.Length);
            output.Write(nameBytes, 0, nameBytes.Length);
            output.Write(BitConverter.GetBytes(pair.Value), 0, 4);
        }

        var chunks = root.ToArray();
        output.Write(chunks, 0, chunks.Length);
        return output.ToArray();
    }

    private uint Allocate(string label)
    {
        if (!ids.TryGetValue(label, out var id))
        {
            id = nextId++;
            ids[label] = id;
        }

        return id;
    }

    private void WriteTypedValue(MapDictValue value)
    {
        switch (value.Type)
        {
            case WorldBuilderConstants.DictValueType.Bool:
                WriteByte(value.IntValue != 0 ? (byte)1 : (byte)0);
                break;
            case WorldBuilderConstants.DictValueType.Int:
                WriteInt(value.IntValue);
                break;
            case WorldBuilderConstants.DictValueType.Real:
                WriteReal(value.RealValue);
                break;
            case WorldBuilderConstants.DictValueType.AsciiString:
                WriteAscii(value.StringValue);
                break;
            case WorldBuilderConstants.DictValueType.UnicodeString:
                WriteUnicode(value.StringValue);
                break;
            default:
                throw new ArgumentException($"Unknown dict value type {(int)value.Type}.", nameof(value));
        }
    }

    private MemoryStream Current()
    {
        if (stack.Count == 0)
        {
            throw new InvalidOperationException("No open chunk to write to.");
        }

        return stack.Peek();
    }
}
