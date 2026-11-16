using GenHub.Infrastructure.Converters;
using System.Globalization;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="NotNullConverter"/>.
/// </summary>
public class NotNullConverterTests
{
    private readonly NotNullConverter _converter = new();
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Tests that <see cref="NotNullConverter.Convert"/> returns true for non-null values.
    /// </summary>
    [Fact]
    public void Convert_WithNonNullValue_ReturnsTrue()
    {
        var result = _converter.Convert("test", typeof(bool), null, _culture);
        Assert.True((bool?)result);
    }

    /// <summary>
    /// Tests that <see cref="NotNullConverter.Convert"/> returns true for empty string.
    /// </summary>
    [Fact]
    public void Convert_WithEmptyString_ReturnsTrue()
    {
        var result = _converter.Convert(string.Empty, typeof(bool), null, _culture);
        Assert.True((bool?)result);
    }

    /// <summary>
    /// Tests that <see cref="NotNullConverter.Convert"/> returns false for null values.
    /// </summary>
    [Fact]
    public void Convert_WithNullValue_ReturnsFalse()
    {
        var result = _converter.Convert(null, typeof(bool), null, _culture);
        Assert.False((bool?)result);
    }

    /// <summary>
    /// Tests that <see cref="NotNullConverter.ConvertBack"/> throws <see cref="NotImplementedException"/>.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        Assert.Throws<NotImplementedException>(() =>
            _converter.ConvertBack(true, typeof(object), null, _culture));
    }
}
