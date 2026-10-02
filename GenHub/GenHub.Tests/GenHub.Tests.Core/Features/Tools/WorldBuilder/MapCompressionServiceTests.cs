// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using System.IO.Compression;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Unit tests for <see cref="MapCompressionService"/>.
/// Vectors are traced from the engine sources: envelope magics and the 8-byte
/// header from CompressionManager, RefPack opcode forms from EAC refdecode,
/// and ZLib framing cross-checked with an independent RFC 1950 implementation.
/// </summary>
public sealed class MapCompressionServiceTests
{
    private readonly MapCompressionService sut = new();

    /// <summary>
    /// Tests that raw chunk bytes without an envelope are detected as uncompressed.
    /// </summary>
    [Fact]
    public void Detect_RawChunkBytes_ReturnsNone()
    {
        // Arrange: starts with the table-of-contents magic, no envelope.
        var data = new byte[] { 0x43, 0x6B, 0x4D, 0x70, 0x01, 0x00, 0x00, 0x00 };

        // Act
        var detected = sut.Detect(data);

        // Assert
        detected.Should().Be(MapCompression.None);
    }

    /// <summary>
    /// Tests that short buffers are never treated as compressed.
    /// </summary>
    [Fact]
    public void Detect_ShortBuffer_ReturnsNone()
    {
        // Arrange
        var data = new byte[] { 0x45, 0x41, 0x52 };

        // Act
        var detected = sut.Detect(data);

        // Assert
        detected.Should().Be(MapCompression.None);
    }

    /// <summary>
    /// Tests that the RefPack envelope magic is detected.
    /// </summary>
    [Fact]
    public void Detect_RefPackMagic_ReturnsRefPack()
    {
        // Arrange
        var data = new byte[] { 0x45, 0x41, 0x52, 0x00, 0x08, 0x00, 0x00, 0x00 };

        // Act
        var detected = sut.Detect(data);

        // Assert
        detected.Should().Be(MapCompression.RefPack);
    }

    /// <summary>
    /// Tests that ZLib envelope magics are detected.
    /// </summary>
    /// <param name="level">The ZLib level digit.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    public void Detect_ZLibMagic_ReturnsZLib(int level)
    {
        // Arrange
        var data = new byte[] { 0x5A, 0x4C, (byte)('0' + level), 0x00, 0x08, 0x00, 0x00, 0x00 };

        // Act
        var detected = sut.Detect(data);

        // Assert
        detected.Should().Be(MapCompression.ZLib);
    }

    /// <summary>
    /// Tests that legacy envelopes are detected as unsupported rather than misread.
    /// </summary>
    [Fact]
    public void Detect_NoxMagic_ReturnsUnsupported()
    {
        // Arrange
        var data = new byte[] { 0x4E, 0x4F, 0x58, 0x00, 0x08, 0x00, 0x00, 0x00 };

        // Act
        var detected = sut.Detect(data);

        // Assert
        detected.Should().Be(MapCompression.Unsupported);
    }

    /// <summary>
    /// Tests that the declared uncompressed length is read from the envelope header.
    /// </summary>
    [Fact]
    public void GetUncompressedSize_RefPackEnvelope_ReturnsDeclaredLength()
    {
        // Arrange
        var data = new byte[] { 0x45, 0x41, 0x52, 0x00, 0x34, 0x12, 0x00, 0x00 };

        // Act
        var size = sut.GetUncompressedSize(data);

        // Assert
        size.Success.Should().BeTrue();
        size.Data.Should().Be(0x1234);
    }

    /// <summary>
    /// Tests that uncompressed buffers report their own length.
    /// </summary>
    [Fact]
    public void GetUncompressedSize_UncompressedBytes_ReturnsBufferLength()
    {
        // Arrange
        var data = new byte[] { 0x43, 0x6B, 0x4D, 0x70, 0x01, 0x02, 0x03 };

        // Act
        var size = sut.GetUncompressedSize(data);

        // Assert
        size.Success.Should().BeTrue();
        size.Data.Should().Be(data.Length);
    }

