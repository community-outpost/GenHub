using System.Globalization;
using GenHub.Infrastructure.Converters;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="NotNullOrEmptyConverter"/>.
/// </summary>
public class NotNullOrEmptyConverterTests
{
    private readonly NotNullOrEmptyConverter _converter = new();
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Tests that <see cref="NotNullOrEmptyConverter.Convert"/> returns true for non-empty string.
    /// </summary>
    [Fact]
    public void Convert_WithNonEmptyString_ReturnsTrue()
    {
        var result = _converter.Convert("test", typeof(bool), null, _culture);
        Assert.True((bool?)result);
    }

    /// <summary>
    /// Tests that <see cref="NotNullOrEmptyConverter.Convert"/> returns false for empty string.
    /// </summary>
    [Fact]
    public void Convert_WithEmptyString_ReturnsFalse()
    {
        var result = _converter.Convert(string.Empty, typeof(bool), null, _culture);
        Assert.False((bool?)result);
    }

    /// <summary>
    /// Tests that <see cref="NotNullOrEmptyConverter.Convert"/> returns false for whitespace string.
    /// </summary>
    [Fact]
    public void Convert_WithWhitespaceString_ReturnsFalse()
    {
        var result = _converter.Convert("   ", typeof(bool), null, _culture);
        Assert.False((bool?)result);
    }

    /// <summary>
    /// Tests that <see cref="NotNullOrEmptyConverter.Convert"/> returns false for null values.
    /// </summary>
    [Fact]
    public void Convert_WithNullValue_ReturnsFalse()
    {
        var result = _converter.Convert(null, typeof(bool), null, _culture);
        Assert.False((bool?)result);
    }

    /// <summary>
    /// Tests that <see cref="NotNullOrEmptyConverter.Convert"/> returns true for non-null non-string objects.
    /// </summary>
    [Fact]
    public void Convert_WithNonStringObject_ReturnsTrue()
    {
        var result = _converter.Convert(42, typeof(bool), null, _culture);
        Assert.True((bool?)result);
    }

    /// <summary>
    /// Tests that <see cref="NotNullOrEmptyConverter.ConvertBack"/> throws <see cref="NotImplementedException"/>.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        Assert.Throws<NotImplementedException>(() =>
            _converter.ConvertBack(true, typeof(object), null, _culture));
    }
}