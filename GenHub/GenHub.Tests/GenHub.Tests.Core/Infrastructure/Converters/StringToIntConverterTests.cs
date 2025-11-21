using GenHub.Infrastructure.Converters;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="StringToIntConverter"/>.
/// </summary>
public class StringToIntConverterTests
{
    /// <summary>
    /// Tests that <see cref="StringToIntConverter.Convert"/> parses a string to int or returns the int value.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="expected">The expected integer result.</param>
    [Theory]
    [InlineData("0", 0)]
    [InlineData("42", 42)]
    [InlineData("-1", -1)]
    [InlineData(5, 5)]
    [InlineData(null, 0)]
    [InlineData("notanint", 0)]
    public void Convert_ParsesStringOrReturnsInt(object? value, int expected)
    {
        var result = StringToIntConverter.Instance.Convert(value, typeof(int), null, null);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests that <see cref="StringToIntConverter.ConvertBack"/> returns the string representation of an integer.
    /// </summary>
    /// <param name="value">The integer value to convert back.</param>
    /// <param name="expected">The expected string result.</param>
    [Theory]
    [InlineData(5, "5")]
    [InlineData(0, "0")]
    public void ConvertBack_ReturnsString(int value, string expected)
    {
        var result = StringToIntConverter.Instance.ConvertBack(value, typeof(string), null, null);
        Assert.Equal(expected, result);
    }
}