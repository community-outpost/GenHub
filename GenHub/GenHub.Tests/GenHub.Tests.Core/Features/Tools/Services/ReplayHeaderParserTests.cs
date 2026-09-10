using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Features.Tools.ReplayManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
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
}