    /// <summary>
    /// Tests that a corrupt declared length fails instead of clamping.
    /// </summary>
    /// <param name="declared">The corrupt declared length.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData((64 * 1024 * 1024) + 1)]
    public void GetUncompressedSize_BadDeclaredLength_ReturnsFailure(int declared)
    {
        // Arrange
        var data = new List<byte> { 0x5A, 0x4C, (byte)'5', 0x00 };
        data.AddRange(BitConverter.GetBytes(declared));

        // Act
        var size = sut.GetUncompressedSize([.. data]);

        // Assert
        size.Success.Should().BeFalse();
        size.FirstError.Should().NotBeNull();
    }

    /// <summary>
    /// Tests that envelopes map to engine CompressionType intent ids.
    /// </summary>
    [Fact]
    public void GetCompressionIntent_Envelopes_MapsToEngineIds()
    {
        // Arrange
        var raw = new byte[] { 0x43, 0x6B, 0x4D, 0x70, 0x01, 0x00, 0x00, 0x00 };
        var refpack = new byte[] { 0x45, 0x41, 0x52, 0x00, 0x08, 0x00, 0x00, 0x00 };
        var zlib9 = new byte[] { 0x5A, 0x4C, (byte)'9', 0x00, 0x08, 0x00, 0x00, 0x00 };

        // Act
        var none = sut.GetCompressionIntent(raw);
        var refpackIntent = sut.GetCompressionIntent(refpack);
        var zlib9Intent = sut.GetCompressionIntent(zlib9);

        // Assert
        none.Success.Should().BeTrue();
        none.Data.Should().Be(WorldBuilderConstants.Compression.IntentNone);
        refpackIntent.Success.Should().BeTrue();
        refpackIntent.Data.Should().Be(WorldBuilderConstants.Compression.IntentRefPack);
        zlib9Intent.Success.Should().BeTrue();
        zlib9Intent.Data.Should().Be(WorldBuilderConstants.Compression.IntentZLibBase + 8);
    }

    /// <summary>
    /// Tests that unsupported envelopes have no mappable intent.
    /// </summary>
    [Fact]
    public void GetCompressionIntent_UnsupportedEnvelope_ReturnsFailure()
    {
        // Arrange
        var data = new byte[] { 0x4E, 0x4F, 0x58, 0x00, 0x08, 0x00, 0x00, 0x00 };

        // Act
        var intent = sut.GetCompressionIntent(data);

        // Assert
        intent.Success.Should().BeFalse();
    }

    /// <summary>
    /// Tests that uncompressed data passes through decompression unchanged.
    /// </summary>
    [Fact]
    public void Decompress_UncompressedBytes_ReturnsSameBytes()
    {
        // Arrange
        var data = new byte[] { 0x43, 0x6B, 0x4D, 0x70, 0x01, 0x02, 0x03 };

        // Act
        var result = sut.Decompress(data);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Equal(data);
    }

    /// <summary>
    /// Tests RefPack literal runs plus the end marker (10FB, 3-byte length).
    /// Stream: header(ulen=8), literal x4, literal x4, end.
    /// </summary>
    [Fact]
    public void Decompress_RefPackLiterals_DecodesBytes()
    {
        // Arrange
        var data = new byte[]
        {
            0x10, 0xFB, 0x00, 0x00, 0x08,
            0xE0, 0x41, 0x42, 0x43, 0x44,
            0xE0, 0x45, 0x46, 0x47, 0x48,
            0xFC,
        };

        // Act
        var result = sut.Decompress(Envelop(data));

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Equal(0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48);
    }

