using System;
using System.Globalization;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using GenHub.Core.Models.Enums;
using GenHub.Infrastructure.Converters;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="NotEqualToConverter"/>, <see cref="BoolToBackgroundConverter"/>,
/// <see cref="BoolToBorderConverter"/>, <see cref="GameTypeInitialConverter"/>, and <see cref="IndentToMarginConverter"/>.
/// </summary>
public class SelectionAndComparisonConverterTests
{
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Verifies that <see cref="NotEqualToConverter"/> accurately compares enum values against string parameter representations.
    /// </summary>
    [Fact]
    public void NotEqualToConverter_EnumAndStringParameter_ComparesCorrectly()
    {
        var converter = new NotEqualToConverter();

        // Enum value equals its string representation -> should return false (meaning they are equal)
        var equalsResult = converter.Convert(ContentState.Downloaded, typeof(bool), "Downloaded", _culture);
        Assert.False((bool)equalsResult!);

        // Enum value does not equal different string -> should return true (meaning not equal)
        var notEqualsResult = converter.Convert(ContentState.Downloaded, typeof(bool), "NotDownloaded", _culture);
        Assert.True((bool)notEqualsResult!);
    }

    /// <summary>
    /// Verifies general equality and null comparison behavior in <see cref="NotEqualToConverter"/>.
    /// </summary>
    [Fact]
    public void NotEqualToConverter_GeneralEqualityAndNulls()
    {
        var converter = new NotEqualToConverter();

        Assert.False((bool)converter.Convert(null, typeof(bool), null, _culture)!);
        Assert.True((bool)converter.Convert("test", typeof(bool), null, _culture)!);
        Assert.True((bool)converter.Convert(null, typeof(bool), "test", _culture)!);
        Assert.False((bool)converter.Convert("test", typeof(bool), "test", _culture)!);
        Assert.True((bool)converter.Convert("test1", typeof(bool), "test2", _culture)!);
    }

    /// <summary>
    /// Verifies that <see cref="BoolToBackgroundConverter"/> returns valid brushes and rejects ConvertBack.
    /// </summary>
    [AvaloniaFact]
    public void BoolToBackgroundConverter_ReturnsBrushAndThrowsOnConvertBack()
    {
        var converter = new BoolToBackgroundConverter();
        foreach (var isSelected in new[] { true, false })
        {
            var brush = converter.Convert(isSelected, typeof(IBrush), null, _culture);

            Assert.IsAssignableFrom<IBrush>(brush);
            Assert.Throws<NotSupportedException>(() => converter.ConvertBack(brush, typeof(bool), null, _culture));
        }
    }

    /// <summary>
    /// Verifies that <see cref="BoolToBorderConverter"/> returns valid brushes and rejects ConvertBack.
    /// </summary>
    [AvaloniaFact]
    public void BoolToBorderConverter_ReturnsBrushAndThrowsOnConvertBack()
    {
        var converter = new BoolToBorderConverter();
        foreach (var isSelected in new[] { true, false })
        {
            var brush = converter.Convert(isSelected, typeof(IBrush), null, _culture);

            Assert.IsAssignableFrom<IBrush>(brush);
            Assert.Throws<NotSupportedException>(() => converter.ConvertBack(brush, typeof(bool), null, _culture));
        }
    }

    /// <summary>
    /// Verifies that <see cref="GameTypeInitialConverter"/> converts various representations to initials.
    /// </summary>
    /// <param name="input">The input game type or name string.</param>
    /// <param name="expected">The expected initial.</param>
    [Theory]
    [InlineData(GameType.ZeroHour, "ZH")]
    [InlineData(GameType.Generals, "G")]
    [InlineData("Zero Hour", "ZH")]
    [InlineData("ZeroHour", "ZH")]
    [InlineData("Zero_Hour", "ZH")]
    [InlineData("zero-hour", "ZH")]
    [InlineData("ZH", "ZH")]
    [InlineData("Generals", "G")]
    [InlineData("G", "G")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    public void GameTypeInitialConverter_HandlesDisplayStringsAndEnums(object? input, string expected)
    {
        var converter = new GameTypeInitialConverter();
        var result = converter.Convert(input, typeof(string), null, _culture);

        Assert.Equal(expected, result);
        Assert.Throws<NotSupportedException>(() => converter.ConvertBack(result, typeof(GameType), null, _culture));
    }

    /// <summary>
    /// Verifies that <see cref="IndentToMarginConverter"/> calculates indent margins correctly.
    /// </summary>
    /// <param name="level">The hierarchy indentation level.</param>
    /// <param name="expectedLeftMargin">The expected left margin.</param>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 24)]
    [InlineData(2, 48)]
    [InlineData(5, 120)]
    [InlineData(10, 120)] // Max 5 levels clamped
    public void IndentToMarginConverter_CalculatesExpectedMargin(int level, double expectedLeftMargin)
    {
        var converter = new IndentToMarginConverter();
        var result = converter.Convert(level, typeof(Thickness), null, _culture);

        Assert.IsType<Thickness>(result);
        var thickness = (Thickness)result;
        Assert.Equal(expectedLeftMargin, thickness.Left);
        Assert.Equal(8, thickness.Bottom);
        Assert.Throws<NotSupportedException>(() => converter.ConvertBack(result, typeof(int), null, _culture));
    }
}
