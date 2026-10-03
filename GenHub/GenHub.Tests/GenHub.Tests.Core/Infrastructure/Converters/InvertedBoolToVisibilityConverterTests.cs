using GenHub.Infrastructure.Converters;
using System.Globalization;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="InvertedBoolToVisibilityConverter"/>.
/// </summary>
public class InvertedBoolToVisibilityConverterTests
{
    private readonly InvertedBoolToVisibilityConverter _converter = new();
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Tests that <see cref="InvertedBoolToVisibilityConverter.Convert"/> returns "Collapsed" for true values.
    /// </summary>
    [Fact]
    public void Convert_WithTrueValue_ReturnsCollapsed()
    {
        var result = _converter.Convert(true, typeof(string), null, _culture);
        Assert.Equal("Collapsed", result);
    }

    /// <summary>
    /// Tests that <see cref="InvertedBoolToVisibilityConverter.Convert"/> returns "Visible" for false values.
    /// </summary>
    [Fact]
    public void Convert_WithFalseValue_ReturnsVisible()
    {
        var result = _converter.Convert(false, typeof(string), null, _culture);
        Assert.Equal("Visible", result);
    }

    /// <summary>
    /// Tests that <see cref="InvertedBoolToVisibilityConverter.Convert"/> returns "Visible" for null values.
    /// </summary>
    [Fact]
    public void Convert_WithNullValue_ReturnsVisible()
    {
        var result = _converter.Convert(null, typeof(string), null, _culture);
        Assert.Equal("Visible", result);
    }

    /// <summary>
    /// Tests that <see cref="InvertedBoolToVisibilityConverter.Convert"/> returns "Visible" for non-boolean values.
    /// </summary>
    [Fact]
    public void Convert_WithNonBooleanValue_ReturnsVisible()
    {
        var result = _converter.Convert("not a boolean", typeof(string), null, _culture);
        Assert.Equal("Visible", result);
    }

    /// <summary>
    /// Tests that <see cref="InvertedBoolToVisibilityConverter.Convert"/> returns false for a true value with a boolean target.
    /// </summary>
    [Fact]
    public void Convert_WithTrueValueAndBoolTarget_ReturnsFalse()
    {
        var result = _converter.Convert(true, typeof(bool), null, _culture);
        Assert.False(Assert.IsType<bool>(result));
    }

    /// <summary>
    /// Tests that <see cref="InvertedBoolToVisibilityConverter.Convert"/> returns true for a false value with a boolean target.
    /// </summary>
    [Fact]
    public void Convert_WithFalseValueAndBoolTarget_ReturnsTrue()
    {
        var result = _converter.Convert(false, typeof(bool), null, _culture);
        Assert.True(Assert.IsType<bool>(result));
    }

    /// <summary>
    /// Tests that <see cref="InvertedBoolToVisibilityConverter.ConvertBack"/> throws <see cref="NotSupportedException"/>.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack("Collapsed", typeof(bool), null, _culture));
    }
}
