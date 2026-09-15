using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Pins the single definition of an unusable client version. Installation manifest ids are minted
/// from this decision in several places, and a site that disagrees with registration produces an id
/// that resolves to no manifest.
/// </summary>
public class UnknownVersionPredicateTests
{
    /// <summary>
    /// Verifies the values that carry no usable version, including the two that previously differed
    /// between sites.
    /// </summary>
    /// <param name="version">The version under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Unknown")]
    [InlineData("unknown")]
    [InlineData("Auto-Updated")]
    [InlineData("auto-updated")]
    public void IsUnknownVersion_WithUnusableValue_ReturnsTrue(string? version)
    {
        Assert.True(GameVersionHelper.IsUnknownVersion(version));
    }

    /// <summary>
    /// Verifies that real versions are still treated as usable, so the predicate cannot swallow one.
    /// </summary>
    /// <param name="version">The version under test.</param>
    [Theory]
    [InlineData("1.04")]
    [InlineData("1.08")]
    [InlineData("2")]
    public void IsUnknownVersion_WithRealVersion_ReturnsFalse(string version)
    {
        Assert.False(GameVersionHelper.IsUnknownVersion(version));
    }

    /// <summary>
    /// Verifies the sentinel constant matches the literal the migrated sites used, so replacing the
    /// hand-rolled copies did not change which values count as unknown.
    /// </summary>
    [Fact]
    public void AutoUpdatedVersionConstant_MatchesTheLiteralTheOldSitesUsed()
    {
        Assert.Equal("Auto-Updated", GameClientConstants.AutoUpdatedVersion);
    }

    /// <summary>
    /// Verifies that a version the predicate accepts can always be turned into a manifest id. The id
    /// generator rejects non-numeric versions, so a value that escapes the predicate throws.
    /// </summary>
    /// <param name="version">The version under test.</param>
    [Theory]
    [InlineData("Auto-Updated")]
    [InlineData("   ")]
    [InlineData("Unknown")]
    public void SentinelVersions_NeverReachTheIdGeneratorVerbatim(string version)
    {
        Assert.True(GameVersionHelper.IsUnknownVersion(version));
        Assert.Equal(
            ManifestConstants.ZeroHourManifestVersion,
            GameVersionHelper.ResolveInstallationVersion(version, GameType.ZeroHour));
    }
}
