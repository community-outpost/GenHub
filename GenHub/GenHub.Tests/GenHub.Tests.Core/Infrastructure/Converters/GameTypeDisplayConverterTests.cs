using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Infrastructure.Converters;
using System;
using System.Globalization;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="GameTypeDisplayConverter"/>.
/// </summary>
public class GameTypeDisplayConverterTests
{
    private readonly GameTypeDisplayConverter _converter = GameTypeDisplayConverter.Instance;
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Verifies that converting GameType.Generals returns the full game title.
    /// </summary>
    [Fact]
    public void Convert_WithGeneralsEnum_ReturnsGeneralsFullName()
    {
        var result = _converter.Convert(GameType.Generals, typeof(string), null, _culture);
        Assert.NotNull(result);
        Assert.Equal(GameClientConstants.GeneralsFullName, result.ToString());
    }

    /// <summary>
    /// Verifies that converting GameType.ZeroHour returns the full game title.
    /// </summary>
    [Fact]
    public void Convert_WithZeroHourEnum_ReturnsZeroHourFullName()
    {
        var result = _converter.Convert(GameType.ZeroHour, typeof(string), null, _culture);
        Assert.NotNull(result);
        Assert.Equal(GameClientConstants.ZeroHourFullName, result.ToString());
    }

    /// <summary>
    /// Verifies that converting a Generals string representation returns the full game title.
    /// </summary>
    /// <param name="input">The game type string input.</param>
    [Theory]
    [InlineData("Generals")]
    [InlineData("generals")]
    public void Convert_WithGeneralsString_ReturnsGeneralsFullName(string input)
    {
        var result = _converter.Convert(input, typeof(string), null, _culture);
        Assert.NotNull(result);
        Assert.Equal(GameClientConstants.GeneralsFullName, result.ToString());
    }

    /// <summary>
    /// Verifies that converting a ZeroHour string representation returns the full game title.
    /// </summary>
    /// <param name="input">The game type string input.</param>
    [Theory]
    [InlineData("ZeroHour")]
    [InlineData("zerohour")]
    public void Convert_WithZeroHourString_ReturnsZeroHourFullName(string input)
    {
        var result = _converter.Convert(input, typeof(string), null, _culture);
        Assert.NotNull(result);
        Assert.Equal(GameClientConstants.ZeroHourFullName, result.ToString());
    }

    /// <summary>
    /// Verifies that converting null returns an empty string.
    /// </summary>
    [Fact]
    public void Convert_WithNull_ReturnsEmptyString()
    {
        var result = _converter.Convert(null, typeof(string), null, _culture);
        Assert.Equal(string.Empty, result);
    }

    /// <summary>
    /// Verifies that converting an unknown string returns the original input string.
    /// </summary>
    [Fact]
    public void Convert_WithUnknownString_ReturnsOriginalString()
    {
        var result = _converter.Convert("UnknownGame", typeof(string), null, _culture);
        Assert.Equal("UnknownGame", result);
    }

    /// <summary>
    /// Verifies that ConvertBack throws a NotSupportedException.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack("Generals", typeof(GameType), null, _culture));
    }
}
