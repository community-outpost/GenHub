using GenHub.Core.Models.Enums;
using GenHub.Infrastructure.Converters;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="NavigationTabConverter"/>.
/// </summary>
public class NavigationTabConverterTests
{
    /// <summary>
    /// Verifies conversion of NavigationTab values to bool and int.
    /// </summary>
    /// <param name="value">The NavigationTab value to convert.</param>
    /// <param name="parameter">The NavigationTab parameter to compare against.</param>
    /// <param name="targetType">The target type for conversion.</param>
    /// <param name="expected">The expected result.</param>
    [Theory]
    [InlineData(NavigationTab.GameProfiles, NavigationTab.GameProfiles, typeof(bool), true)]
    [InlineData(NavigationTab.GameProfiles, NavigationTab.Downloads, typeof(bool), false)]
    [InlineData(NavigationTab.Downloads, NavigationTab.Downloads, typeof(bool), true)]
    [InlineData(NavigationTab.GameProfiles, NavigationTab.GameProfiles, typeof(int), 1)]
    [InlineData(NavigationTab.Downloads, NavigationTab.Downloads, typeof(int), 2)]
    public void Convert_ReturnsExpectedValue(NavigationTab value, NavigationTab parameter, Type targetType, object expected)
    {
        var result = NavigationTabConverter.Instance.Convert(value, targetType, parameter, null);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies conversion with invalid input returns default values.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="parameter">The parameter to compare against.</param>
    /// <param name="targetType">The target type for conversion.</param>
    /// <param name="expected">The expected result.</param>
    [Theory]
    [InlineData(null, NavigationTab.GameProfiles, typeof(bool), false)]
    [InlineData(NavigationTab.GameProfiles, null, typeof(bool), false)]
    [InlineData("invalid", NavigationTab.GameProfiles, typeof(bool), false)]
    public void Convert_WithInvalidInput_ReturnsDefault(object? value, object? parameter, Type targetType, object expected)
    {
        var result = NavigationTabConverter.Instance.Convert(value, targetType, parameter, null);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies ConvertBack returns correct NavigationTab for valid indices.
    /// </summary>
    /// <param name="value">The integer value to convert back.</param>
    /// <param name="expected">The expected NavigationTab.</param>
    [Theory]
    [InlineData(0, NavigationTab.Home)]
    [InlineData(1, NavigationTab.GameProfiles)]
    [InlineData(2, NavigationTab.Downloads)]
    [InlineData(3, NavigationTab.Tools)]
    [InlineData(4, NavigationTab.Settings)]
    public void ConvertBack_WithValidIndex_ReturnsTab(int value, NavigationTab expected)
    {
        var result = NavigationTabConverter.Instance.ConvertBack(value, typeof(NavigationTab), null, null);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies ConvertBack throws for invalid input.
    /// </summary>
    [Fact]
    public void ConvertBack_WithInvalidValue_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            NavigationTabConverter.Instance.ConvertBack("invalid", typeof(NavigationTab), null, null));
    }
}