    /// <summary>
    /// Tests the RefPack short copy form: literal "ABCD" then a copy of length 4.
    /// </summary>
    [Fact]
    public void Decompress_RefPackShortCopies_ReproducesPattern()
    {
        // Arrange: literal "ABCD", short copy (offset 1, length 4), end.
        var data = new byte[]
        {
            0x10, 0xFB, 0x00, 0x00, 0x08,
            0xE0, 0x41, 0x42, 0x43, 0x44,
            0x04, 0x01,
            0xFC,
        };

        // Act
        var result = sut.Decompress(Envelop(data));

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Equal(0x41, 0x42, 0x43, 0x44, 0x43, 0x44, 0x43, 0x44);
    }

    /// <summary>
    /// Tests the RefPack int copy form with a multi-byte offset.
    /// Prefix: 300 zero bytes; copy reads offset 299, length 4.
    /// </summary>
    [Fact]
    public void Decompress_RefPackIntCopy_CopiesFromOffset()
    {
        // Arrange: ulen = 304. Literals 112 + 112 + 76, then int copy, then end.
        var payload = new List<byte> { 0x10, 0xFB, 0x00, 0x01, 0x30 };
        for (var i = 0; i < 2; i++)
        {
            payload.Add(0xFB);
            for (var j = 0; j < 112; j++)
            {
                payload.Add(0x00);
            }
        }

        payload.Add(0xF2);
        for (var j = 0; j < 76; j++)
        {
            payload.Add(0x00);
        }

        payload.AddRange([0x80, 0x01, 0x2B, 0xFC]);

        // Act
        var result = sut.Decompress(Envelop([.. payload]));

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(304);
        result.Data.Should().OnlyContain(b => b == 0);
    }

    /// <summary>
    /// Tests the RefPack long copy form with an offset above 64K.
    /// Prefix: 74592 zero bytes; copy reads offset 0x12345, length 5.
    /// </summary>
    [Fact]
    public void Decompress_RefPackLongCopy_CopiesFromFarOffset()
    {
        // Arrange: ulen = 74597. Literals 666 x 112, then long copy, then end.
        var payload = new List<byte> { 0x90, 0xFB, 0x00, 0x01, 0x23, 0x65 };
        for (var i = 0; i < 666; i++)
        {
            payload.Add(0xFB);
            for (var j = 0; j < 112; j++)
            {
                payload.Add(0x00);
            }
        }

        payload.AddRange([0xD0, 0x23, 0x45, 0x00, 0xFC]);

        // Act
        var result = sut.Decompress(Envelop([.. payload]));

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(74597);
    }

    /// <summary>
    /// Tests RefPack opcode forms against vectors decoded independently with the
    /// niotso-reading oracle (nfs-resources-converter RefPack), plus the 0x11FB
    /// header order taken from EA's official refdecode.cpp, so the suite does
    /// not rely solely on self-authored streams.
    /// </summary>
    /// <param name="payload">The RefPack payload starting at the 10FB-family marker.</param>
    /// <param name="expected">The expected decompressed bytes.</param>
    [Theory]
    [MemberData(nameof(OracleVerifiedRefPackVectors))]
    public void Decompress_OracleVerifiedRefPackVector_MatchesIndependentDecoder(byte[] payload, byte[] expected)
    {
        // Act
        var result = sut.Decompress(Envelop(payload));

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Equal(expected);
    }

    /// <summary>
    /// Tests that truncated RefPack input fails cleanly instead of overrunning.
    /// </summary>
    [Fact]
    public void Decompress_TruncatedRefPack_ReturnsFailure()
    {
        // Arrange: header claims 8 bytes but the stream ends mid-literal.
        var data = new byte[] { 0x10, 0xFB, 0x00, 0x00, 0x08, 0xE0, 0x41 };

        // Act
        var result = sut.Decompress(Envelop(data));

        // Assert
        result.Success.Should().BeFalse();
        result.Data.Should().BeNull();
    }

