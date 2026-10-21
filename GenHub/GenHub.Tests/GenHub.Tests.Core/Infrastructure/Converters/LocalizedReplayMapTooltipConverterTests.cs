using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Infrastructure.Converters;
using System;
using System.Globalization;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="LocalizedReplayMapTooltipConverter"/>.
/// </summary>
public class LocalizedReplayMapTooltipConverterTests
{
    private readonly LocalizedReplayMapTooltipConverter _converter = LocalizedReplayMapTooltipConverter.Instance;

    /// <summary>
    /// Verifies that the tooltip names the map and states its availability with the map CRC and size.
    /// </summary>
    /// <param name="availability">The replay map availability.</param>
    /// <param name="expectedStatus">Text the availability line must contain.</param>
    [Theory]
    [InlineData(ReplayMapAvailability.Available, "Map found")]
    [InlineData(ReplayMapAvailability.NotFound, "Map not found")]
    [InlineData(ReplayMapAvailability.Unknown, "could not be checked")]
    public void Convert_WithMapCrcAndSize_DescribesAvailability(ReplayMapAvailability availability, string expectedStatus)
    {
        var replay = CreateReplay(new ReplayMetadata { MapName = "tansooo", MapCrc = 0x6314F5FD, MapSize = 106110 });
        replay.MapAvailability = availability;

        var result = _converter.Convert(replay, typeof(string), null, CultureInfo.InvariantCulture) as string;

        Assert.NotNull(result);
        Assert.StartsWith("tansooo", result);
        Assert.Contains(expectedStatus, result);
        Assert.Contains("0x6314F5FD", result);
    }

    /// <summary>
    /// Verifies that a replay without a map CRC keeps the plain map name tooltip, and a replay without metadata gets none.
    /// </summary>
    [Fact]
    public void Convert_WithoutMapCrc_ReturnsMapNameOnly()
    {
        var withName = CreateReplay(new ReplayMetadata { MapName = "tansooo" });
        var withoutMetadata = CreateReplay(null);

        Assert.Equal("tansooo", _converter.Convert(withName, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Null(_converter.Convert(withoutMetadata, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Null(_converter.Convert("not a replay", typeof(string), null, CultureInfo.InvariantCulture));
    }

    private static ReplayFile CreateReplay(ReplayMetadata? metadata) => new()
    {
        FileName = "Test.rep",
        FullPath = "/replays/Test.rep",
        SizeInBytes = 1024,
        LastModified = DateTime.UtcNow,
        GameVersion = GameType.ZeroHour,
        Metadata = metadata,
    };
}
