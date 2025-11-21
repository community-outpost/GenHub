using GenHub.Core.Models.Enums;

namespace GenHub.Tests.Core.Common.Models;

/// <summary>
/// Unit tests for <see cref="NavigationTab"/> enum.
/// </summary>
public class NavigationTabTests
{
    /// <summary>
    /// Verifies that all NavigationTab enum values are defined.
    /// </summary>
    [Fact]
    public void NavigationTab_AllValuesAreDefined()
    {
        var values = Enum.GetValues<NavigationTab>();
        Assert.Equal(5, values.Length);
        Assert.Contains(NavigationTab.Home, values);
        Assert.Contains(NavigationTab.GameProfiles, values);
        Assert.Contains(NavigationTab.Downloads, values);
        Assert.Contains(NavigationTab.Settings, values);
    }
}