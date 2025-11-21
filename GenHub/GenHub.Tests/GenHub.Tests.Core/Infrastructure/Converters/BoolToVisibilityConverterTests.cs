using System.Globalization;
using GenHub.Infrastructure.Converters;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="BoolToVisibilityConverter"/>.
/// </summary>
public class BoolToVisibilityConverterTests
{
    private readonly BoolToVisibilityConverter _converter = new();
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Tests that <see cref="BoolToVisibilityConverter.Convert"/> returns "Visible" for true values.
    /// </summary>
    [Fact]
    public void Convert_WithTrueValue_ReturnsVisible()
    {
        var result = _converter.Convert(true, typeof(string), null, _culture);
        Assert.Equal("Visible", result);
    }

    /// <summary>
    /// Tests that <see cref="BoolToVisibilityConverter.Convert"/> returns "Collapsed" for false values.
    /// </summary>
    [Fact]
    public void Convert_WithFalseValue_ReturnsCollapsed()
    {
        var result = _converter.Convert(false, typeof(string), null, _culture);
        Assert.Equal("Collapsed", result);
    }

    /// <summary>
    /// Tests that <see cref="BoolToVisibilityConverter.Convert"/> returns "Collapsed" for null values.
    /// </summary>
    [Fact]
    public void Convert_WithNullValue_ReturnsCollapsed()
    {
        var result = _converter.Convert(null, typeof(string), null, _culture);
        Assert.Equal("Collapsed", result);
    }

    /// <summary>
    /// Tests that <see cref="BoolToVisibilityConverter.Convert"/> returns "Collapsed" for non-boolean values.
    /// </summary>
    [Fact]
    public void Convert_WithNonBooleanValue_ReturnsCollapsed()
    {
        var result = _converter.Convert("not a boolean", typeof(string), null, _culture);
        Assert.Equal("Collapsed", result);
    }

    /// <summary>
    /// Tests that <see cref="BoolToVisibilityConverter.ConvertBack"/> throws <see cref="NotImplementedException"/>.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        Assert.Throws<NotImplementedException>(() =>
            _converter.ConvertBack("Visible", typeof(bool), null, _culture));
    }
}