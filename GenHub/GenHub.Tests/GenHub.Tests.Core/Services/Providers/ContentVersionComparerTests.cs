using GenHub.Core.Constants;
using GenHub.Tests.Core.Helpers;

namespace GenHub.Tests.Core.Services.Providers;

/// <summary>
/// Tests for <see cref="GenHub.Core.Services.Providers.ContentVersionComparer"/>, which routes
/// each publisher to the version scheme named by its provider definition.
/// </summary>
public class ContentVersionComparerTests
{
    private readonly GenHub.Core.Interfaces.Providers.IContentVersionComparer _comparer = TestVersionComparer.CreateDefault();

    /// <summary>
    /// The regression this whole mechanism exists for: a Generals Online release from June 2026
    /// must supersede one from December 2025. Ordering by the MMDDYY integer reported the opposite,
    /// which silently suppressed the update prompt.
    /// </summary>
    [Fact]
    public void IsNewer_GeneralsOnline_DetectsUpdateAcrossYearBoundary()
    {
        Assert.True(_comparer.IsNewer("060526_QFE1", "121525_QFE1", PublisherTypeConstants.GeneralsOnline));
        Assert.False(_comparer.IsNewer("121525_QFE1", "060526_QFE1", PublisherTypeConstants.GeneralsOnline));
    }

    /// <summary>
    /// Verifies Generals Online ordering by date, then QFE, ignoring build tags.
    /// </summary>
    /// <param name="version1">The first version.</param>
    /// <param name="version2">The second version.</param>
    /// <param name="expected">The expected sign of the comparison.</param>
    [Theory]
    [InlineData("042826_QFE3_EAC", "042826_QFE3_EAC", 0)]
    [InlineData("042826_QFE4_EAC", "042826_QFE3_EAC", 1)]
    [InlineData("042826_QFE3_EAC", "042826_QFE2", 1)]
    [InlineData("042826_QFE2", "042826_QFE2_EAC", 0)]
    [InlineData("042926_QFE1_EAC", "042826_QFE3_EAC", 1)]
    [InlineData("060526_QFE1", "042826_QFE3_EAC", 1)]
    public void Compare_GeneralsOnline_ReturnsCorrectOrder(string version1, string version2, int expected)
    {
        var result = _comparer.Compare(version1, version2, PublisherTypeConstants.GeneralsOnline);

        Assert.Equal(expected, Math.Sign(result));
    }

    /// <summary>
    /// Verifies that Community Outpost date versions compare by calendar date.
    /// </summary>
    /// <param name="version1">The first version.</param>
    /// <param name="version2">The second version.</param>
    /// <param name="expected">The expected sign of the comparison.</param>
    [Theory]
    [InlineData("2025-12-29", "2025-12-28", 1)]
    [InlineData("2025-12-28", "2025-12-29", -1)]
    [InlineData("2025-12-29", "2025-12-29", 0)]
    [InlineData("2025-11-07", "2025-12-26", -1)]
    [InlineData("2026-01-01", "2025-12-31", 1)]
    [InlineData("2025-12-29", "20251229", 0)]
    [InlineData("2025-12-30", "20251229", 1)]
    [InlineData("2025-12-28", "20251229", -1)]
    public void Compare_CommunityOutpost_ReturnsCorrectOrder(string version1, string version2, int expected)
    {
        var result = _comparer.Compare(version1, version2, CommunityOutpostConstants.PublisherType);

        Assert.Equal(expected, Math.Sign(result));
    }

    /// <summary>
    /// Verifies that TheSuperHackers numeric and date-stamp versions compare correctly.
    /// </summary>
    /// <param name="version1">The first version.</param>
    /// <param name="version2">The second version.</param>
    /// <param name="expected">The expected sign of the comparison.</param>
    [Theory]
    [InlineData("20251229", "20251228", 1)]
    [InlineData("20251228", "20251229", -1)]
    [InlineData("20251229", "20251229", 0)]
    [InlineData("20251226", "20241226", 1)]
    [InlineData("20260116", "260116", 0)]
    [InlineData("270116", "260116", 1)]
    [InlineData("1.20260116", "20260116", 1)]
    [InlineData("weekly-2025-12-26", "weekly-2025-11-21", 1)]
    public void Compare_TheSuperHackers_ReturnsCorrectOrder(string version1, string version2, int expected)
    {
        var result = _comparer.Compare(version1, version2, PublisherTypeConstants.TheSuperHackers);

        Assert.Equal(expected, Math.Sign(result));
    }

    /// <summary>
    /// Verifies that semantic versions compare segment by segment under the default scheme.
    /// </summary>
    /// <param name="version1">The first version.</param>
    /// <param name="version2">The second version.</param>
    /// <param name="expected">The expected sign of the comparison.</param>
    [Theory]
    [InlineData("1.0", "1.0", 0)]
    [InlineData("2.0", "1.0", 1)]
    [InlineData("1.0", "2.0", -1)]
    [InlineData("1.10", "1.9", 1)]
    [InlineData("1.9.1", "1.9", 1)]
    [InlineData("2.0.0", "1.99.99", 1)]
    [InlineData("v1.2.3", "1.2.3", 0)]
    [InlineData("1.04", "1.08", -1)]
    [InlineData("104", "108", -1)]
    [InlineData("1.08", "1.04", 1)]
    public void Compare_UnknownPublisher_UsesDefaultScheme(string version1, string version2, int expected)
    {
        var result = _comparer.Compare(version1, version2, null);

        Assert.Equal(expected, Math.Sign(result));
    }

    /// <summary>
    /// Verifies that missing versions order below present ones.
    /// </summary>
    /// <param name="version1">The first version.</param>
    /// <param name="version2">The second version.</param>
    /// <param name="expected">The expected sign of the comparison.</param>
    [Theory]
    [InlineData(null, null, 0)]
    [InlineData("", "", 0)]
    [InlineData(null, "1.0", -1)]
    [InlineData("1.0", null, 1)]
    [InlineData("", "1.0", -1)]
    [InlineData("1.0", "", 1)]
    public void Compare_NullOrEmpty_OrdersMissingVersionsFirst(string? version1, string? version2, int expected)
    {
        var result = _comparer.Compare(version1, version2, "unknown");

        Assert.Equal(expected, Math.Sign(result));
    }

    /// <summary>
    /// Verifies that a publisher with no definition falls back to the default scheme
    /// rather than failing.
    /// </summary>
    [Fact]
    public void Compare_UnregisteredPublisher_FallsBackToDefaultScheme()
    {
        Assert.Equal(VersionSchemeConstants.Default, _comparer.GetScheme("nobody-ships-this").SchemeId);
        Assert.True(_comparer.Compare("def", "abc", "nobody-ships-this") > 0);
    }

    /// <summary>
    /// Verifies that a scheme can be used directly as a LINQ ordering comparer, which is how
    /// callers pick the newest installed version.
    /// </summary>
    [Fact]
    public void GetScheme_OrdersVersionsForLinq()
    {
        string[] installed = ["121525_QFE1", "060526_QFE1", "042826_QFE3_EAC"];

        var newest = installed
            .OrderByDescending(version => version, _comparer.GetScheme(PublisherTypeConstants.GeneralsOnline))
            .First();

        Assert.Equal("060526_QFE1", newest);
    }
}
