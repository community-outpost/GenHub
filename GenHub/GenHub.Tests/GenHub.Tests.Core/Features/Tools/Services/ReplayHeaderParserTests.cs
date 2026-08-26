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
    /// Verifies that a valid replay stream parses all header metadata accurately.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseHeaderAsync_ValidReplayStream_ExtractsMetadataSuccessfullyAsync()
    {
        // Construct valid GENREP stream
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // 1. Magic "GENREP"
        writer.Write(Encoding.ASCII.GetBytes("GENREP"));

        // 2. Fixed fields (22 bytes)
        writer.Write(100u);
        writer.Write(200u);
        writer.Write(300u);
        writer.Write((byte)1);
        writer.Write((byte)2);
        writer.Write(new byte[8]);

        // 3. Version string UTF-16LE null terminated
        writer.Write(Encoding.Unicode.GetBytes("1.04" + char.MinValue));

        // 4. Skip 16 bytes
        writer.Write(new byte[16]);

        // 5. VersionTimeString UTF-16LE null terminated
        writer.Write(Encoding.Unicode.GetBytes("Aug 21 2026" + char.MinValue));

        // 6. Title/Description UTF-16LE null terminated
        writer.Write(Encoding.Unicode.GetBytes("Test Match Replay" + char.MinValue));

        // 7. VersionNumber, exeCRC, iniCRC
        writer.Write(20260821u);
        writer.Write(0x27533BB0u);
        writer.Write(0x76B251A3u);

        // 8. InitString ASCII null terminated
        writer.Write(Encoding.ASCII.GetBytes("M=maps/defcon6/defcon6.map;S=HPlayerOne,0,0,1;S=HPlayerTwo,0,0,2;" + char.MinValue));

        writer.Flush();
        stream.Position = 0;

        var result = await _parser.ParseHeaderAsync(stream);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.NotNull(result.Data);
        Assert.Equal("1.04", result.Data.VersionString);
        Assert.Equal("Aug 21 2026", result.Data.BuildTimeString);
        Assert.Equal("Test Match Replay", result.Data.Title);
        Assert.Equal(20260821u, result.Data.VersionNumber);
        Assert.Equal(0x27533BB0u, result.Data.ExeCrc);
        Assert.Equal(0x76B251A3u, result.Data.IniCrc);
        Assert.Equal("0x27533BB0", result.Data.FormattedExeCrc);
        Assert.Equal("0x76B251A3", result.Data.FormattedIniCrc);
        Assert.Equal("defcon6", result.Data.MapName);
        Assert.NotNull(result.Data.Players);
        Assert.Equal(2, result.Data.Players.Count);
        Assert.Contains("PlayerOne", result.Data.Players);
        Assert.Contains("PlayerTwo", result.Data.Players);
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
}
