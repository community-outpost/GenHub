using GenHub.Core.Services.Providers.VersionSchemes;

namespace GenHub.Tests.Core.Services.Providers;

/// <summary>
/// Tests for <see cref="MmddyyQfeVersionScheme"/>.
/// </summary>
public class MmddyyQfeVersionSchemeTests
{
    private readonly MmddyyQfeVersionScheme _scheme = new();

    /// <summary>
    /// Verifies that versions parse into year, month, day and QFE components.
    /// </summary>
    /// <param name="version">The version string.</param>
    /// <param name="year">The expected year.</param>
    /// <param name="month">The expected month.</param>
    /// <param name="day">The expected day.</param>
    /// <param name="qfe">The expected QFE number.</param>
    [Theory]
    [InlineData("101525_QFE2", 2025, 10, 15, 2)]
    [InlineData("060526_QFE1", 2026, 6, 5, 1)]
    [InlineData("042826_QFE3_EAC", 2026, 4, 28, 3)]
    [InlineData("011526_QFE1_EAC_X86", 2026, 1, 15, 1)]
    [InlineData("042826_QFE10", 2026, 4, 28, 10)]
    [InlineData("042826_qfe3", 2026, 4, 28, 3)]
    public void TryParse_ReadsDateAndQfe(string version, int year, int month, int day, int qfe)
    {
        Assert.True(_scheme.TryParse(version, out var result));
        Assert.Equal(new long[] { year, month, day, qfe }, result.Components);
    }

    /// <summary>
    /// Verifies that the QFE segment is located by its marker rather than by position.
    /// </summary>
    [Fact]
    public void TryParse_FindsQfeSegmentRegardlessOfPosition()
    {
        Assert.True(_scheme.TryParse("042826_EAC_QFE3", out var result));
        Assert.Equal(new long[] { 2026, 4, 28, 3 }, result.Components);
    }

    /// <summary>
    /// Verifies that two-digit years always map to the publisher's 2000-2099 range.
    /// </summary>
    [Fact]
    public void TryParse_UsesExplicitTwentyFirstCenturyPolicy()
    {
        Assert.True(_scheme.TryParse("010130_QFE1", out var result));
        Assert.Equal(new long[] { 2030, 1, 1, 1 }, result.Components);
    }

    /// <summary>
    /// Verifies that malformed versions are rejected without throwing.
    /// </summary>
    /// <param name="version">The version string.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("042826")]
    [InlineData("ABCDEF_QFE1")]
    [InlineData("042826_QFEx")]
    [InlineData("133126_QFE1")]
    [InlineData("043126_QFE1")]
    [InlineData("0428267_QFE1")]
    [InlineData("042826_EAC")]
    [InlineData("042826_QFE-1")]
    [InlineData("042826__QFE3")]
    [InlineData("_042826_QFE3")]
    [InlineData("042826_QFE3_")]
    [InlineData("042826_QFE1_QFE2")]
    public void TryParse_RejectsMalformedVersions(string? version)
    {
        Assert.False(_scheme.TryParse(version, out var result));
        Assert.True(result.IsEmpty);
    }

    /// <summary>
    /// Verifies ordering across months, years, QFE numbers and build tags.
    /// </summary>
    /// <param name="version1">The first version.</param>
    /// <param name="version2">The second version.</param>
    /// <param name="expected">The expected sign of the comparison.</param>
    [Theory]
    [InlineData("060526_QFE1", "121525_QFE1", 1)] // Jun 2026 beats Dec 2025 despite the smaller MMDDYY integer
    [InlineData("042826_QFE3", "111825_QFE2", 1)] // Apr 2026 beats Nov 2025
    [InlineData("010126_QFE1", "123125_QFE9", 1)] // Across the year boundary
    [InlineData("042826_QFE10", "042826_QFE9", 1)] // QFE beyond a single digit
    [InlineData("042826_QFE3_EAC", "042826_QFE3", 0)] // Build tags do not affect ordering
    [InlineData("042826_QFE2", "042826_QFE3_EAC", -1)]
    public void Compare_OrdersByDateThenQfe(string version1, string version2, int expected)
    {
        Assert.Equal(expected, Math.Sign(_scheme.Compare(version1, version2)));
    }

    /// <summary>
    /// Verifies that an unreadable version is ordered below a readable one, so a broken
    /// installed version never suppresses an available update.
    /// </summary>
    /// <param name="version1">The first version.</param>
    /// <param name="version2">The second version.</param>
    /// <param name="expected">The expected sign of the comparison.</param>
    [Theory]
    [InlineData("060526_QFE1", "Unknown", 1)]
    [InlineData("Unknown", "060526_QFE1", -1)]
    [InlineData("Unknown", "Unknown", 0)]
    public void Compare_TreatsUnreadableVersionsAsOlder(string version1, string version2, int expected)
    {
        Assert.Equal(expected, Math.Sign(_scheme.Compare(version1, version2)));
    }
}
