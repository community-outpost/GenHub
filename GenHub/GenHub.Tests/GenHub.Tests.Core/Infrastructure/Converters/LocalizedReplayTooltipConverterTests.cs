using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Infrastructure.Converters;
using System;
using System.Globalization;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="LocalizedReplayTooltipConverter"/>.
/// </summary>
public class LocalizedReplayTooltipConverterTests
{
    private readonly LocalizedReplayTooltipConverter _converter = LocalizedReplayTooltipConverter.Instance;
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Verifies that null, empty, or non-ReplayFile input returns an empty string without crashing.
    /// </summary>
    /// <param name="input">The invalid or null input object.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("non-model text")]
    [InlineData(12345)]
    public void Convert_WithNullOrInvalidInput_ReturnsEmptyString(object? input)
    {
        var result = _converter.Convert(input, typeof(string), null, _culture);
        Assert.Equal(string.Empty, result);
    }

    /// <summary>
    /// Verifies that Convert with ReplayFile model resolves appropriate tooltips for Play, Takeover, and Compatibility.
    /// </summary>
    [Fact]
    public void Convert_WithReplayFile_ResolvesAppropriateTooltips()
    {
        var replay = new ReplayFile
        {
            FileName = "TestReplay.rep",
            FullPath = @"C:\Games\Replays\TestReplay.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            MatchingProfileName = "ZH Profile",
            CompatibilityStatus = ReplayCompatibilityStatus.Compatible,
            SupportsCheckpoints = true,
            GameVersion = GameType.ZeroHour,
        };

        var playResult = _converter.Convert(replay, typeof(string), "Play", _culture) as string;
        Assert.False(string.IsNullOrWhiteSpace(playResult));
        Assert.Contains("ZH Profile", playResult);

        var takeoverResult = _converter.Convert(replay, typeof(string), "Takeover", _culture) as string;
        Assert.False(string.IsNullOrWhiteSpace(takeoverResult));

        var compatResult = _converter.Convert(replay, typeof(string), "Compatibility", _culture) as string;
        Assert.False(string.IsNullOrWhiteSpace(compatResult));
    }

    /// <summary>
    /// Verifies that an Orphaned ReplayFile extracts and formats distinct Exe and INI CRC values.
    /// </summary>
    [Fact]
    public void Convert_WithOrphanedReplay_PreservesDistinctHexCrcTokens()
    {
        var replay = new ReplayFile
        {
            FileName = "Orphaned.rep",
            FullPath = @"C:\Games\Replays\Orphaned.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            CompatibilityStatus = ReplayCompatibilityStatus.Orphaned,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x12345678,
                IniCrc = 0x87654321,
            },
        };

        var result = _converter.Convert(replay, typeof(string), null, _culture) as string;

        Assert.NotNull(result);
        Assert.Contains("0x12345678", result);
        Assert.Contains("0x87654321", result);
        Assert.Contains("official catalog", result);
    }

    /// <summary>
    /// Verifies that RequiresProfile, Downloadable, and Unknown compatibility statuses resolve expected text.
    /// </summary>
    /// <param name="status">The compatibility status to test.</param>
    /// <param name="expectedSubstring">The substring expected in the fallback text.</param>
    [Theory]
    [InlineData(ReplayCompatibilityStatus.RequiresProfile, "Create Profile")]
    [InlineData(ReplayCompatibilityStatus.Downloadable, "Setup")]
    [InlineData(ReplayCompatibilityStatus.Unknown, "Replay header metadata is not available")]
    public void Convert_WithDifferentCompatibilityStatuses_ResolvesExpectedText(
        ReplayCompatibilityStatus status,
        string expectedSubstring)
    {
        var replay = new ReplayFile
        {
            FileName = "Test.rep",
            FullPath = @"C:\Games\Test.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            CompatibilityStatus = status,
        };

        var result = _converter.Convert(replay, typeof(string), null, _culture) as string;

        Assert.NotNull(result);
        Assert.Contains(expectedSubstring, result);
    }

    /// <summary>
    /// Verifies that recovery engine suffix is appended when checkpoint support and recovery profile are present.
    /// </summary>
    [Fact]
    public void Convert_WithRecoveryEngine_AppendsRecoverySuffix()
    {
        var replay = new ReplayFile
        {
            FileName = "Recovery.rep",
            FullPath = @"C:\Games\Recovery.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            CompatibilityStatus = ReplayCompatibilityStatus.Compatible,
            MatchingProfileName = "ZH Standard",
            SupportsCheckpoints = true,
            RecoveryProfileName = "ZH Recovery Build",
        };

        var result = _converter.Convert(replay, typeof(string), null, _culture) as string;

        Assert.NotNull(result);
        Assert.Contains("Recovery Engine: Profile 'ZH Recovery Build'", result);
    }

    /// <summary>
    /// Verifies that Takeover tooltip explains requirement when checkpoint support is absent.
    /// </summary>
    [Fact]
    public void Convert_WithTakeoverWithoutCheckpointSupport_ExplainsRequirement()
    {
        var replay = new ReplayFile
        {
            FileName = "NoRecovery.rep",
            FullPath = @"C:\Games\NoRecovery.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            SupportsCheckpoints = false,
        };

        var result = _converter.Convert(replay, typeof(string), "Takeover", _culture) as string;

        Assert.NotNull(result);
        Assert.Contains("require a game client with checkpoint capabilities", result);
    }

    /// <summary>
    /// Verifies that ConvertBack throws NotSupportedException.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack("test", typeof(string), null, _culture));
    }
}
