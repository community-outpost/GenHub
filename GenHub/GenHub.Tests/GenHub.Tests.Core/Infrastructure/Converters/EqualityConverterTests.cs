using GenHub.Infrastructure.Converters;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="EqualityConverter"/>.
/// </summary>
public class EqualityConverterTests
{
    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, false)]
    [InlineData("a", "a", true)]
    [InlineData("a", "b", false)]
    [InlineData(null, 1, false)]
    [InlineData(1, null, false)]
    public void Convert_ComparesValues(object? value, object? parameter, bool expected)
    {
        var result = EqualityConverter.Instance.Convert(value, typeof(bool), parameter, null);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ConvertBack_Throws()
    {
        Assert.Throws<System.NotImplementedException>(() =>
            EqualityConverter.Instance.ConvertBack(true, typeof(object), null, null));
    }
}
