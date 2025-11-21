using System.Globalization;
using GenHub.Infrastructure.Converters;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="BoolToColorConverter"/>.
/// </summary>
public class BoolToColorConverterTests
{
    private readonly BoolToColorConverter _defaultConverter = new();
    private readonly BoolToColorConverter _customConverter = new(Avalonia.Media.Colors.Green, Avalonia.Media.Colors.Red);
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Tests that <see cref="BoolToColorConverter.Convert"/> returns TrueColor for true values with default colors.
    /// </summary>
    [Fact]
    public void Convert_WithTrueValue_ReturnsDefaultTrueColor()
    {
        var result = _defaultConverter.Convert(true, typeof(Avalonia.Media.Color), null, _culture) as Avalonia.Media.SolidColorBrush;
        Assert.NotNull(result);
        Assert.Equal(Avalonia.Media.Colors.Green, result.Color);
    }

    /// <summary>
    /// Tests that <see cref="BoolToColorConverter.Convert"/> returns FalseColor for false values with default colors.
    /// </summary>
    [Fact]
    public void Convert_WithFalseValue_ReturnsDefaultFalseColor()
    {
        var result = _defaultConverter.Convert(false, typeof(Avalonia.Media.Color), null, _culture) as Avalonia.Media.SolidColorBrush;
        Assert.NotNull(result);
        Assert.Equal(Avalonia.Media.Colors.Red, result.Color);
    }

    /// <summary>
    /// Tests that <see cref="BoolToColorConverter.Convert"/> returns FalseColor for null values.
    /// </summary>
    [Fact]
    public void Convert_WithNullValue_ReturnsDefaultFalseColor()
    {
        var result = _defaultConverter.Convert(null, typeof(Avalonia.Media.Color), null, _culture) as Avalonia.Media.SolidColorBrush;
        Assert.NotNull(result);
        Assert.Equal(Avalonia.Media.Colors.Red, result.Color);
    }

    /// <summary>
    /// Tests that <see cref="BoolToColorConverter.Convert"/> returns custom TrueColor for true values.
    /// </summary>
    [Fact]
    public void Convert_WithTrueValue_ReturnsCustomTrueColor()
    {
        var result = _customConverter.Convert(true, typeof(Avalonia.Media.Color), null, _culture) as Avalonia.Media.SolidColorBrush;
        Assert.NotNull(result);
        Assert.Equal(Avalonia.Media.Colors.Green, result.Color);
    }

    /// <summary>
    /// Tests that <see cref="BoolToColorConverter.Convert"/> returns custom FalseColor for false values.
    /// </summary>
    [Fact]
    public void Convert_WithFalseValue_ReturnsCustomFalseColor()
    {
        var result = _customConverter.Convert(false, typeof(Avalonia.Media.Color), null, _culture) as Avalonia.Media.SolidColorBrush;
        Assert.NotNull(result);
        Assert.Equal(Avalonia.Media.Colors.Red, result.Color);
    }

    /// <summary>
    /// Tests that <see cref="BoolToColorConverter.Convert"/> returns FalseColor for non-boolean values.
    /// </summary>
    [Fact]
    public void Convert_WithNonBooleanValue_ReturnsFalseColor()
    {
        var result = _defaultConverter.Convert("not a boolean", typeof(Avalonia.Media.Color), null, _culture) as Avalonia.Media.SolidColorBrush;
        Assert.NotNull(result);
        Assert.Equal(Avalonia.Media.Colors.Red, result.Color);
    }

    /// <summary>
    /// Tests that <see cref="BoolToColorConverter.ConvertBack"/> throws <see cref="NotImplementedException"/>.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        Assert.Throws<NotImplementedException>(() =>
            _defaultConverter.ConvertBack(Avalonia.Media.Colors.Green, typeof(bool), null, _culture));
    }
}