    /// <summary>
    /// Tests ZLib decompression against framework-produced deflate bytes.
    /// </summary>
    [Fact]
    public void Decompress_ZLibEnvelope_DecodesPayload()
    {
        // Arrange: build a ZL5 envelope with an independent deflate implementation.
        var raw = new byte[] { 0x43, 0x6B, 0x4D, 0x70, 0x01, 0x02, 0x03, 0x04 };
        using var deflated = new MemoryStream();
        using (var deflater = new DeflateStream(deflated, CompressionLevel.Optimal, true))
        {
            deflater.Write(raw, 0, raw.Length);
        }

        var envelope = new List<byte> { 0x5A, 0x4C, (byte)'5', 0x00 };
        envelope.AddRange(BitConverter.GetBytes(raw.Length));
        envelope.AddRange([0x78, 0x5E]);
        envelope.AddRange(deflated.ToArray());
        envelope.AddRange(Adler32BigEndian(raw));

        // Act
        var result = sut.Decompress([.. envelope]);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Equal(raw);
    }

    /// <summary>
    /// Tests ZLib decompression against an independent RFC 1950 (zlib) producer,
    /// matching what the engine's compress2 writes.
    /// </summary>
    [Fact]
    public void Decompress_IndependentZLibEnvelope_DecodesPayload()
    {
        // Arrange
        var raw = new byte[] { 0x43, 0x6B, 0x4D, 0x70, 0x01, 0x02, 0x03, 0x04 };
        var envelope = ZLibEnvelope(raw, 9);

        // Act
        var result = sut.Decompress(envelope);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Equal(raw);
    }

    /// <summary>
    /// Tests that unsupported envelopes fail with a named codec.
    /// </summary>
    [Fact]
    public void Decompress_UnsupportedEnvelope_ReturnsFailure()
    {
        // Arrange
        var data = new byte[] { 0x4E, 0x4F, 0x58, 0x00, 0x08, 0x00, 0x00, 0x00, 0x01, 0x02 };

        // Act
        var result = sut.Decompress(data);

        // Assert
        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("NOX");
    }

    /// <summary>
    /// Tests that a RefPack envelope wrapping a real table-of-contents
    /// decompresses to parseable chunk bytes.
    /// </summary>
    [Fact]
    public void Decompress_RefPackEnvelopeWithRealToc_Parses()
    {
        // Arrange
        var raw = BuildRawToc();
        var payload = RefPackEncodeLiterals(raw);
        var envelope = new List<byte> { 0x45, 0x41, 0x52, 0x00 };
        envelope.AddRange(BitConverter.GetBytes(raw.Length));
        envelope.AddRange(payload);

        // Act
        var decompressed = sut.Decompress([.. envelope]);

        // Assert
        decompressed.Success.Should().BeTrue();
        decompressed.Data.Should().Equal(raw);
        var parsed = MapChunkReader.TryParse(decompressed.Data!);
        parsed.Success.Should().BeTrue();
        parsed.Data!.TopLevel.Should().ContainSingle().Which.Label.Should().Be("WorldInfo");
    }

    /// <summary>
    /// Tests that a ZLib envelope wrapping a real table-of-contents
    /// decompresses to parseable chunk bytes.
    /// </summary>
    [Fact]
    public void Decompress_ZLibEnvelopeWithRealToc_Parses()
    {
        // Arrange
        var raw = BuildRawToc();
        var envelope = ZLibEnvelope(raw, 5);

        // Act
        var decompressed = sut.Decompress(envelope);

        // Assert
        decompressed.Success.Should().BeTrue();
        decompressed.Data.Should().Equal(raw);
        var parsed = MapChunkReader.TryParse(decompressed.Data!);
        parsed.Success.Should().BeTrue();
        parsed.Data!.TopLevel.Should().ContainSingle().Which.Label.Should().Be("WorldInfo");
    }

