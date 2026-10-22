using GenHub.Core.Models.Enums;
using GenHub.Core.Services.Tools.Checksum;
using System.Text;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Unit tests for SAGE checksum calculation algorithms, INI normalizer, BIG archive parser, and PE version extractor.
/// </summary>
public sealed class SageChecksumTests
{
    /// <summary>
    /// Verifies LegacyChecksum accumulator produces expected SAGE ROL-1 values.
    /// </summary>
    [Fact]
    public void LegacyChecksum_CalculatesExpectedValues()
    {
        var checksum = new LegacyChecksum();
        Assert.Equal(0u, checksum.Value);

        // Single byte 0x42
        checksum.Add(new byte[] { 0x42 });
        Assert.Equal(0x42u, checksum.Value);

        // Next byte 0x01: (0x42 << 1) + 1 = 0x85
        checksum.Add(new byte[] { 0x01 });
        Assert.Equal(0x85u, checksum.Value);

        checksum.Reset();
        Assert.Equal(0u, checksum.Value);
    }

    /// <summary>
    /// Verifies LegacyChecksum high-bit rotation into the low bit.
    /// </summary>
    [Fact]
    public void LegacyChecksum_HighBitRotatesCorrectly()
    {
        var checksum = new LegacyChecksum();

        // 0x80000000 shifted left by 1 should rotate high bit to 1
        // 0x80 is bit 7; after 24 one-bit ROL iterations (24 zero bytes), it reaches bit 31 (0x80000000).
        checksum.Add(new byte[] { 0x80 });
        for (int i = 0; i < 24; i++)
        {
            checksum.Add(new byte[] { 0x00 });
        }

        // Checksum now has 0x80000000
        uint current = checksum.Value;
        Assert.Equal(0x80000000u, current);

        // Next step: (0x80000000 << 1) + 0 + (0x80000000 >> 31) = 1
        checksum.Add(new byte[] { 0x00 });
        Assert.Equal(1u, checksum.Value);
    }

    /// <summary>
    /// Verifies XferChecksum 4-byte chunk BE accumulation and endian reversal.
    /// </summary>
    [Fact]
    public void XferChecksum_AccumulatesChunksAndAppliesEndianReversal()
    {
        var checksum = new XferChecksum();
        Assert.Equal(0u, checksum.Value);

        // 4 bytes: [0x12, 0x34, 0x56, 0x78] -> Big-endian chunk 0x12345678
        // Value: ReverseEndianness(0x12345678) = 0x78563412
        checksum.Add(new byte[] { 0x12, 0x34, 0x56, 0x78 });
        Assert.Equal(0x78563412u, checksum.Value);

        checksum.Reset();
        Assert.Equal(0u, checksum.Value);
    }

    /// <summary>
    /// Verifies XferChecksum partial tail handling (1, 2, and 3 bytes).
    /// </summary>
    /// <param name="tailLength">The length of the partial tail chunk.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void XferChecksum_HandlesPartialTails(int tailLength)
    {
        var checksum = new XferChecksum();
        byte[] data = new byte[tailLength];
        for (int i = 0; i < tailLength; i++)
        {
            data[i] = (byte)(i + 1);
        }

        checksum.Add(data);
        Assert.NotEqual(0u, checksum.Value);
    }

    /// <summary>
    /// Verifies IniNormalizer strips comments and converts control characters to spaces.
    /// </summary>
    [Fact]
    public void IniNormalizer_StripsCommentsAndNormalizesControlCharacters()
    {
        string rawIni = "GameData\r\n  Windowed = Yes ; inline comment\r\n\tMaxFPS = 60\r\n; entire comment line\r\nEnd\n";
        byte[] rawBytes = Encoding.ASCII.GetBytes(rawIni);

        var lines = new List<string>();
        IniNormalizer.ProcessLines(rawBytes, line => lines.Add(Encoding.ASCII.GetString(line)));

        Assert.Equal(4, lines.Count);
        Assert.Equal("GameData ", lines[0]); // \r was converted to space
        Assert.Equal("  Windowed = Yes ", lines[1]); // Comment stripped, trailing space kept
        Assert.Equal(" MaxFPS = 60 ", lines[2]); // \t and \r converted to space
        Assert.Equal("End", lines[3]); // trailing newline stripped
    }

    /// <summary>
    /// Verifies PeVersionExtractor parses synthetic MSVC Version::setVersion bytecode.
    /// </summary>
    [Fact]
    public void PeVersionExtractor_ExtractsSyntheticMsvcPattern()
    {
        // Construct byte sequence matching MatchMsvc
        // consumePush (0x6A 0x00) -> consumePush (0x6A 0x00) -> 0x6A minor -> 0x6A major -> 0xC6 0x45 ?? 0x00 0x8B 0x0D ?? ?? ?? ?? 0xE8
        byte[] pattern =
        [
            0x6A, 0x00, // push
            0x6A, 0x00, // push
            0x6A, 0x04, // minor = 4
            0x6A, 0x01, // major = 1
            0xC6, 0x45, 0x10, 0x00,
            0x8B, 0x0D, 0x11, 0x22, 0x33, 0x44,
            0xE8, 0x55, 0x66, 0x77, 0x88
        ];

        bool extracted = PeVersionExtractor.TryExtract(pattern, out int major, out int minor);
        Assert.True(extracted);
        Assert.Equal(1, major);
        Assert.Equal(4, minor);
    }

