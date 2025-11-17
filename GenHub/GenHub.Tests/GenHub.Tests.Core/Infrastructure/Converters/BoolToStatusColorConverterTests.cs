using System.Globalization;
using GenHub.Core.Constants;
using GenHub.Infrastructure.Converters;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="BoolToStatusColorConverter"/>.
/// </summary>
public class BoolToStatusColorConverterTests
{
    private readonly BoolToStatusColorConverter _converter = new();
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Tests that <see cref="BoolToStatusColorConverter.Convert"/> returns success color for true values.
    /// </summary>
    [Fact]
    public void Convert_WithTrueValue_ReturnsSuccessColor()
    {
        var result = _converter.Convert(true, typeof(string), null, _culture);
        Assert.Equal(UiConstants.StatusSuccessColor, result);
    }

    /// <summary>
    /// Tests that <see cref="BoolToStatusColorConverter.Convert"/> returns error color for false values.
    /// </summary>
    [Fact]
    public void Convert_WithFalseValue_ReturnsErrorColor()
    {
        var result = _converter.Convert(false, typeof(string), null, _culture);
        Assert.Equal(UiConstants.StatusErrorColor, result);
    }

    /// <summary>
    /// Tests that <see cref="BoolToStatusColorConverter.Convert"/> returns error color for null values.
    /// </summary>
    [Fact]
    public void Convert_WithNullValue_ReturnsErrorColor()
    {
        var result = _converter.Convert(null, typeof(string), null, _culture);
        Assert.Equal(UiConstants.StatusErrorColor, result);
    }

    /// <summary>
    /// Tests that <see cref="BoolToStatusColorConverter.Convert"/> returns error color for non-boolean values.
    /// </summary>
    [Fact]
    public void Convert_WithNonBooleanValue_ReturnsErrorColor()
    {
        var result = _converter.Convert("not a boolean", typeof(string), null, _culture);
        Assert.Equal(UiConstants.StatusErrorColor, result);
    }

    /// <summary>
    /// Tests that <see cref="BoolToStatusColorConverter.ConvertBack"/> throws <see cref="NotImplementedException"/>.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        Assert.Throws<NotImplementedException>(() =>
            _converter.ConvertBack("any value", typeof(bool), null, _culture));
    }
}