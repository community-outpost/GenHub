// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using FluentAssertions;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Unit tests for <see cref="WakCodec"/>.
/// Layout verified against WaterTracksRenderSystem::saveTracksTo/loadTracksFrom
/// (W3DWaterTracks.cpp): 20-byte records (start, end, i32 waveType) plus a
/// trailing i32 count.
/// </summary>
public sealed class WakCodecTests
{
    /// <summary>
    /// Tests that wave tracks round trip exactly.
    /// </summary>
    [Fact]
    public void Waves_RoundTrip_PreservesTracks()
    {
        // Arrange
        var tracks = new List<WaveTrackRecord>
        {
            new() { StartX = 10f, StartY = 20f, EndX = 30f, EndY = 40f, WaveType = 1 },
            new() { StartX = 50f, StartY = 60f, EndX = 70f, EndY = 80f, WaveType = 2 },
        };

        // Act
        var bytes = WakCodec.Encode(tracks);
        var decoded = WakCodec.Decode(bytes);

        // Assert
        decoded.Success.Should().BeTrue();
        decoded.Data.Should().HaveCount(2);
        decoded.Data![0].StartX.Should().Be(10f);
        decoded.Data![0].EndY.Should().Be(40f);
        decoded.Data![0].WaveType.Should().Be(1);
        decoded.Data![1].StartX.Should().Be(50f);
        decoded.Data![1].WaveType.Should().Be(2);
    }

    /// <summary>
    /// Tests that the count trailer sits at the end of the file.
    /// </summary>
    [Fact]
    public void Waves_Bytes_EndWithCount()
    {
        // Arrange
        var tracks = new List<WaveTrackRecord> { new(), new(), new() };

        // Act
        var bytes = WakCodec.Encode(tracks);

        // Assert
        bytes.Length.Should().Be((3 * WakCodec.RecordSize) + 4);
        BitConverter.ToInt32(bytes, bytes.Length - 4).Should().Be(3);
    }

    /// <summary>
    /// Tests that an empty track list encodes to a zero count.
    /// </summary>
    [Fact]
    public void Waves_Empty_EncodesZeroCount()
    {
        // Arrange
        var tracks = new List<WaveTrackRecord>();

        // Act
        var bytes = WakCodec.Encode(tracks);
        var decoded = WakCodec.Decode(bytes);

        // Assert
        bytes.Should().HaveCount(4);
        decoded.Success.Should().BeTrue();
        decoded.Data.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that truncated files fail cleanly.
    /// </summary>
    [Fact]
    public void Waves_Truncated_ReturnsFailure()
    {
        // Arrange
        var bytes = new byte[] { 0x00, 0x01, 0x02 };

        // Act
        var decoded = WakCodec.Decode(bytes);

        // Assert
        decoded.Success.Should().BeFalse();
    }

    /// <summary>
    /// Tests decoding against hand-derived bytes in the exact C++ fwrite order
    /// (startX, startY, endX, endY, i32 waveType per record, trailing count),
    /// independent of the C# encoder.
    /// </summary>
    [Fact]
    public void Waves_HandDerivedBytes_MatchesCppLayout()
    {
        // Arrange: two records plus a count trailer of 2.
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(1.5f));
        bytes.AddRange(BitConverter.GetBytes(2.5f));
        bytes.AddRange(BitConverter.GetBytes(-3.25f));
        bytes.AddRange(BitConverter.GetBytes(4f));
        bytes.AddRange(BitConverter.GetBytes(2));
        bytes.AddRange(BitConverter.GetBytes(10f));
        bytes.AddRange(BitConverter.GetBytes(20f));
        bytes.AddRange(BitConverter.GetBytes(30f));
        bytes.AddRange(BitConverter.GetBytes(40f));
        bytes.AddRange(BitConverter.GetBytes(0));
        bytes.AddRange(BitConverter.GetBytes(2));

        // Act
        var decoded = WakCodec.Decode([.. bytes]);

        // Assert
        decoded.Success.Should().BeTrue();
        decoded.Data.Should().HaveCount(2);
        decoded.Data![0].StartX.Should().Be(1.5f);
        decoded.Data![0].StartY.Should().Be(2.5f);
        decoded.Data![0].EndX.Should().Be(-3.25f);
        decoded.Data![0].EndY.Should().Be(4f);
        decoded.Data![0].WaveType.Should().Be(2);
        decoded.Data![1].StartX.Should().Be(10f);
        decoded.Data![1].EndY.Should().Be(40f);
        decoded.Data![1].WaveType.Should().Be(0);
    }

    /// <summary>
    /// Tests that a count trailer disagreeing with the file size fails cleanly.
    /// </summary>
    [Fact]
    public void Waves_CountMismatch_ReturnsFailure()
    {
        // Arrange: one record but a trailing count of 2.
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(1f));
        bytes.AddRange(BitConverter.GetBytes(2f));
        bytes.AddRange(BitConverter.GetBytes(3f));
        bytes.AddRange(BitConverter.GetBytes(4f));
        bytes.AddRange(BitConverter.GetBytes(1));
        bytes.AddRange(BitConverter.GetBytes(2));

        // Act
        var decoded = WakCodec.Decode([.. bytes]);

        // Assert
        decoded.Success.Should().BeFalse();
    }
}
