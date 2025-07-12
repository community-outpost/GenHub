using GenHub.Infrastructure.Converters;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="TabIndexToVisibilityConverter"/>.
/// </summary>
public class TabIndexToVisibilityConverterTests
{
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 1, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, false)]
    [InlineData(null, 0, false)]
    [InlineData(0, null, false)]
    public void Convert_ReturnsExpected(object? value, object? parameter, bool expected)
    {
        var result = TabIndexToVisibilityConverter.Instance.Convert(value, typeof(bool), parameter, null);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ConvertBack_Throws()
    {
        Assert.Throws<System.NotImplementedException>(() =>
            TabIndexToVisibilityConverter.Instance.ConvertBack(true, typeof(object), null, null));
    }
}
