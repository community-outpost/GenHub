using GenHub.Common.Models;
using Xunit;

namespace GenHub.Tests.Core.Common.Models;

/// <summary>
/// Unit tests for <see cref="NavigationTab"/> enum.
/// </summary>
public class NavigationTabTests
{
    /// <summary>
    /// Verifies that the NavigationTab enum values match their expected integer values.
    /// </summary>
    /// <param name="tab">The tab enum value.</param>
    /// <param name="expectedValue">The expected integer value.</param>
    [Theory]
    [InlineData(NavigationTab.GameProfiles, 0)]
    [InlineData(NavigationTab.Downloads, 1)]
    [InlineData(NavigationTab.Settings, 2)]
    public void NavigationTab_HasExpectedValues(NavigationTab tab, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)tab);
    }

    /// <summary>
    /// Verifies that all NavigationTab enum values are defined.
    /// </summary>
    [Fact]
    public void NavigationTab_AllValuesAreDefined()
    {
        var values = Enum.GetValues<NavigationTab>();
        Assert.Equal(3, values.Length);
        Assert.Contains(NavigationTab.GameProfiles, values);
        Assert.Contains(NavigationTab.Downloads, values);
        Assert.Contains(NavigationTab.Settings, values);
    }
}
