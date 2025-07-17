using GenHub.Infrastructure.Converters;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="StringToIntConverter"/>.
/// </summary>
public class StringToIntConverterTests
{
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

    [Theory]
    [InlineData(5, "5")]
    [InlineData(0, "0")]
    public void ConvertBack_ReturnsString(int value, string expected)
    {
        var result = StringToIntConverter.Instance.ConvertBack(value, typeof(string), null, null);
        Assert.Equal(expected, result);
    }
}
