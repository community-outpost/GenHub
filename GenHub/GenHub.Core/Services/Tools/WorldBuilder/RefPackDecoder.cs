// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// EA RefPack (10FB family) decompressor.
/// Implements the EA SAGE engine RefPack dialect used in Command &amp; Conquer: Generals and Zero Hour
/// maps and BIG archives. Mirrors the engine REF_decode opcode forms with strict bounds checking.
/// Field order and opcode semantics match EA's official refdecode.cpp
/// (electronicarts/CnC_Generals_Zero_Hour, Compression/EAC): the 0x01 flag means a compressed
/// size precedes the uncompressed size, and the stop form carries (first &amp; 3) trailing literals:
/// 2-byte header (0x10FB/0x11FB/0x90FB/0x91FB), 3-byte or 4-byte uncompressed size, 0x00..0x7F 2-byte
/// short copy (0..3 literals, 3..10 copy length, 0..1023 offset), 0x80..0xBF 3-byte medium copy (0..3
/// literals, 4..67 copy length, 0..16383 offset), 0xC0..0xDF 4-byte long copy (0..3 literals, 5..1028 copy
/// length, 0..131071 offset), 0xE0..0xFB literal runs ((tag - 0xDF) * 4 literals), and 0xFC..0xFF stop opcode
/// with 0..3 trailing literal bytes.
/// </summary>
public static class RefPackDecoder
{
    private enum DecodeStep
    {
        Continue,
        Completed,
        Failed,
    }

    /// <summary>
    /// Decodes a RefPack payload (without the map envelope header).
    /// </summary>
    /// <param name="data">Payload starting at the 10FB-family marker.</param>
    /// <returns>The decompressed bytes.</returns>
    public static OperationResult<byte[]> Decode(ReadOnlySpan<byte> data)
    {
        if (!TryParseHeader(data, out var position, out var length, out var headerError))
        {
            return OperationResult<byte[]>.CreateFailure(headerError!);
        }

        var output = new byte[length];
        var written = 0;
        while (true)
        {
            var step = DecodeNextChunk(data, ref position, output, ref written, out var stepError);
            if (step == DecodeStep.Failed)
            {
                return OperationResult<byte[]>.CreateFailure(stepError!);
            }

            if (step == DecodeStep.Completed)
            {
                break;
            }
        }

        if (written != length)
        {
            return OperationResult<byte[]>.CreateFailure($"RefPack length mismatch: wrote {written} of {length} bytes.");
        }

        return OperationResult<byte[]>.CreateSuccess(output);
    }

    private static bool TryParseHeader(
        ReadOnlySpan<byte> data,
        out int position,
        out int length,
        out string? error)
    {
        position = 0;
        length = 0;
        error = null;

        if (data.Length < 5)
        {
            error = "Truncated RefPack header.";
            return false;
        }

        var type = (data[0] << 8) | data[1];
        if (type != 0x10FB && type != 0x11FB && type != 0x90FB && type != 0x91FB)
        {
            error = $"Unknown RefPack marker 0x{type:X4}.";
            return false;
        }

        position = 2;
        var sizeLength = (type & 0x8000) != 0 ? 4 : 3;
        if ((type & 0x100) != 0)
        {
            position += sizeLength;
        }

        for (var i = 0; i < sizeLength; i++)
        {
            if (position >= data.Length)
            {
                error = "Truncated RefPack length.";
                return false;
            }

            length = (length << 8) | data[position++];
        }

        if (length < 0 || length > WorldBuilderConstants.Limits.MaxDecompressedSize)
        {
            error = $"Invalid RefPack length {length}.";
            return false;
        }

        return true;
    }

