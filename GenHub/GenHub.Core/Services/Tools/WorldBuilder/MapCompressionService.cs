// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Results;
using System.Buffers.Binary;
using System.IO.Compression;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Detects, decompresses, and compresses map file envelopes.
/// Verified against Libraries/Compression/CompressionManager: 4-byte magic
/// (EAR/NOX/ZL1-ZL9/EAB/EAH) plus i32 uncompressed length header. ZLib
/// payloads are standard zlib streams (the engine uses compress2/uncompress),
/// so the header and Adler-32 trailer follow RFC 1950 byte order.
/// </summary>
public sealed class MapCompressionService : IMapCompressionService
{
    /// <inheritdoc />
    public MapCompression Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length < WorldBuilderConstants.Compression.EnvelopeHeaderSize)
        {
            return MapCompression.None;
        }

        if (StartsWith(data, WorldBuilderConstants.Compression.RefPackMagic))
        {
            return MapCompression.RefPack;
        }

        if (data[0] == WorldBuilderConstants.Compression.ZLibMagic0
            && data[1] == WorldBuilderConstants.Compression.ZLibMagic1
            && data[2] >= (byte)('0' + WorldBuilderConstants.Compression.MinZLibLevel)
            && data[2] <= (byte)('0' + WorldBuilderConstants.Compression.MaxZLibLevel)
            && data[3] == 0)
        {
            return MapCompression.ZLib;
        }

        if (StartsWith(data, WorldBuilderConstants.Compression.NoxMagic)
            || StartsWith(data, WorldBuilderConstants.Compression.BTreeMagic)
            || StartsWith(data, WorldBuilderConstants.Compression.HuffMagic))
        {
            return MapCompression.Unsupported;
        }

        return MapCompression.None;
    }

    /// <inheritdoc />
    public OperationResult<int> GetUncompressedSize(ReadOnlySpan<byte> data)
    {
        if (data.Length < WorldBuilderConstants.Compression.EnvelopeHeaderSize)
        {
            return OperationResult<int>.CreateSuccess(data.Length);
        }

        if (Detect(data) == MapCompression.None)
        {
            return OperationResult<int>.CreateSuccess(data.Length);
        }

        var declared = BitConverter.ToInt32(data.Slice(4, 4));
        if (declared < 0 || declared > WorldBuilderConstants.Limits.MaxDecompressedSize)
        {
            return OperationResult<int>.CreateFailure($"Invalid declared uncompressed length {declared}.");
        }

        return OperationResult<int>.CreateSuccess(declared);
    }

    /// <inheritdoc />
    public OperationResult<int> GetCompressionIntent(ReadOnlySpan<byte> data)
    {
        var detected = Detect(data);
        if (detected == MapCompression.None)
        {
            return OperationResult<int>.CreateSuccess(WorldBuilderConstants.Compression.IntentNone);
        }

        if (detected == MapCompression.RefPack)
        {
            return OperationResult<int>.CreateSuccess(WorldBuilderConstants.Compression.IntentRefPack);
        }

        if (detected == MapCompression.ZLib)
        {
            var level = data[2] - '0';
            return OperationResult<int>.CreateSuccess(
                WorldBuilderConstants.Compression.IntentZLibBase + level - 1);
        }

        return OperationResult<int>.CreateFailure($"Unsupported map compression envelope '{EnvelopeName(data)}'.");
    }

    /// <inheritdoc />
    public OperationResult<byte[]> Decompress(ReadOnlySpan<byte> data)
    {
        var detected = Detect(data);
        return detected switch
        {
            MapCompression.None => OperationResult<byte[]>.CreateSuccess(data.ToArray()),
            MapCompression.RefPack => RefPackDecoder.Decode(data[WorldBuilderConstants.Compression.EnvelopeHeaderSize..]),
            MapCompression.ZLib => ZLibDecoder.Decode(data),
            _ => OperationResult<byte[]>.CreateFailure($"Unsupported map compression envelope '{EnvelopeName(data)}'. Resave the map uncompressed or RefPack/ZLib."),
        };
    }

    /// <inheritdoc />
    public OperationResult<byte[]> CompressZLib(ReadOnlySpan<byte> raw, int level)
    {
        if (level < WorldBuilderConstants.Compression.MinZLibLevel
            || level > WorldBuilderConstants.Compression.MaxZLibLevel)
        {
            return OperationResult<byte[]>.CreateFailure($"Invalid ZLib level {level}; expected 1..9.");
        }

        try
        {
            using var output = new MemoryStream();
            output.WriteByte(WorldBuilderConstants.Compression.ZLibMagic0);
            output.WriteByte(WorldBuilderConstants.Compression.ZLibMagic1);
            output.WriteByte((byte)('0' + level));
            output.WriteByte(0);
            output.Write(BitConverter.GetBytes(raw.Length), 0, 4);
            output.WriteByte(WorldBuilderConstants.Compression.ZLibStreamCmf);
            output.WriteByte(WorldBuilderConstants.Compression.ZLibStreamFlgByLevel[level]);
            using (var deflater = new DeflateStream(output, ToDeflateLevel(level), true))
            {
                var bytes = raw.ToArray();
                deflater.Write(bytes, 0, bytes.Length);
            }

            Span<byte> adler = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(adler, ZLibAdler32(raw));
            output.Write(adler);
            return OperationResult<byte[]>.CreateSuccess(output.ToArray());
        }
        catch (ObjectDisposedException ex)
        {
            return OperationResult<byte[]>.CreateFailure($"ZLib compression failed: {ex.Message}");
        }
        catch (IOException ex)
        {
            return OperationResult<byte[]>.CreateFailure($"ZLib compression failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult<byte[]>.CreateFailure($"ZLib compression failed: {ex.Message}");
        }
    }

    private static bool StartsWith(ReadOnlySpan<byte> data, byte[] magic)
    {
        if (data.Length < magic.Length)
        {
            return false;
        }

        for (var i = 0; i < magic.Length; i++)
        {
            if (data[i] != magic[i])
            {
                return false;
            }
        }

        return true;
    }

    private static string EnvelopeName(ReadOnlySpan<byte> data)
    {
        if (StartsWith(data, WorldBuilderConstants.Compression.NoxMagic))
        {
            return "NOX";
        }

        if (StartsWith(data, WorldBuilderConstants.Compression.BTreeMagic))
        {
            return "BTREE";
        }

        if (StartsWith(data, WorldBuilderConstants.Compression.HuffMagic))
        {
            return "HUFF";
        }

        return "unknown";
    }

    private static CompressionLevel ToDeflateLevel(int level)
    {
        return level <= WorldBuilderConstants.Compression.MinZLibLevel
            ? CompressionLevel.Fastest
            : CompressionLevel.Optimal;
    }

    private static uint ZLibAdler32(ReadOnlySpan<byte> data)
    {
        uint a = 1;
        uint b = 0;
        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        return (b << 16) | a;
    }

    private static class ZLibDecoder
    {
        public static OperationResult<byte[]> Decode(ReadOnlySpan<byte> data)
        {
            if (data.Length < WorldBuilderConstants.Compression.EnvelopeHeaderSize + 6)
            {
                return OperationResult<byte[]>.CreateFailure("Truncated ZLib envelope.");
            }

            var declared = BitConverter.ToInt32(data.Slice(4, 4));
            if (declared < 0 || declared > WorldBuilderConstants.Limits.MaxDecompressedSize)
            {
                return OperationResult<byte[]>.CreateFailure("Invalid ZLib declared length.");
            }

            try
            {
                var payload = data[WorldBuilderConstants.Compression.EnvelopeHeaderSize..].ToArray();
                using var input = new MemoryStream(payload, false)
                {
                    Position = 2,
                };
                using var inflater = new DeflateStream(input, CompressionMode.Decompress);
                var bytes = new byte[declared];
                var total = 0;
                var read = 0;
                while (total < declared && (read = inflater.Read(bytes, total, declared - total)) > 0)
                {
                    total += read;
                }

                if (total != declared || inflater.ReadByte() != -1)
                {
                    return OperationResult<byte[]>.CreateFailure("ZLib payload length mismatch.");
                }

                return OperationResult<byte[]>.CreateSuccess(bytes);
            }
            catch (ObjectDisposedException ex)
            {
                return OperationResult<byte[]>.CreateFailure($"ZLib decompression failed: {ex.Message}");
            }
            catch (IOException ex)
            {
                return OperationResult<byte[]>.CreateFailure($"ZLib decompression failed: {ex.Message}");
            }
            catch (InvalidDataException ex)
            {
                return OperationResult<byte[]>.CreateFailure($"ZLib decompression failed: {ex.Message}");
            }
        }
    }
}
