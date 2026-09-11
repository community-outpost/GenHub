using GenHub.Core.Models.Tools.GenHotkeys;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.GenHotkeys;

/// <summary>
/// Unit tests for <see cref="HotkeyFaction"/> grouping and title formatting.
/// </summary>
public class HotkeyFactionTests
{
    /// <summary>
    /// Verifies that China generals are classified under the China faction group with correct titles.
    /// </summary>
    /// <param name="shortName">The faction short name.</param>
    /// <param name="displayName">The faction display name.</param>
    /// <param name="expectedGroup">The expected group name.</param>
    /// <param name="expectedTitle">The expected formatted dropdown title.</param>
    [Theory]
    [InlineData("PRC", "CHINA", "China", "CHINA")]
    [InlineData("INF", "INFANTRY", "China", "CHINA • INFANTRY")]
    [InlineData("NUK", "NUKE", "China", "CHINA • NUKE")]
    [InlineData("TNK", "TANK", "China", "CHINA • TANK")]
    public void FactionGroup_ChinaGenerals_ClassifiedCorrectly(string shortName, string displayName, string expectedGroup, string expectedTitle)
    {
        var faction = new HotkeyFaction
        {
            ShortName = shortName,
            DisplayName = displayName,
            DisplayNameDescription = "Test Faction",
        };

        Assert.Equal(expectedGroup, faction.FactionGroup);
        Assert.Equal(expectedTitle, faction.FormattedTitle);
    }

    /// <summary>
    /// Verifies that GLA generals are classified under the GLA faction group with correct titles.
    /// </summary>
    /// <param name="shortName">The faction short name.</param>
    /// <param name="displayName">The faction display name.</param>
    /// <param name="expectedGroup">The expected group name.</param>
    /// <param name="expectedTitle">The expected formatted dropdown title.</param>
    [Theory]
    [InlineData("GLA", "GLA", "GLA", "GLA")]
    [InlineData("TOX", "TOXIC", "GLA", "GLA • TOXIC")]
    [InlineData("STL", "STEALTH", "GLA", "GLA • STEALTH")]
    [InlineData("DML", "DEMO", "GLA", "GLA • DEMO")]
    public void FactionGroup_GlaGenerals_ClassifiedCorrectly(string shortName, string displayName, string expectedGroup, string expectedTitle)
    {
        var faction = new HotkeyFaction
        {
            ShortName = shortName,
            DisplayName = displayName,
            DisplayNameDescription = "Test Faction",
        };

        Assert.Equal(expectedGroup, faction.FactionGroup);
        Assert.Equal(expectedTitle, faction.FormattedTitle);
    }

    /// <summary>
    /// Verifies that USA generals are classified under the USA faction group with correct titles.
    /// </summary>
    /// <param name="shortName">The faction short name.</param>
    /// <param name="displayName">The faction display name.</param>
    /// <param name="expectedGroup">The expected group name.</param>
    /// <param name="expectedTitle">The expected formatted dropdown title.</param>
    [Theory]
    [InlineData("USA", "USA", "USA", "USA")]
    [InlineData("AIR", "AIR", "USA", "USA • AIR")]
    [InlineData("LSR", "LASER", "USA", "USA • LASER")]
    [InlineData("SWG", "SUPERWEAPON", "USA", "USA • SUPERWEAPON")]
    public void FactionGroup_UsaGenerals_ClassifiedCorrectly(string shortName, string displayName, string expectedGroup, string expectedTitle)
    {
        var faction = new HotkeyFaction
        {
            ShortName = shortName,
            DisplayName = displayName,
            DisplayNameDescription = "Test Faction",
        };

        Assert.Equal(expectedGroup, faction.FactionGroup);
        Assert.Equal(expectedTitle, faction.FormattedTitle);
    }
}
