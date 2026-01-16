using System.Globalization;
using Avalonia.Media;
using GenHub.Infrastructure.Converters;
using Xunit;

namespace GenHub.Tests.Core.Converters;

/// <summary>
/// Unit tests for the <see cref="BoolToBrushConverter"/>.
/// </summary>
public class BoolToBrushConverterTests
{
    private readonly BoolToBrushConverter _converter = new();

    /// <summary>
    /// Verifies that Convert returns the correct brush when the parameter is valid.
    /// </summary>
    /// <param name="value">The boolean value to convert.</param>
    /// <param name="parameter">The parameter containing the two brush colors separated by '|'.</param>
    /// <param name="r">Expected red component.</param>
    /// <param name="g">Expected green component.</param>
    /// <param name="b">Expected blue component.</param>
    [Theory]
    [InlineData(true, "#FF0000|#00FF00", 255, 0, 0)] // Red
    [InlineData(false, "#FF0000|#00FF00", 0, 255, 0)] // Green/Lime
    [InlineData(true, "#F59E0B|#22C55E", 245, 158, 11)] // Orange
    [InlineData(false, "#F59E0B|#22C55E", 34, 197, 94)] // Success Green
    public void Convert_ReturnsCorrectBrush_WhenParameterIsValid(bool value, string parameter, byte r, byte g, byte b)
    {
        // Act
        var result = _converter.Convert(value, typeof(IBrush), parameter, CultureInfo.InvariantCulture);

        // Assert
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(result);
        Assert.Equal(r, brush.Color.R);
        Assert.Equal(g, brush.Color.G);
        Assert.Equal(b, brush.Color.B);
        Assert.Equal(255, brush.Color.A);
    }

    /// <summary>
    /// Verifies that Convert returns a transparent brush when the value is not a boolean.
    /// </summary>
    [Fact]
    public void Convert_ReturnsTransparent_WhenValueValueIsNotBool()
    {
        // Act
        var result = _converter.Convert("not a bool", typeof(IBrush), "#FF0000|#00FF00", CultureInfo.InvariantCulture);

        // Assert
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(result);
        Assert.Equal(Colors.Transparent, brush.Color);
    }

    /// <summary>
    /// Verifies that Convert returns a transparent brush when the parameter is missing.
    /// </summary>
    [Fact]
    public void Convert_ReturnsTransparent_WhenParameterIsMissing()
    {
        // Act
        var result = _converter.Convert(true, typeof(IBrush), null, CultureInfo.InvariantCulture);

        // Assert
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(result);
        Assert.Equal(Colors.Transparent, brush.Color);
    }

    /// <summary>
    /// Verifies that Convert returns a transparent brush when the parameter format is invalid.
    /// </summary>
    [Fact]
    public void Convert_ReturnsTransparent_WhenParameterFormatIsInvalid()
    {
        // Act
        var result = _converter.Convert(true, typeof(IBrush), "onlyonecolor", CultureInfo.InvariantCulture);

        // Assert
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(result);
        Assert.Equal(Colors.Transparent, brush.Color);
    }
}