    /// <summary>
    /// Verifies GameCrcCalculatorService returns failure when executable does not exist.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task GameCrcCalculatorService_MissingExecutable_ReturnsFailureAsync()
    {
        var service = new GameCrcCalculatorService();
        var result = await service.CalculateExeCrcAsync("non_existent_file.exe");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    /// <summary>
    /// Verifies GameCrcCalculatorService returns failure when game root does not exist.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task GameCrcCalculatorService_MissingGameRoot_ReturnsFailureAsync()
    {
        var service = new GameCrcCalculatorService();
        var result = await service.CalculateIniCrcAsync("non_existent_dir", GameType.ZeroHour);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    /// <summary>
    /// Verifies BigArchiveReader throws InvalidDataException on corrupt or truncated header.
    /// </summary>
    [Fact]
    public void BigArchiveReader_CorruptedHeader_ThrowsInvalidDataException()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // Construct archive with declared header larger than stream length
            File.WriteAllBytes(tempFile, [0x42, 0x49, 0x47, 0x46, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x10, 0x00]);
            Assert.Throws<InvalidDataException>(() => BigArchiveReader.ReadIndex(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Verifies PeVersionExtractor returns false gracefully on non-existent file path.
    /// </summary>
    [Fact]
    public void PeVersionExtractor_NonExistentFile_ReturnsFalse()
    {
        bool extracted = PeVersionExtractor.TryExtractFromVersionInfo("non_existent_file.exe", out int major, out int minor);
        Assert.False(extracted);
        Assert.Equal(0, major);
        Assert.Equal(0, minor);
    }

    /// <summary>
    /// Verifies XferChecksum produces golden vector values for empty input, single byte, multi-block, and normalized INI content.
    /// </summary>
    [Fact]
    public void XferChecksum_GoldenVectors_ProduceExpectedChecksums()
    {
        // 1. Empty input
        var checksum = new XferChecksum();
        checksum.Add(ReadOnlySpan<byte>.Empty);
        Assert.Equal(0x00000000u, checksum.Value);

        // 2. Single byte [0x42]
        checksum.Reset();
        checksum.Add(new byte[] { 0x42 });
        Assert.Equal(0x42000000u, checksum.Value);

        // 3. Four bytes [0x12, 0x34, 0x56, 0x78]
        checksum.Reset();
        checksum.Add(new byte[] { 0x12, 0x34, 0x56, 0x78 });
        Assert.Equal(0x78563412u, checksum.Value);

        // 4. Multi-block ASCII string "CommandAndConquer"
        checksum.Reset();
        checksum.Add(Encoding.ASCII.GetBytes("CommandAndConquer"));
        Assert.Equal(0xD5F1DDD8u, checksum.Value);

        // 5. Normalized INI content processed through IniNormalizer
        checksum.Reset();
        string sampleIni = "GameData\r\n  Windowed = Yes ; comment\r\n  MaxFPS = 60\r\nEnd\n";
        IniNormalizer.ProcessLines(Encoding.ASCII.GetBytes(sampleIni), lineSpan => checksum.Add(lineSpan));
        Assert.Equal(0xA52E92D5u, checksum.Value);
    }

    /// <summary>
    /// Verifies that GameCrcCalculatorService invalidates cached INI CRC when game data files are modified.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CalculateIniCrcAsync_WhenDataChanges_InvalidatesCacheAutomaticallyAsync()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "GenHub_IniCrcTest_" + Guid.NewGuid().ToString("N"));
        string iniDir = Path.Combine(tempDir, "Data", "INI");
        Directory.CreateDirectory(iniDir);

        try
        {
            GameCrcCalculatorService.ClearCache();
            var service = new GameCrcCalculatorService();
            string iniPath = Path.Combine(iniDir, "GameData.ini");

            await File.WriteAllTextAsync(iniPath, "GameData\r\n  Windowed = Yes\r\nEnd\r\n");
            File.SetLastWriteTimeUtc(iniPath, DateTime.UtcNow.AddMinutes(-10));

            var result1 = await service.CalculateIniCrcAsync(tempDir, GameType.ZeroHour);
            Assert.True(result1.Success);

            // Second call with untouched files returns cached result
            var resultCached = await service.CalculateIniCrcAsync(tempDir, GameType.ZeroHour);
            Assert.True(resultCached.Success);
            Assert.Equal(result1.Data, resultCached.Data);

            // Modify the INI file with new content and a newer timestamp
            await File.WriteAllTextAsync(iniPath, "GameData\r\n  Windowed = No\r\n  MaxFPS = 144\r\nEnd\r\n");
            File.SetLastWriteTimeUtc(iniPath, DateTime.UtcNow);

            var result2 = await service.CalculateIniCrcAsync(tempDir, GameType.ZeroHour);
            Assert.True(result2.Success);
            Assert.NotEqual(result1.Data, result2.Data);
        }
        finally
        {
            GameCrcCalculatorService.ClearCache();
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
