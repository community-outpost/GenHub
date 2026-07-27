using GenHub.Core.Models.Content;

namespace GenHub.Tests.Core.Models.Content;

/// <summary>
/// Tests for <see cref="ContentVersion"/> ordering.
/// </summary>
public class ContentVersionTests
{
    /// <summary>
    /// Verifies that components are compared most-significant first.
    /// </summary>
    [Fact]
    public void CompareTo_OrdersByMostSignificantComponentFirst()
    {
        var december2025 = new ContentVersion(2025, 12, 15, 1);
        var june2026 = new ContentVersion(2026, 6, 5, 1);

        Assert.True(june2026 > december2025);
    }

    /// <summary>
    /// Verifies that a later component only matters when the earlier ones tie.
    /// </summary>
    [Fact]
    public void CompareTo_UsesLaterComponentsOnlyAsTiebreaker()
    {
        var qfe1 = new ContentVersion(2026, 6, 5, 1);
        var qfe10 = new ContentVersion(2026, 6, 5, 10);

        Assert.True(qfe10 > qfe1);
    }

    /// <summary>
    /// Verifies that missing trailing components are treated as zero.
    /// </summary>
    [Fact]
    public void CompareTo_TreatsMissingTrailingComponentsAsZero()
    {
        Assert.Equal(new ContentVersion(1, 7), new ContentVersion(1, 7, 0));
        Assert.True(new ContentVersion(1, 7, 1) > new ContentVersion(1, 7));
    }

    /// <summary>
    /// Verifies that equal versions produce equal hash codes.
    /// </summary>
    [Fact]
    public void GetHashCode_MatchesForEquivalentVersions()
    {
        Assert.Equal(new ContentVersion(1, 7).GetHashCode(), new ContentVersion(1, 7, 0).GetHashCode());
    }

    /// <summary>
    /// Verifies that a default version carries no components.
    /// </summary>
    [Fact]
    public void Default_IsEmpty()
    {
        var version = default(ContentVersion);

        Assert.True(version.IsEmpty);
        Assert.Empty(version.Components);
    }
}
