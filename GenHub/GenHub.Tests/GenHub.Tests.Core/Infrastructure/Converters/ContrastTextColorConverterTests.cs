using System.Globalization;
using Avalonia.Media;
using GenHub.Infrastructure.Converters;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="ContrastTextColorConverter"/>.
/// </summary>
public class ContrastTextColorConverterTests
{
    private readonly ContrastTextColorConverter _converter = new();
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Tests that <see cref="ContrastTextColorConverter.Convert"/> returns black for light colors.
    /// </summary>
    [Fact]
    public void Convert_WithLightColor_ReturnsBlackBrush()
    {
        var lightColor = Color.FromRgb(200, 200, 200); // Light gray
        var result = _converter.Convert(lightColor, typeof(IBrush), null, _culture);
        Assert.Equal(Brushes.Black, result);
    }

    /// <summary>
    /// Tests that <see cref="ContrastTextColorConverter.Convert"/> returns white for dark colors.
    /// </summary>
    [Fact]
    public void Convert_WithDarkColor_ReturnsWhiteBrush()
    {
        var darkColor = Color.FromRgb(50, 50, 50); // Dark gray
        var result = _converter.Convert(darkColor, typeof(IBrush), null, _culture);
        Assert.Equal(Brushes.White, result);
    }

    /// <summary>
    /// Tests that <see cref="ContrastTextColorConverter.Convert"/> returns black for pure white.
    /// </summary>
    [Fact]
    public void Convert_WithWhiteColor_ReturnsBlackBrush()
    {
        var result = _converter.Convert(Colors.White, typeof(IBrush), null, _culture);
        Assert.Equal(Brushes.Black, result);
    }

    /// <summary>
    /// Tests that <see cref="ContrastTextColorConverter.Convert"/> returns white for pure black.
    /// </summary>
    [Fact]
    public void Convert_WithBlackColor_ReturnsWhiteBrush()
    {
        var result = _converter.Convert(Colors.Black, typeof(IBrush), null, _culture);
        Assert.Equal(Brushes.White, result);
    }

    /// <summary>
    /// Tests that <see cref="ContrastTextColorConverter.Convert"/> handles color strings correctly.
    /// </summary>
    [Fact]
    public void Convert_WithValidColorString_ReturnsCorrectBrush()
    {
        var result = _converter.Convert("#FFFFFF", typeof(IBrush), null, _culture); // White
        Assert.Equal(Brushes.Black, result);

        result = _converter.Convert("#000000", typeof(IBrush), null, _culture); // Black
        Assert.Equal(Brushes.White, result);
    }

    /// <summary>
    /// Tests that <see cref="ContrastTextColorConverter.Convert"/> returns white for invalid color strings.
    /// </summary>
    [Fact]
    public void Convert_WithInvalidColorString_ReturnsWhiteBrush()
    {
        var result = _converter.Convert("invalid-color", typeof(IBrush), null, _culture);
        Assert.Equal(Brushes.White, result);
    }

    /// <summary>
    /// Tests that <see cref="ContrastTextColorConverter.Convert"/> returns white for null input.
    /// </summary>
    [Fact]
    public void Convert_WithNullValue_ReturnsWhiteBrush()
    {
        var result = _converter.Convert(null, typeof(IBrush), null, _culture);
        Assert.Equal(Brushes.White, result);
    }

    /// <summary>
    /// Tests that <see cref="ContrastTextColorConverter.Convert"/> returns white for non-color values.
    /// </summary>
    [Fact]
    public void Convert_WithNonColorValue_ReturnsWhiteBrush()
    {
        var result = _converter.Convert(42, typeof(IBrush), null, _culture);
        Assert.Equal(Brushes.White, result);
    }

    /// <summary>
    /// Tests that <see cref="ContrastTextColorConverter.ConvertBack"/> throws <see cref="NotImplementedException"/>.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        Assert.Throws<NotImplementedException>(() =>
            _converter.ConvertBack(Brushes.Black, typeof(Color), null, _culture));
    }
}