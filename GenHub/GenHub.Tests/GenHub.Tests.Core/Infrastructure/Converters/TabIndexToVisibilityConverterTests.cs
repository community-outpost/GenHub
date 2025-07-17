using System;
using GenHub.Infrastructure.Converters;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="TabIndexToVisibilityConverter"/>.
/// </summary>
public class TabIndexToVisibilityConverterTests
{
    /// <summary>
    /// Tests that <see cref="TabIndexToVisibilityConverter.Convert"/> returns the expected result
    /// when comparing the value and parameter.
    /// </summary>
    /// <param name="value">The value to compare.</param>
    /// <param name="parameter">The parameter to compare against.</param>
    /// <param name="expected">The expected boolean result.</param>
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 1, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, false)]
    [InlineData(null, 0, false)]
    [InlineData(0, null, false)]
    public void Convert_ReturnsExpected(object? value, object? parameter, bool expected)
    {
        var result = TabIndexToVisibilityConverter.Instance.Convert(value, typeof(bool), parameter, null) as bool?;
        Assert.Equal(expected, result.GetValueOrDefault());
    }

    /// <summary>
    /// Tests that <see cref="TabIndexToVisibilityConverter.ConvertBack"/> throws <see cref="NotImplementedException"/>.
    /// </summary>
    [Fact]
    public void ConvertBack_Throws()
    {
        Assert.Throws<NotImplementedException>(() =>
            TabIndexToVisibilityConverter.Instance.ConvertBack(true, typeof(object), null, null));
    }
}