    /// <summary>
    /// Tests that the ZLib writer produces a readable envelope.
    /// </summary>
    [Fact]
    public void CompressZLib_RawBytes_RoundTrips()
    {
        // Arrange
        var raw = new byte[] { 0x43, 0x6B, 0x4D, 0x70, 0x01, 0x02, 0x03, 0x04 };

        // Act
        var compressed = sut.CompressZLib(raw, WorldBuilderConstants.Compression.DefaultZLibLevel);

        // Assert
        compressed.Success.Should().BeTrue();
        compressed.Data.Should().NotBeNull();
        compressed.Data![0].Should().Be(0x5A);
        var roundTripped = sut.Decompress(compressed.Data!);
        roundTripped.Success.Should().BeTrue();
        roundTripped.Data.Should().Equal(raw);
    }

    /// <summary>
    /// Tests that the requested level is written into the envelope magic
    /// with the matching zlib stream header.
    /// </summary>
    /// <param name="level">The ZLib level.</param>
    /// <param name="expectedFlg">The expected zlib FLG byte.</param>
    [Theory]
    [InlineData(1, 0x01)]
    [InlineData(5, 0x5E)]
    [InlineData(9, 0xDA)]
    public void CompressZLib_Level_WritesMatchingMagicAndHeader(int level, byte expectedFlg)
    {
        // Arrange
        var raw = new byte[] { 0x43, 0x6B, 0x4D, 0x70, 0x01, 0x02, 0x03, 0x04 };

        // Act
        var compressed = sut.CompressZLib(raw, level);

        // Assert
        compressed.Success.Should().BeTrue();
        compressed.Data![0].Should().Be(0x5A);
        compressed.Data![1].Should().Be(0x4C);
        compressed.Data![2].Should().Be((byte)('0' + level));
        compressed.Data![3].Should().Be(0x00);
        compressed.Data![8].Should().Be(0x78);
        compressed.Data![9].Should().Be(expectedFlg);
        compressed.Data![^4..].Should().Equal(Adler32BigEndian(raw));
    }

    /// <summary>
    /// Tests that out-of-range levels fail instead of writing a bad magic.
    /// </summary>
    /// <param name="level">The invalid ZLib level.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void CompressZLib_InvalidLevel_ReturnsFailure(int level)
    {
        // Arrange
        var raw = new byte[] { 0x43, 0x6B, 0x4D, 0x70 };

        // Act
        var compressed = sut.CompressZLib(raw, level);

        // Assert
        compressed.Success.Should().BeFalse();
        compressed.Data.Should().BeNull();
    }

    /// <summary>
    /// Tests that writer output decompresses with an independent RFC 1950
    /// implementation, proving the framing matches what the engine reads.
    /// </summary>
    [Fact]
    public void CompressZLib_Output_DecodesWithIndependentZLib()
    {
        // Arrange
        var raw = new byte[] { 0x43, 0x6B, 0x4D, 0x70, 0x01, 0x02, 0x03, 0x04 };

        // Act
        var compressed = sut.CompressZLib(raw, WorldBuilderConstants.Compression.DefaultZLibLevel);

        // Assert
        compressed.Success.Should().BeTrue();
        using var input = new MemoryStream(compressed.Data!, WorldBuilderConstants.Compression.EnvelopeHeaderSize, compressed.Data!.Length - WorldBuilderConstants.Compression.EnvelopeHeaderSize, false);
        using var reader = new ZLibStream(input, CompressionMode.Decompress);
        using var decoded = new MemoryStream();
        reader.CopyTo(decoded);
        decoded.ToArray().Should().Equal(raw);
    }

