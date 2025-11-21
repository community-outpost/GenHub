using System.Globalization;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Infrastructure.Converters;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="GameTypeToIconConverter"/>.
/// </summary>
public class GameTypeToIconConverterTests
{
    private readonly GameTypeToIconConverter _converter = new();
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Tests that <see cref="GameTypeToIconConverter.Convert"/> returns the correct icon URI for Generals game type.
    /// </summary>
    [Fact]
    public void Convert_WithGeneralsGameType_ReturnsGeneralsIconUri()
    {
        var result = _converter.Convert(GameType.Generals, typeof(string), null, _culture);
        Assert.Equal(UriConstants.GeneralsIconUri, result);
    }

    /// <summary>
    /// Tests that <see cref="GameTypeToIconConverter.Convert"/> returns the correct icon URI for Zero Hour game type.
    /// </summary>
    [Fact]
    public void Convert_WithZeroHourGameType_ReturnsZeroHourIconUri()
    {
        var result = _converter.Convert(GameType.ZeroHour, typeof(string), null, _culture);
        Assert.Equal(UriConstants.ZeroHourIconUri, result);
    }

    /// <summary>
    /// Tests that <see cref="GameTypeToIconConverter.Convert"/> returns the default icon URI for unknown game type.
    /// </summary>
    [Fact]
    public void Convert_WithUnknownGameType_ReturnsDefaultIconUri()
    {
        // Assuming there's an unknown enum value, but since GameType might be limited,
        // we'll test with a cast to simulate unknown value
        var unknownGameType = (GameType)999; // Simulate unknown value
        var result = _converter.Convert(unknownGameType, typeof(string), null, _culture);
        Assert.Equal(UriConstants.DefaultIconUri, result);
    }

    /// <summary>
    /// Tests that <see cref="GameTypeToIconConverter.Convert"/> returns the default icon URI for null input.
    /// </summary>
    [Fact]
    public void Convert_WithNullValue_ReturnsDefaultIconUri()
    {
        var result = _converter.Convert(null, typeof(string), null, _culture);
        Assert.Equal(UriConstants.DefaultIconUri, result);
    }

    /// <summary>
    /// Tests that <see cref="GameTypeToIconConverter.Convert"/> returns the default icon URI for non-GameType values.
    /// </summary>
    [Fact]
    public void Convert_WithNonGameTypeValue_ReturnsDefaultIconUri()
    {
        var result = _converter.Convert("invalid", typeof(string), null, _culture);
        Assert.Equal(UriConstants.DefaultIconUri, result);
    }

    /// <summary>
    /// Tests that <see cref="GameTypeToIconConverter.ConvertBack"/> throws <see cref="NotSupportedException"/>.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack(UriConstants.GeneralsIconUri, typeof(GameType), null, _culture));
    }

    /// <summary>
    /// Tests that <see cref="GameTypeToIconConverter.Instance"/> is not null.
    /// </summary>
    [Fact]
    public void Instance_IsNotNull()
    {
        Assert.NotNull(GameTypeToIconConverter.Instance);
    }
}