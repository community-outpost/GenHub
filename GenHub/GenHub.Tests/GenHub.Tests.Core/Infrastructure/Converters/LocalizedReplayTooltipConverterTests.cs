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
    /// Verifies that null or empty input returns an empty string or the input representation without crashing.
    /// </summary>
    [Fact]
    public void Convert_WithNullOrEmpty_ReturnsExpected()
    {
        var resultNull = _converter.Convert(null, typeof(string), null, _culture);
        Assert.Equal(string.Empty, resultNull);

        var resultEmpty = _converter.Convert(string.Empty, typeof(string), null, _culture);
        Assert.Equal(string.Empty, resultEmpty);
    }

    /// <summary>
    /// Verifies that when localization is not initialized, fallback strings are preserved.
    /// </summary>
    [Fact]
    public void Convert_WithoutLocalization_ReturnsOriginalString()
    {
        const string text = "Checkpoint recovery and match takeover require a game client with checkpoint capabilities (e.g. MP-Recovery or modern community engine).";
        var result = _converter.Convert(text, typeof(string), null, _culture);
        Assert.Equal(text, result);
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
    /// Verifies that Convert with compatibility formatted strings preserves the prefix and CRC values.
    /// </summary>
    /// <param name="input">The prefixed compatibility string.</param>
    [Theory]
    [InlineData("[OK] Compatible client detected.")]
    [InlineData("[WARN] Profile mismatch.")]
    [InlineData("[FAIL] No client found.")]
    public void Convert_WithPrefixedCompatibilityString_PreservesOrTranslates(string input)
    {
        var result = _converter.Convert(input, typeof(string), null, _culture) as string;
        Assert.NotNull(result);
        Assert.StartsWith("[", result);
    }

    /// <summary>
    /// Verifies that CRC mismatches extract and preserve real hex CRC tokens.
    /// </summary>
    [Fact]
    public void Convert_WithCrcMismatch_PreservesHexCrcTokens()
    {
        const string input = "[FAIL] INI CRC mismatch. Replay: 0x12345678, Profile: 0x87654321.";
        var result = _converter.Convert(input, typeof(string), null, _culture) as string;

        Assert.NotNull(result);
        Assert.Contains("0x12345678", result);
        Assert.Contains("0x87654321", result);
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
