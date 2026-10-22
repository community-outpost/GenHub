using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Features.Tools.ReplayManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Unit tests for binary .rep replay header parser.
/// </summary>
public sealed class ReplayHeaderParserTests
{
    private readonly ReplayHeaderParser _parser = new(NullLogger<ReplayHeaderParser>.Instance);

    /// <summary>
    /// Verifies that a valid replay stream parses all header metadata accurately with the Recorder.cpp binary layout.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseHeaderAsync_ValidReplayStream_ExtractsMetadataSuccessfullyAsync()
    {
        // Construct valid GENREP stream
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // 1. Magic "GENREP" (6 bytes)
        writer.Write(Encoding.ASCII.GetBytes("GENREP"));

        // 2. Fixed fields (22 bytes: startTime 4, endTime 4, frameCount 4, flags 2, pad 8)
        writer.Write(100u);
        writer.Write(200u);
        writer.Write(300u);
        writer.Write((byte)1);
        writer.Write((byte)2);
        writer.Write(new byte[8]);

        // 3. Replay Title / Name UTF-16LE null terminated (written first by RecorderClass)
        writer.Write(Encoding.Unicode.GetBytes("Test Match Replay" + char.MinValue));

        // 4. Skip 16 bytes (SYSTEMTIME timestamp)
        writer.Write(new byte[16]);

        // 5. Version string UTF-16LE null terminated
        writer.Write(Encoding.Unicode.GetBytes("Version 1.04" + char.MinValue));

        // 6. BuildTimeString UTF-16LE null terminated
        writer.Write(Encoding.Unicode.GetBytes("Aug 21 2026" + char.MinValue));

        // 7. VersionNumber, exeCRC, iniCRC
        writer.Write(20260821u);
        writer.Write(0x27533BB0u);
        writer.Write(0x76B251A3u);

        // 8. InitString ASCII null terminated with colon-separated slots
        writer.Write(Encoding.ASCII.GetBytes("M=maps/defcon6/defcon6.map;S=HPlayerOne,127.0.0.1,8086,0,1,2:HPlayerTwo,127.0.0.1,8087,0,3,4:CE,5,6,0,1:X:X;" + char.MinValue));

        writer.Flush();
        stream.Position = 0;

        var result = await _parser.ParseHeaderAsync(stream);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.NotNull(result.Data);
        Assert.Equal("Test Match Replay", result.Data.Title);
        Assert.Equal("Version 1.04", result.Data.VersionString);
        Assert.Equal("Aug 21 2026", result.Data.BuildTimeString);
        Assert.Equal(20260821u, result.Data.VersionNumber);
        Assert.Equal(0x27533BB0u, result.Data.ExeCrc);
        Assert.Equal(0x76B251A3u, result.Data.IniCrc);
        Assert.Equal("0x27533BB0", result.Data.FormattedExeCrc);
        Assert.Equal("0x76B251A3", result.Data.FormattedIniCrc);
        Assert.Equal("defcon6", result.Data.MapName);
        Assert.Equal(300u, result.Data.TotalFrames);
        Assert.Equal(30, result.Data.FramesPerSecond);
        Assert.NotNull(result.Data.Duration);
        Assert.Equal(TimeSpan.FromSeconds(10), result.Data.Duration.Value);
        Assert.NotNull(result.Data.Players);
        Assert.Equal(3, result.Data.Players.Count);
        Assert.Contains("PlayerOne", result.Data.Players);
        Assert.Contains("PlayerTwo", result.Data.Players);
        Assert.Contains("AI (Easy)", result.Data.Players);

        Assert.NotNull(result.Data.Slots);
        Assert.Equal(3, result.Data.Slots.Count);
        Assert.Equal("PlayerOne", result.Data.Slots[0].PlayerName);
        Assert.True(result.Data.Slots[0].IsHuman);
        Assert.Equal(1, result.Data.Slots[0].ColorIndex);
        Assert.Equal(2, result.Data.Slots[0].FactionIndex);

        Assert.Equal("AI (Easy)", result.Data.Slots[2].PlayerName);
        Assert.False(result.Data.Slots[2].IsHuman);
        Assert.Equal(5, result.Data.Slots[2].ColorIndex);
        Assert.Equal(6, result.Data.Slots[2].FactionIndex);
    }

