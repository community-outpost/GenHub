using GenHub.Infrastructure.Converters;
using System.Globalization;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="NullToVisibilityConverter"/>.
/// </summary>
public class NullToVisibilityConverterTests
{
    private readonly NullToVisibilityConverter _converter = new();
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Tests that <see cref="NullToVisibilityConverter.Convert"/> returns "Collapsed" for null values.
    /// </summary>
    [Fact]
    public void Convert_WithNullValue_ReturnsCollapsed()
    {
        var result = _converter.Convert(null, typeof(string), null, _culture);
        Assert.Equal("Collapsed", result);
    }

    /// <summary>
    /// Tests that <see cref="NullToVisibilityConverter.Convert"/> returns "Visible" for non-null values.
    /// </summary>
    [Fact]
    public void Convert_WithNonNullValue_ReturnsVisible()
    {
        var result = _converter.Convert("test", typeof(string), null, _culture);
        Assert.Equal("Visible", result);
    }

    /// <summary>
    /// Tests that <see cref="NullToVisibilityConverter.Convert"/> returns "Visible" for empty string.
    /// </summary>
    [Fact]
    public void Convert_WithEmptyString_ReturnsVisible()
    {
        var result = _converter.Convert(string.Empty, typeof(string), null, _culture);
        Assert.Equal("Visible", result);
    }

    /// <summary>
    /// Tests that <see cref="NullToVisibilityConverter.Convert"/> returns false for a null value with a boolean target.
    /// </summary>
    [Fact]
    public void Convert_WithNullValueAndBoolTarget_ReturnsFalse()
    {
        var result = _converter.Convert(null, typeof(bool), null, _culture);
        Assert.False(Assert.IsType<bool>(result));
    }

    /// <summary>
    /// Tests that <see cref="NullToVisibilityConverter.Convert"/> returns true for a non-null value with a boolean target.
    /// </summary>
    [Fact]
    public void Convert_WithNonNullValueAndBoolTarget_ReturnsTrue()
    {
        var result = _converter.Convert("test", typeof(bool), null, _culture);
        Assert.True(Assert.IsType<bool>(result));
    }

    /// <summary>
    /// Tests that <see cref="NullToVisibilityConverter.ConvertBack"/> throws <see cref="NotImplementedException"/>.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        Assert.Throws<NotImplementedException>(() =>
            _converter.ConvertBack("Visible", typeof(object), null, _culture));
    }
}