    private static DecodeStep DecodeNextChunk(
        ReadOnlySpan<byte> data,
        ref int position,
        byte[] output,
        ref int written,
        out string? error)
    {
        error = null;
        if (position >= data.Length)
        {
            error = "Truncated RefPack stream.";
            return DecodeStep.Failed;
        }

        var first = data[position++];
        if ((first & 0x80) == 0)
        {
            if (!CopyShort(data, ref position, output, ref written, first))
            {
                error = "Truncated RefPack short copy.";
                return DecodeStep.Failed;
            }

            return DecodeStep.Continue;
        }

        if ((first & 0x40) == 0)
        {
            if (!CopyInt(data, ref position, output, ref written, first))
            {
                error = "Truncated RefPack int copy.";
                return DecodeStep.Failed;
            }

            return DecodeStep.Continue;
        }

        if ((first & 0x20) == 0)
        {
            if (!CopyLong(data, ref position, output, ref written, first))
            {
                error = "Truncated RefPack long copy.";
                return DecodeStep.Failed;
            }

            return DecodeStep.Continue;
        }

        return DecodeLiteralOrTail(data, ref position, output, ref written, first, out error);
    }

    private static DecodeStep DecodeLiteralOrTail(
        ReadOnlySpan<byte> data,
        ref int position,
        byte[] output,
        ref int written,
        byte first,
        out string? error)
    {
        error = null;
        var run = ((first & 0x1F) << 2) + 4;
        if (run <= 112)
        {
            if (!CopyLiteral(data, ref position, output, ref written, run))
            {
                error = "Truncated RefPack literal run.";
                return DecodeStep.Failed;
            }

            return DecodeStep.Continue;
        }

        if (!CopyLiteral(data, ref position, output, ref written, first & 3))
        {
            error = "Truncated RefPack tail.";
            return DecodeStep.Failed;
        }

        return DecodeStep.Completed;
    }

    private static bool CopyShort(ReadOnlySpan<byte> data, ref int position, byte[] output, ref int written, byte first)
    {
        if (position >= data.Length)
        {
            return false;
        }

        var second = data[position++];
        var literals = first & 3;
        if (!CopyLiteral(data, ref position, output, ref written, literals))
        {
            return false;
        }

        var offset = ((first & 0x60) << 3) + second;
        var length = ((first & 0x1C) >> 2) + 3;
        return CopyMatch(output, ref written, offset, length);
    }

    private static bool CopyInt(ReadOnlySpan<byte> data, ref int position, byte[] output, ref int written, byte first)
    {
        if (position + 1 >= data.Length)
        {
            return false;
        }

        var second = data[position++];
        var third = data[position++];
        var literals = second >> 6;
        if (!CopyLiteral(data, ref position, output, ref written, literals))
        {
            return false;
        }

        var offset = ((second & 0x3F) << 8) + third;
        var length = (first & 0x3F) + 4;
        return CopyMatch(output, ref written, offset, length);
    }

    private static bool CopyLong(ReadOnlySpan<byte> data, ref int position, byte[] output, ref int written, byte first)
    {
        if (position + 2 >= data.Length)
        {
            return false;
        }

        var second = data[position++];
        var third = data[position++];
        var forth = data[position++];
        var literals = first & 3;
        if (!CopyLiteral(data, ref position, output, ref written, literals))
        {
            return false;
        }

        var offset = (((first & 0x10) >> 4) << 16) + (second << 8) + third;
        var length = (((first & 0x0C) >> 2) << 8) + forth + 5;
        return CopyMatch(output, ref written, offset, length);
    }

    private static bool CopyLiteral(ReadOnlySpan<byte> data, ref int position, byte[] output, ref int written, int count)
    {
        if (count < 0 || count > data.Length - position || count > output.Length - written)
        {
            return false;
        }

        data.Slice(position, count).CopyTo(output.AsSpan(written, count));
        position += count;
        written += count;
        return true;
    }

    private static bool CopyMatch(byte[] output, ref int written, int offset, int length)
    {
        var source = written - 1 - offset;
        if (source < 0 || length < 0 || length > output.Length - written)
        {
            return false;
        }

        for (var i = 0; i < length; i++)
        {
            output[written++] = output[source++];
        }

        return true;
    }
}