    /// <summary>
    /// Verifies that an invalid magic header returns a failure result.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseHeaderAsync_InvalidMagic_ReturnsFailureAsync()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("INVALID_HEADER_DATA_STREAM_TEST_LONG_ENOUGH"));

        var result = await _parser.ParseHeaderAsync(stream);

        Assert.False(result.Success);
        Assert.Contains("Invalid replay file magic header", result.FirstError);
    }

    /// <summary>
    /// Verifies that a truncated header stream returns a failure result.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseHeaderAsync_TooShort_ReturnsFailureAsync()
    {
        using var stream = new MemoryStream(new byte[10]);

        var result = await _parser.ParseHeaderAsync(stream);

        Assert.False(result.Success);
        Assert.Contains("too small", result.FirstError);
    }

    /// <summary>
    /// Verifies that a non-existent file path returns a failure result.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseHeaderAsync_NonExistentFile_ReturnsFailureAsync()
    {
        var result = await _parser.ParseHeaderAsync("/non/existent/path/replay.rep");

        Assert.False(result.Success);
        Assert.Contains("not found", result.FirstError);
    }

    /// <summary>
    /// Verifies that player slot markers are correctly stripped and colon-separated empty slot markers are ignored.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseHeaderAsync_SlotPrefixAndEmptySlotMarkers_ExtractsCleanPlayerNamesAsync()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // 1. Magic "GENREP"
        writer.Write(Encoding.ASCII.GetBytes("GENREP"));

        // 2. Fixed fields (22 bytes)
        writer.Write(new byte[22]);

        // 3. Title UTF-16LE null terminated
        writer.Write(Encoding.Unicode.GetBytes("Slot Test" + char.MinValue));

        // 4. Skip 16 bytes
        writer.Write(new byte[16]);

        // 5. Version string UTF-16LE null terminated
        writer.Write(Encoding.Unicode.GetBytes("1.04" + char.MinValue));

        // 6. VersionTimeString UTF-16LE null terminated
        writer.Write(Encoding.Unicode.GetBytes("Aug 21 2026" + char.MinValue));

        // 7. VersionNumber, exeCRC, iniCRC
        writer.Write(20260821u);
        writer.Write(0x27533BB0u);
        writer.Write(0x76B251A3u);

        // 8. InitString with single-char empty marker S=H, and prefix player S=HOcelot in colon list
        writer.Write(Encoding.ASCII.GetBytes("M=maps/test/test.map;S=H,0,0,1:HOcelot,0,0,2:CCommander,0,0,3:X:X;" + char.MinValue));

        writer.Flush();
        stream.Position = 0;

        var result = await _parser.ParseHeaderAsync(stream);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.NotNull(result.Data);
        Assert.NotNull(result.Data.Players);
        Assert.Equal(2, result.Data.Players.Count);
        Assert.Contains("Ocelot", result.Data.Players);
        Assert.Contains("Commander", result.Data.Players);
    }

    /// <summary>
    /// Verifies that player names starting with 'H' or 'C' (e.g. Hank, Clint, captain, Chuck)
    /// are not mangled by the slot status marker parsing, and that direct H= tokens preserve names.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseHeaderAsync_PlayerNamesStartingWithHorC_PreservesRealPlayerNamesAsync()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("GENREP"));
        writer.Write(new byte[22]);
        writer.Write(Encoding.Unicode.GetBytes("Names Test" + char.MinValue));
        writer.Write(new byte[16]);
        writer.Write(Encoding.Unicode.GetBytes("1.04" + char.MinValue));
        writer.Write(Encoding.Unicode.GetBytes("Aug 21 2026" + char.MinValue));
        writer.Write(20260821u);
        writer.Write(0x27533BB0u);
        writer.Write(0x76B251A3u);

        // S= slots have uppercase 'H' or 'C' prefix added by Generals engine before player name:
        // HHank -> Hank, HClint -> Clint, Hcaptain -> captain, CChuck -> Chuck
        // Standalone markers X and O are ignored as closed/open slots.
        writer.Write(Encoding.ASCII.GetBytes("M=maps/test/test.map;S=HHank,0,0,1:HClint,0,0,2:Hcaptain,0,0,3:CChuck,0,0,4:X:O;" + char.MinValue));

        writer.Flush();
        stream.Position = 0;

        var result = await _parser.ParseHeaderAsync(stream);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.NotNull(result.Data);
        Assert.NotNull(result.Data.Players);
        Assert.Equal(4, result.Data.Players.Count);
        Assert.Equal("Hank", result.Data.Players[0]);
        Assert.Equal("Clint", result.Data.Players[1]);
        Assert.Equal("captain", result.Data.Players[2]);
        Assert.Equal("Chuck", result.Data.Players[3]);
    }

    /// <summary>
    /// Verifies that a direct host/human token (H=) preserves the name without stripping leading 'H' or 'C'.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseHeaderAsync_DirectHostToken_PreservesPlayerNameAsync()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("GENREP"));
        writer.Write(new byte[22]);
        writer.Write(Encoding.Unicode.GetBytes("Direct Host Test" + char.MinValue));
        writer.Write(new byte[16]);
        writer.Write(Encoding.Unicode.GetBytes("1.04" + char.MinValue));
        writer.Write(Encoding.Unicode.GetBytes("Aug 21 2026" + char.MinValue));
        writer.Write(20260821u);
        writer.Write(0x27533BB0u);
        writer.Write(0x76B251A3u);

        // H= token provides player/host name directly without S= slot prefix
        writer.Write(Encoding.ASCII.GetBytes("M=maps/test/test.map;H=Hank;" + char.MinValue));

        writer.Flush();
        stream.Position = 0;

        var result = await _parser.ParseHeaderAsync(stream);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.NotNull(result.Data);
        Assert.NotNull(result.Data.Players);
        Assert.Single(result.Data.Players);
        Assert.Equal("Hank", result.Data.Players[0]);
    }

    /// <summary>
    /// Verifies that a replay stream containing the 60Hz keyword without GeneralsOnline correctly detects 60 FPS tick rate.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseHeaderAsync_When60HzKeywordPresentWithoutGeneralsOnline_Extracts60FpsAndDurationAsync()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("GENREP"));
        writer.Write(1000u);
        writer.Write(1100u);
        writer.Write(6000u);
        writer.Write((byte)1);
        writer.Write((byte)2);
        writer.Write(new byte[8]);

        writer.Write(Encoding.Unicode.GetBytes("Standard Match" + char.MinValue));
        writer.Write(new byte[16]);
        writer.Write(Encoding.Unicode.GetBytes("Version 1.04 (60Hz)" + char.MinValue));
        writer.Write(Encoding.Unicode.GetBytes("Aug 21 2026" + char.MinValue));
        writer.Write(20260821u);
        writer.Write(0x27533BB0u);
        writer.Write(0x76B251A3u);
        writer.Write(Encoding.ASCII.GetBytes("M=maps/defcon6/defcon6.map;H=PlayerOne;" + char.MinValue));

        writer.Flush();
        stream.Position = 0;

        var result = await _parser.ParseHeaderAsync(stream);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.NotNull(result.Data);
        Assert.Equal(6000u, result.Data.TotalFrames);
        Assert.Equal(60, result.Data.FramesPerSecond);
        Assert.NotNull(result.Data.Duration);
        Assert.Equal(TimeSpan.FromSeconds(100), result.Data.Duration.Value);
    }

    /// <summary>
    /// Verifies that a replay stream containing the GeneralsOnline keyword without 60Hz correctly detects 60 FPS tick rate.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseHeaderAsync_WhenGeneralsOnlineKeywordPresentWithout60Hz_Extracts60FpsAndDurationAsync()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("GENREP"));
        writer.Write(1000u);
        writer.Write(1100u);
        writer.Write(6000u);
        writer.Write((byte)1);
        writer.Write((byte)2);
        writer.Write(new byte[8]);

        writer.Write(Encoding.Unicode.GetBytes("Standard Match" + char.MinValue));
        writer.Write(new byte[16]);
        writer.Write(Encoding.Unicode.GetBytes("GeneralsOnline 1.04" + char.MinValue));
        writer.Write(Encoding.Unicode.GetBytes("Aug 21 2026" + char.MinValue));
        writer.Write(20260821u);
        writer.Write(0x27533BB0u);
        writer.Write(0x76B251A3u);
        writer.Write(Encoding.ASCII.GetBytes("M=maps/defcon6/defcon6.map;H=PlayerOne;" + char.MinValue));

        writer.Flush();
        stream.Position = 0;

        var result = await _parser.ParseHeaderAsync(stream);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.NotNull(result.Data);
        Assert.Equal(6000u, result.Data.TotalFrames);
        Assert.Equal(60, result.Data.FramesPerSecond);
        Assert.NotNull(result.Data.Duration);
        Assert.Equal(TimeSpan.FromSeconds(100), result.Data.Duration.Value);
    }

    /// <summary>
    /// Verifies that unterminated UTF-16 strings fail gracefully without throwing.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseHeaderAsync_UnterminatedUtf16String_ReturnsFailureAsync()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("GENREP"));
        writer.Write(new byte[22]);

        // Write non-null terminated string filled with 0xFF
        writer.Write(new byte[100]);
        for (var i = 28; i < 128; i++)
        {
            stream.GetBuffer()[i] = 0x41;
        }

        stream.Position = 0;

        var result = await _parser.ParseHeaderAsync(stream);

        Assert.False(result.Success);
        Assert.Contains("Unterminated UTF-16", result.FirstError);
    }

    /// <summary>
    /// Verifies that a stream longer than the import size limit still parses, because only the header is read.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseHeaderAsync_StreamExceedingMaxReplaySizeBytes_ParsesHeaderAsync()
    {
        using var header = new MemoryStream();
        WriteMinimalValidHeader(header);
        using var stream = new OversizedStream(header.ToArray());

        var result = await _parser.ParseHeaderAsync(stream);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.Equal("Long Match", result.Data!.Title);
        Assert.Equal("defcon6", result.Data.MapName);
    }

    /// <summary>
    /// Verifies that a replay file larger than the import size limit still parses, because only the header is read.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseHeaderAsync_FileExceedingMaxReplaySizeBytes_ParsesHeaderAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GenHubReplayParser", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, "long.rep");
        try
        {
            await using (var file = File.Create(filePath))
            {
                WriteMinimalValidHeader(file);
                file.SetLength(ReplayManagerConstants.MaxReplaySizeBytes + 1);
            }

            var result = await _parser.ParseHeaderAsync(filePath);

            Assert.True(result.Success, string.Join(" ", result.Errors));
            Assert.Equal("Long Match", result.Data!.Title);
            Assert.Equal("defcon6", result.Data.MapName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that valid SYSTEMTIME timestamp is parsed as DateTimeKind.Unspecified to avoid timezone double-shifting.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseHeaderAsync_ValidSystemTime_ParsesGameDateWithUnspecifiedKindAsync()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("GENREP"));
        writer.Write(new byte[22]);
        writer.Write(Encoding.Unicode.GetBytes("Timestamp Test" + char.MinValue));

        // SYSTEMTIME: wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds
        writer.Write((ushort)2026);
        writer.Write((ushort)9);
        writer.Write((ushort)1);
        writer.Write((ushort)14);
        writer.Write((ushort)14);
        writer.Write((ushort)30);
        writer.Write((ushort)45);
        writer.Write((ushort)123);

        writer.Write(Encoding.Unicode.GetBytes("1.04" + char.MinValue));
        writer.Write(Encoding.Unicode.GetBytes("Aug 21 2026" + char.MinValue));
        writer.Write(20260821u);
        writer.Write(0x27533BB0u);
        writer.Write(0x76B251A3u);
        writer.Write(Encoding.ASCII.GetBytes("M=maps/test/test.map;H=Hank;" + char.MinValue));

        writer.Flush();
        stream.Position = 0;

        var result = await _parser.ParseHeaderAsync(stream);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.NotNull(result.Data);
        Assert.NotNull(result.Data.GameDate);
        Assert.Equal(DateTimeKind.Unspecified, result.Data.GameDate.Value.Kind);
        Assert.Equal(new DateTime(2026, 9, 14, 14, 30, 45, 123, DateTimeKind.Unspecified), result.Data.GameDate.Value);
    }

    /// <summary>
    /// Verifies that when HeaderFrameCount is 0, fallback chunk scanning parses commands
    /// using table-driven argument sizes (including TEAMID 0x5 and WIDECHAR 0xA) and accurately resolves max timecode.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseHeaderAsync_FallbackChunkArgSizeTeamIdAndWidechar_PinsArgSizeAndParsesMaxTimecodeAsync()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // Header
        writer.Write(Encoding.ASCII.GetBytes("GENREP"));
        writer.Write(1000u); // startTime
        writer.Write(1100u); // endTime
        writer.Write(0u);    // HeaderFrameCount = 0 to trigger chunk scan fallback
        writer.Write((byte)1);
        writer.Write((byte)2);
        writer.Write(new byte[8]);

        // Title
        writer.Write(Encoding.Unicode.GetBytes("Chunk Fallback Test" + char.MinValue));

        // SYSTEMTIME
        writer.Write(new byte[16]);

        // Version & Build time
        writer.Write(Encoding.Unicode.GetBytes("1.04" + char.MinValue));
        writer.Write(Encoding.Unicode.GetBytes("Aug 21 2026" + char.MinValue));
        writer.Write(20260821u);
        writer.Write(0x27533BB0u);
        writer.Write(0x76B251A3u);

        // Initial setup string
        writer.Write(Encoding.ASCII.GetBytes("M=maps/test/test.map;H=Hank;" + char.MinValue));

        // Trailer: null-terminated local player index string + 16 bytes fixed trailer
        writer.Write(Encoding.ASCII.GetBytes("0" + char.MinValue));
        writer.Write(new byte[16]);

        // Chunk 1:
        // Header (13 bytes): timecode (4), command (4), number (4), ncomms (1)
        writer.Write(120u); // timecode = 120
        writer.Write(1u);   // command
        writer.Write(1u);   // number
        writer.Write((byte)2); // ncomms = 2

        // Command 0: 0x5 (TEAMID, 4 bytes), nargs = 1 -> payload 4 bytes
        writer.Write((byte)0x5); // cmdType TEAMID
        writer.Write((byte)1);   // nargs = 1

        // Command 1: 0xA (WIDECHAR, 2 bytes), nargs = 2 -> payload 4 bytes
        writer.Write((byte)0xA); // cmdType WIDECHAR
        writer.Write((byte)2);   // nargs = 2

        // Payload for Command 0 (TEAMID): 4 bytes
        writer.Write(12345u);

        // Payload for Command 1 (WIDECHAR): 2 * 2 = 4 bytes
        writer.Write((ushort)'A');
        writer.Write((ushort)'B');

        // Chunk 2:
        // Header (13 bytes)
        writer.Write(240u); // timecode = 240
        writer.Write(1u);
        writer.Write(2u);
        writer.Write((byte)0); // ncomms = 0

        writer.Flush();
        stream.Position = 0;

        var result = await _parser.ParseHeaderAsync(stream);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.NotNull(result.Data);
        Assert.Equal(240u, result.Data.TotalFrames);
        Assert.Equal(30, result.Data.FramesPerSecond);
        Assert.NotNull(result.Data.Duration);
        Assert.Equal(TimeSpan.FromSeconds(8), result.Data.Duration.Value);
    }

    /// <summary>
    /// Verifies that when a chunk header specifies a payload extending beyond available bytes,
    /// its timecode is not counted into maxTimecode and parsing halts safely.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseHeaderAsync_FallbackChunkWithTruncatedPayload_DoesNotCountTruncatedChunkTimecodeAsync()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // Header with HeaderFrameCount = 0
        writer.Write(Encoding.ASCII.GetBytes("GENREP"));
        writer.Write(1000u);
        writer.Write(1100u);
        writer.Write(0u);
        writer.Write((byte)1);
        writer.Write((byte)2);
        writer.Write(new byte[8]);

        writer.Write(Encoding.Unicode.GetBytes("Truncated Chunk Test" + char.MinValue));
        writer.Write(new byte[16]);
        writer.Write(Encoding.Unicode.GetBytes("1.04" + char.MinValue));
        writer.Write(Encoding.Unicode.GetBytes("Aug 21 2026" + char.MinValue));
        writer.Write(20260821u);
        writer.Write(0x27533BB0u);
        writer.Write(0x76B251A3u);
        writer.Write(Encoding.ASCII.GetBytes("M=maps/test/test.map;H=Hank;" + char.MinValue));
        writer.Write(Encoding.ASCII.GetBytes("0" + char.MinValue));
        writer.Write(new byte[16]);

        // Chunk 1: valid chunk with timecode = 150
        writer.Write(150u);
        writer.Write(1u);
        writer.Write(1u);
        writer.Write((byte)0); // ncomms = 0

        // Chunk 2: invalid chunk with timecode = 9999, but payload truncated
        writer.Write(9999u);
        writer.Write(1u);
        writer.Write(2u);
        writer.Write((byte)1); // ncomms = 1
        writer.Write((byte)0); // type = INTEGER (4 bytes)
        writer.Write((byte)10); // nargs = 10 -> expects 40 bytes payload, but stream ends here!

        writer.Flush();
        stream.Position = 0;

        var result = await _parser.ParseHeaderAsync(stream);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.NotNull(result.Data);
        Assert.Equal(150u, result.Data.TotalFrames);
        Assert.Equal(TimeSpan.FromSeconds(5), result.Data.Duration);
    }

    /// <summary>
    /// Verifies that duplicate AI slots of the same difficulty appear once per slot in the player names,
    /// so the names tooltip agrees with the player count.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseHeaderAsync_DuplicateAiSlots_PlayerNamesAgreeWithPlayerCountAsync()
    {
        using var stream = new MemoryStream();
        WriteHeaderWithInitString(stream, "M=maps/test/test.map;S=Hbabai,0,0,0,1,2:HILnur,0,0,0,3,4:CH,5,6,0,1:CH,7,8,0,1:X:X:X:X;");
        stream.Position = 0;

        var result = await _parser.ParseHeaderAsync(stream);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        var replay = new ReplayFile
        {
            FullPath = "test.rep",
            FileName = "test.rep",
            SizeInBytes = stream.Length,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = result.Data,
        };

        Assert.Equal(4, replay.PlayerCount);
        Assert.Equal("babai, ILnur, AI (Hard), AI (Hard)", replay.PlayerNamesDisplay);
    }

    /// <summary>
    /// Verifies that the map CRC is read as hexadecimal and the map size as decimal from the init string.
    /// </summary>
    /// <param name="initString">The replay init string.</param>
    /// <param name="expectedCrc">The expected map CRC.</param>
    /// <param name="expectedSize">The expected map size in bytes.</param>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData("M=00maps/tansooo;MC=6314F5FD;MS=106110;SD=1;C=100;", 1662318077u, 106110u)]
    [InlineData("M=03maps/[rank] arctic arena zh v1;MC=43396DD8;MS=162490;SD=1;", 1127837144u, 162490u)]
    [InlineData("M=03maps/[aod] the persian gulf;MC=C33D1C4;MS=111694;", 0x0C33D1C4u, 111694u)]
    [InlineData("M=maps/x/x.map;mc=6314f5fd;ms=106110;", 1662318077u, 106110u)]
    [InlineData("M=maps/x/x.map;MC=0x6314F5FD;MS=106110;", 1662318077u, 106110u)]
    [InlineData("MS=106110;MC=6314F5FD;M=maps/x/x.map;", 1662318077u, 106110u)]
    public async Task ParseHeaderAsync_MapCrcAndSizeTokens_ParsesHexCrcAndDecimalSizeAsync(string initString, uint expectedCrc, uint expectedSize)
    {
        using var stream = new MemoryStream();
        WriteHeaderWithInitString(stream, initString);
        stream.Position = 0;

        var result = await _parser.ParseHeaderAsync(stream);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.NotNull(result.Data);
        Assert.Equal(expectedCrc, result.Data.MapCrc);
        Assert.Equal(expectedSize, result.Data.MapSize);
        Assert.Equal($"0x{expectedCrc:X8}", result.Data.FormattedMapCrc);
    }

    /// <summary>
    /// Verifies that missing, empty, malformed, negative, or overflowing map CRC and size tokens leave the values unset
    /// without failing the header parse.
    /// </summary>
    /// <param name="initString">The replay init string.</param>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData("M=maps/x/x.map;S=HPlayer,0,0,0,1,2:X;")]
    [InlineData("M=maps/x/x.map;MC=;MS=;")]
    [InlineData("M=maps/x/x.map;MC=XYZ;MS=12ab;")]
    [InlineData("M=maps/x/x.map;MC=1FFFFFFFF;MS=-5;")]
    [InlineData("M=maps/x/x.map;MC=0x;MS=99999999999;")]
    public async Task ParseHeaderAsync_MissingOrMalformedMapCrcAndSize_LeavesValuesUnsetAsync(string initString)
    {
        using var stream = new MemoryStream();
        WriteHeaderWithInitString(stream, initString);
        stream.Position = 0;

        var result = await _parser.ParseHeaderAsync(stream);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.NotNull(result.Data);
        Assert.Null(result.Data.MapCrc);
        Assert.Null(result.Data.MapSize);
        Assert.Null(result.Data.FormattedMapCrc);
        Assert.Equal("x", result.Data.MapName);
    }

    private static void WriteMinimalValidHeader(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("GENREP"));
        writer.Write(100u);
        writer.Write(200u);
        writer.Write(300u);
        writer.Write((byte)1);
        writer.Write((byte)2);
        writer.Write(new byte[8]);
        writer.Write(Encoding.Unicode.GetBytes("Long Match" + char.MinValue));
        writer.Write(new byte[16]);
        writer.Write(Encoding.Unicode.GetBytes("Version 1.04" + char.MinValue));
        writer.Write(Encoding.Unicode.GetBytes("Aug 21 2026" + char.MinValue));
        writer.Write(20260821u);
        writer.Write(0x27533BB0u);
        writer.Write(0x76B251A3u);
        writer.Write(Encoding.ASCII.GetBytes("M=maps/defcon6/defcon6.map;S=HPlayerOne,127.0.0.1,8086,0,1,2:X:X;" + char.MinValue));
        writer.Flush();
    }

    private static void WriteHeaderWithInitString(Stream stream, string initString)
    {
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("GENREP"));
        writer.Write(new byte[22]);
        writer.Write(Encoding.Unicode.GetBytes("Init String Test" + char.MinValue));
        writer.Write(new byte[16]);
        writer.Write(Encoding.Unicode.GetBytes("1.04" + char.MinValue));
        writer.Write(Encoding.Unicode.GetBytes("Aug 21 2026" + char.MinValue));
        writer.Write(20260821u);
        writer.Write(0x27533BB0u);
        writer.Write(0x76B251A3u);
        writer.Write(Encoding.ASCII.GetBytes(initString + char.MinValue));
        writer.Flush();
    }

    private sealed class OversizedStream(byte[] content) : MemoryStream(content)
    {
        public override long Length => ReplayManagerConstants.MaxReplaySizeBytes + 1;

        public override bool CanSeek => true;
    }
}