    /// <summary>
    /// Gets RefPack streams whose expected outputs were produced by the independent oracle.
    /// </summary>
    public static TheoryData<byte[], byte[]> OracleVerifiedRefPackVectors => new()
    {
        // Literal run plus a one-byte stop tail: "Hello, World!".
        {
            [0x10, 0xFB, 0x00, 0x00, 0x0D, 0xE2, 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x2C, 0x20, 0x57, 0x6F, 0x72, 0x6C, 0x64, 0xFD, 0x21],
            [0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x2C, 0x20, 0x57, 0x6F, 0x72, 0x6C, 0x64, 0x21]
        },

        // Short copy: "ABAB" then length 3 at offset 1.
        {
            [0x10, 0xFB, 0x00, 0x00, 0x07, 0xE0, 0x41, 0x42, 0x41, 0x42, 0x00, 0x01, 0xFC],
            [0x41, 0x42, 0x41, 0x42, 0x41, 0x42, 0x41]
        },

        // Int copy: "WXYZ" then length 4 at offset 3.
        {
            [0x10, 0xFB, 0x00, 0x00, 0x08, 0xE0, 0x57, 0x58, 0x59, 0x5A, 0x80, 0x00, 0x03, 0xFC],
            [0x57, 0x58, 0x59, 0x5A, 0x57, 0x58, 0x59, 0x5A]
        },

        // Long copy: "1234" then length 5 at offset 3.
        {
            [0x10, 0xFB, 0x00, 0x00, 0x09, 0xE0, 0x31, 0x32, 0x33, 0x34, 0xC0, 0x00, 0x03, 0x00, 0xFC],
            [0x31, 0x32, 0x33, 0x34, 0x31, 0x32, 0x33, 0x34, 0x31]
        },

        // 0x11FB header: compressed size precedes the uncompressed size per EA refdecode,
        // then literals plus a three-byte stop tail.
        {
            [0x11, 0xFB, 0x00, 0x00, 0x09, 0x00, 0x00, 0x07, 0xE0, 0x61, 0x62, 0x63, 0x64, 0xFF, 0x58, 0x59, 0x5A],
            [0x61, 0x62, 0x63, 0x64, 0x58, 0x59, 0x5A]
        },
    };

    private static byte[] Envelop(byte[] payload)
    {
        var envelope = new List<byte> { 0x45, 0x41, 0x52, 0x00 };
        envelope.AddRange(BitConverter.GetBytes(0));
        envelope.AddRange(payload);
        return [.. envelope];
    }

    private static byte[] BuildRawToc()
    {
        var writer = new MapChunkWriter();
        writer.OpenChunk("WorldInfo", 1);
        writer.WriteInt(42);
        writer.CloseChunk();
        return writer.ToFileBytes();
    }

    private static byte[] ZLibEnvelope(byte[] raw, int level)
    {
        using var deflated = new MemoryStream();
        using (var compressor = new ZLibStream(deflated, CompressionLevel.Optimal, true))
        {
            compressor.Write(raw, 0, raw.Length);
        }

        var envelope = new List<byte> { 0x5A, 0x4C, (byte)('0' + level), 0x00 };
        envelope.AddRange(BitConverter.GetBytes(raw.Length));
        envelope.AddRange(deflated.ToArray());
        return [.. envelope];
    }

    private static byte[] RefPackEncodeLiterals(byte[] raw)
    {
        var payload = new List<byte> { 0x10, 0xFB, (byte)(raw.Length >> 16), (byte)(raw.Length >> 8), (byte)raw.Length };
        var position = 0;
        while (raw.Length - position >= 4)
        {
            var run = Math.Min(112, ((raw.Length - position) / 4) * 4);
            payload.Add((byte)(0xE0 + ((run - 4) / 4)));
            for (var i = 0; i < run; i++)
            {
                payload.Add(raw[position++]);
            }
        }

        var tail = raw.Length - position;
        payload.Add((byte)(0xFC + tail));
        for (var i = 0; i < tail; i++)
        {
            payload.Add(raw[position++]);
        }

        return [.. payload];
    }

    private static byte[] Adler32BigEndian(byte[] data)
    {
        uint a = 1;
        uint b = 0;
        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        var adler = (b << 16) | a;
        return [(byte)(adler >> 24), (byte)(adler >> 16), (byte)(adler >> 8), (byte)adler];
    }
}
