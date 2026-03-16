using GenHub.Core.Constants;
using GenHub.Core.Helpers;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Tests for the VersionComparer utility class.
/// </summary>
public class VersionComparerTests
{
    /// <summary>
    /// Verifies that date-based versions (typical for Community Patch) are compared correctly.
    /// </summary>
    /// <param name="version1">The first version string.</param>
    /// <param name="version2">The second version string.</param>
    /// <param name="expected">The expected comparison result.</param>
    [Theory]
    [InlineData("2025-12-29", "2025-12-28", 1)] // Newer date
    [InlineData("2025-12-28", "2025-12-29", -1)] // Older date
    [InlineData("2025-12-29", "2025-12-29", 0)] // Same date
    [InlineData("2025-11-07", "2025-12-26", -1)] // Different months
    [InlineData("2026-01-01", "2025-12-31", 1)] // Different years
    public void CompareVersions_CommunityOutpost_DateVersions_ReturnsCorrectComparison(
        string version1,
        string version2,
        int expected)
    {
        // Act
        var result = VersionComparer.CompareVersions(version1, version2, CommunityOutpostConstants.PublisherType);

        // Assert
        Assert.Equal(expected, Math.Sign(result));
    }

    /// <summary>
    /// Verifies that numeric versions (typical for SuperHackers) are compared correctly.
    /// </summary>
    /// <param name="version1">The first version string.</param>
    /// <param name="version2">The second version string.</param>
    /// <param name="expected">The expected comparison result.</param>
    [Theory]
    [InlineData("20251229", "20251228", 1)] // Newer version
    [InlineData("20251228", "20251229", -1)] // Older version
    [InlineData("20251229", "20251229", 0)] // Same version
    [InlineData("20251226", "20241226", 1)] // Different years
    [InlineData("20260116", "260116", 0)] // YYYYMMDD vs YYMMDD (same date)
    [InlineData("270116", "260116", 1)] // YYMMDD comparison
    [InlineData("1.20260116", "20260116", 1)] // Semantic 1.YYYYMMDD > YYYYMMDD (fallback to semantic)
    public void CompareVersions_TheSuperHackers_NumericVersions_ReturnsCorrectComparison(
        string version1,
        string version2,
        int expected)
    {
        // Act
        var result = VersionComparer.CompareVersions(version1, version2, PublisherTypeConstants.TheSuperHackers);

        // Assert
        Assert.Equal(expected, Math.Sign(result));
    }

    /// <summary>
    /// Verifies that standard numeric versions (GeneralsOnline) are compared correctly.
    /// </summary>
    /// <param name="version1">The first version string.</param>
    /// <param name="version2">The second version string.</param>
    /// <param name="expected">The expected comparison result.</param>
    [Theory]
    [InlineData("1.0", "1.0", 0)] // Same version
    [InlineData("2.0", "1.0", 1)] // Newer version
    [InlineData("1.0", "2.0", -1)] // Older version
    [InlineData("1.10", "1.9", 1)] // Semantic: 10 > 9
    [InlineData("1.9.1", "1.9", 1)] // Semantic: 9.1 > 9.0
    [InlineData("2.0.0", "1.99.99", 1)] // Major version change
    [InlineData("v1.2.3", "1.2.3", 0)] // Prefix handling (extracted digits)
    public void CompareVersions_GeneralsOnline_NumericVersions_ReturnsCorrectComparison(
        string version1,
        string version2,
        int expected)
    {
        // Act
        var result = VersionComparer.CompareVersions(version1, version2, PublisherTypeConstants.GeneralsOnline);

        // Assert
        Assert.Equal(expected, Math.Sign(result));
    }

    /// <summary>
    /// Verifies that null or empty version strings are handled correctly (null is older).
    /// </summary>
    /// <param name="version1">The first version string.</param>
    /// <param name="version2">The second version string.</param>
    /// <param name="expected">The expected comparison result.</param>
    [Theory]
    [InlineData(null, null, 0)]
    [InlineData("", "", 0)]
    [InlineData(null, "1.0", -1)]
    [InlineData("1.0", null, 1)]
    [InlineData("", "1.0", -1)]
    [InlineData("1.0", "", 1)]
    public void CompareVersions_NullOrEmpty_ReturnsCorrectComparison(
        string? version1,
        string? version2,
        int expected)
    {
        // Act
        var result = VersionComparer.CompareVersions(version1, version2, "unknown");

        // Assert
        Assert.Equal(expected, Math.Sign(result));
    }

    /// <summary>
    /// Verifies that mixed formats (YYYY-MM-DD vs YYYYMMDD) are normalized and compared correctly.
    /// </summary>
    /// <param name="version1">The first version string.</param>
    /// <param name="version2">The second version string.</param>
    /// <param name="expected">The expected comparison result.</param>
    [Theory]
    [InlineData("2025-12-29", "20251229", 0)] // Date format vs numeric format (same date)
    [InlineData("2025-12-30", "20251229", 1)] // Date format vs numeric format (newer)
    [InlineData("2025-12-28", "20251229", -1)] // Date format vs numeric format (older)
    public void CompareVersions_CommunityOutpost_MixedFormats_ReturnsCorrectComparison(
        string version1,
        string version2,
        int expected)
    {
        // Act
        var result = VersionComparer.CompareVersions(version1, version2, CommunityOutpostConstants.PublisherType);

        // Assert
        Assert.Equal(expected, Math.Sign(result));
    }

    /// <summary>
    /// Verifies that unknown publisher types fall back to standard string comparison.
    /// </summary>
    [Fact]
    public void CompareVersions_UnknownPublisher_FallsBackToStringComparison()
    {
        // Arrange
        var version1 = "abc";
        var version2 = "def";

        // Act
        var result = VersionComparer.CompareVersions(version1, version2, "unknown-publisher");

        // Assert - "abc" < "def" in ordinal comparison
        Assert.True(result < 0);
    }

    /// <summary>
    /// Verifies that numeric extraction works for unknown publishers when versions are numeric.
    /// </summary>
    /// <param name="version1">The first version string.</param>
    /// <param name="version2">The second version string.</param>
    /// <param name="expected">The expected comparison result.</param>
    [Theory]
    [InlineData("1.04", "1.08", -1)] // Dotted versions
    [InlineData("104", "108", -1)] // Numeric versions
    [InlineData("1.08", "1.04", 1)] // Reverse order
    public void CompareVersions_UnknownPublisher_NumericExtraction_ReturnsCorrectComparison(
        string version1,
        string version2,
        int expected)
    {
        // Act
        var result = VersionComparer.CompareVersions(version1, version2, null);

        // Assert
        Assert.Equal(expected, Math.Sign(result));
    }
}
