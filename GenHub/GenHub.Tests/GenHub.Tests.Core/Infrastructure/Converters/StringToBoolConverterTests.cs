using System.Globalization;
using GenHub.Infrastructure.Converters;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="StringToBoolConverter"/>.
/// </summary>
public class StringToBoolConverterTests
{
    private readonly StringToBoolConverter _converter = new();
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Tests that <see cref="StringToBoolConverter.Convert"/> returns true for non-empty string.
    /// </summary>
    [Fact]
    public void Convert_WithNonEmptyString_ReturnsTrue()
    {
        var result = _converter.Convert("test", typeof(bool), null, _culture);
        Assert.True((bool?)result);
    }

    /// <summary>
    /// Tests that <see cref="StringToBoolConverter.Convert"/> returns false for empty string.
    /// </summary>
    [Fact]
    public void Convert_WithEmptyString_ReturnsFalse()
    {
        var result = _converter.Convert(string.Empty, typeof(bool), null, _culture);
        Assert.False((bool?)result);
    }

    /// <summary>
    /// Tests that <see cref="StringToBoolConverter.Convert"/> returns false for whitespace string.
    /// </summary>
    [Fact]
    public void Convert_WithWhitespaceString_ReturnsFalse()
    {
        var result = _converter.Convert("   ", typeof(bool), null, _culture);
        Assert.False((bool?)result);
    }

    /// <summary>
    /// Tests that <see cref="StringToBoolConverter.Convert"/> returns false for null values.
    /// </summary>
    [Fact]
    public void Convert_WithNullValue_ReturnsFalse()
    {
        var result = _converter.Convert(null, typeof(bool), null, _culture);
        Assert.False((bool?)result);
    }

    /// <summary>
    /// Tests that <see cref="StringToBoolConverter.Convert"/> returns false for non-string values.
    /// </summary>
    [Fact]
    public void Convert_WithNonStringValue_ReturnsFalse()
    {
        var result = _converter.Convert(42, typeof(bool), null, _culture);
        Assert.False((bool?)result);
    }

    /// <summary>
    /// Tests that <see cref="StringToBoolConverter.Convert"/> inverts result when parameter is "invert".
    /// </summary>
    [Fact]
    public void Convert_WithInvertParameter_InvertsResult()
    {
        var result = _converter.Convert("test", typeof(bool), "invert", _culture);
        Assert.False((bool?)result);
    }

    /// <summary>
    /// Tests that <see cref="StringToBoolConverter.Convert"/> inverts result for empty string when parameter is "invert".
    /// </summary>
    [Fact]
    public void Convert_WithInvertParameter_InvertsEmptyStringResult()
    {
        var result = _converter.Convert(string.Empty, typeof(bool), "invert", _culture);
        Assert.True((bool?)result);
    }

    /// <summary>
    /// Tests that <see cref="StringToBoolConverter.ConvertBack"/> throws <see cref="NotImplementedException"/>.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        Assert.Throws<NotImplementedException>(() =>
            _converter.ConvertBack(true, typeof(string), null, _culture));
    }
}