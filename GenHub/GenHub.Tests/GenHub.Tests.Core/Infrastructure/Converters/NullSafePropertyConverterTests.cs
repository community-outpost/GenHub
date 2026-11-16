using GenHub.Infrastructure.Converters;
using System.Globalization;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="NullSafePropertyConverter"/>.
/// </summary>
public class NullSafePropertyConverterTests
{
    private readonly NullSafePropertyConverter _converter = new();
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Tests that <see cref="NullSafePropertyConverter.Convert"/> returns the value when it's not null.
    /// </summary>
    [Fact]
    public void Convert_WithNonNullValue_ReturnsValue()
    {
        var result = _converter.Convert("test", typeof(string), null, _culture);
        Assert.Equal("test", result);
    }

    /// <summary>
    /// Tests that <see cref="NullSafePropertyConverter.Convert"/> returns parameter value when input is null.
    /// </summary>
    [Fact]
    public void Convert_WithNullValue_ReturnsParameter()
    {
        var result = _converter.Convert(null, typeof(string), "default", _culture);
        Assert.Equal("default", result);
    }

    /// <summary>
    /// Tests that <see cref="NullSafePropertyConverter.Convert"/> returns empty string when both value and parameter are null.
    /// </summary>
    [Fact]
    public void Convert_WithNullValueAndParameter_ReturnsEmptyString()
    {
        var result = _converter.Convert(null, typeof(string), null, _culture);
        Assert.Equal(string.Empty, result);
    }

    /// <summary>
    /// Tests that <see cref="NullSafePropertyConverter.ConvertBack"/> returns the value unchanged.
    /// </summary>
    [Fact]
    public void ConvertBack_ReturnsValueUnchanged()
    {
        var result = _converter.ConvertBack("test", typeof(string), null, _culture);
        Assert.Equal("test", result);
    }

    /// <summary>
    /// Tests that <see cref="NullSafePropertyConverter.ConvertBack"/> returns null when input is null.
    /// </summary>
    [Fact]
    public void ConvertBack_WithNullValue_ReturnsNull()
    {
        var result = _converter.ConvertBack(null, typeof(string), null, _culture);
        Assert.Null(result);
    }
}
