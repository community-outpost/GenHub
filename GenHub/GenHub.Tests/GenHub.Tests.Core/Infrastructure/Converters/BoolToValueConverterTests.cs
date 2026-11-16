using GenHub.Infrastructure.Converters;
using System.Globalization;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="BoolToValueConverter"/>.
/// </summary>
public class BoolToValueConverterTests
{
    private readonly BoolToValueConverter _defaultConverter = new();
    private readonly BoolToValueConverter _customConverter = new() { TrueValue = "TrueValue", FalseValue = "FalseValue" };
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Tests that <see cref="BoolToValueConverter.Convert"/> returns TrueValue for true values.
    /// </summary>
    [Fact]
    public void Convert_WithTrueValue_ReturnsTrueValue()
    {
        var result = _customConverter.Convert(true, typeof(string), null, _culture);
        Assert.Equal("TrueValue", result);
    }

    /// <summary>
    /// Tests that <see cref="BoolToValueConverter.Convert"/> returns FalseValue for false values.
    /// </summary>
    [Fact]
    public void Convert_WithFalseValue_ReturnsFalseValue()
    {
        var result = _customConverter.Convert(false, typeof(string), null, _culture);
        Assert.Equal("FalseValue", result);
    }

    /// <summary>
    /// Tests that <see cref="BoolToValueConverter.Convert"/> returns null for null values with default converter.
    /// </summary>
    [Fact]
    public void Convert_WithNullValue_ReturnsNull()
    {
        var result = _defaultConverter.Convert(null, typeof(object), null, _culture);
        Assert.Null(result);
    }

    /// <summary>
    /// Tests that <see cref="BoolToValueConverter.Convert"/> returns FalseValue for non-boolean values.
    /// </summary>
    [Fact]
    public void Convert_WithNonBooleanValue_ReturnsFalseValue()
    {
        var result = _customConverter.Convert("not a boolean", typeof(string), null, _culture);
        Assert.Equal("FalseValue", result);
    }

    /// <summary>
    /// Tests that <see cref="BoolToValueConverter.ConvertBack"/> converts TrueValue back to true.
    /// </summary>
    [Fact]
    public void ConvertBack_WithTrueValue_ReturnsTrue()
    {
        var result = _customConverter.ConvertBack("TrueValue", typeof(bool), null, _culture);
        Assert.True((bool?)result);
    }

    /// <summary>
    /// Tests that <see cref="BoolToValueConverter.ConvertBack"/> converts FalseValue back to false.
    /// </summary>
    [Fact]
    public void ConvertBack_WithFalseValue_ReturnsFalse()
    {
        var result = _customConverter.ConvertBack("FalseValue", typeof(bool), null, _culture);
        Assert.False((bool?)result);
    }

    /// <summary>
    /// Tests that <see cref="BoolToValueConverter.ConvertBack"/> returns false for unknown values.
    /// </summary>
    [Fact]
    public void ConvertBack_WithUnknownValue_ReturnsFalse()
    {
        var result = _customConverter.ConvertBack("unknown", typeof(bool), null, _culture);
        Assert.False((bool?)result);
    }
}
