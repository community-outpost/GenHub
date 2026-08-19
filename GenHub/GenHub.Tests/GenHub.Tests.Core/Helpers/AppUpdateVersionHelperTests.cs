using GenHub.Core.Helpers;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="AppUpdateVersionHelper"/>.
/// </summary>
public class AppUpdateVersionHelperTests
{
    /// <summary>
    /// Tests that ExtractRunNumber extracts expected run numbers.
    /// </summary>
    /// <param name="version">The version string to extract the run number from.</param>
    /// <param name="expectedRun">The expected run number.</param>
    [Theory]
    [InlineData("0.0.1282-pr265", 1282)]
    [InlineData("0.0.1287-pr265", 1287)]
    [InlineData("0.0.1287-main", 1287)]
    [InlineData("0.0.1287-development", 1287)]
    [InlineData("0.0.1300-fix-ci.9", 1300)]
    [InlineData("0.0.1287", 1287)]
    [InlineData("0.0.0-ci.500", 500)]
    [InlineData("1.0.42", 0)]
    [InlineData("1.2.5", 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData(null, 0)]
    [InlineData("abc", 0)]
    public void ExtractRunNumber_WithVariousFormats_ShouldReturnExpectedNumber(string? version, int expectedRun)
    {
        var result = AppUpdateVersionHelper.ExtractRunNumber(version);
        Assert.Equal(expectedRun, result);
    }

    /// <summary>
    /// Tests that IsArtifactVersionNewer returns true when new run is greater.
    /// </summary>
    [Fact]
    public void IsArtifactVersionNewer_WhenNewerRun_ShouldReturnTrue()
    {
        var result = AppUpdateVersionHelper.IsArtifactVersionNewer("0.0.1287-pr265", "0.0.1282-pr265");
        Assert.True(result);
    }

    /// <summary>
    /// Tests that IsArtifactVersionNewer returns false when same run.
    /// </summary>
    [Fact]
    public void IsArtifactVersionNewer_WhenSameRun_ShouldReturnFalse()
    {
        var result = AppUpdateVersionHelper.IsArtifactVersionNewer("0.0.1282-pr265", "0.0.1282-pr265");
        Assert.False(result);
    }

    /// <summary>
    /// Tests that IsArtifactVersionNewer returns false when older run.
    /// </summary>
    [Fact]
    public void IsArtifactVersionNewer_WhenOlderRun_ShouldReturnFalse()
    {
        var result = AppUpdateVersionHelper.IsArtifactVersionNewer("0.0.1280-pr265", "0.0.1282-pr265");
        Assert.False(result);
    }

    /// <summary>
    /// Tests that IsArtifactVersionNewer works for branch versions.
    /// </summary>
    [Fact]
    public void IsArtifactVersionNewer_BranchVersions_ShouldCompareCorrectly()
    {
        Assert.True(AppUpdateVersionHelper.IsArtifactVersionNewer("0.0.1287-main", "0.0.1282-main"));
        Assert.False(AppUpdateVersionHelper.IsArtifactVersionNewer("0.0.1282-main", "0.0.1282-main"));
    }

    /// <summary>
    /// Tests that IsArtifactVersionNewer handles null or empty inputs.
    /// </summary>
    [Fact]
    public void IsArtifactVersionNewer_WithNullOrEmpty_ShouldHandleGracefully()
    {
        Assert.False(AppUpdateVersionHelper.IsArtifactVersionNewer(null, "0.0.1282-pr265"));
        Assert.False(AppUpdateVersionHelper.IsArtifactVersionNewer(string.Empty, "0.0.1282-pr265"));
        Assert.True(AppUpdateVersionHelper.IsArtifactVersionNewer("0.0.1282-pr265", null));
        Assert.True(AppUpdateVersionHelper.IsArtifactVersionNewer("0.0.1282-pr265", string.Empty));
    }

    /// <summary>
    /// Tests that fallback versions like 0.0.0 are not treated as newer than installed builds.
    /// </summary>
    [Fact]
    public void IsArtifactVersionNewer_FallbackZeroVersusValidRun_ShouldReturnFalse()
    {
        Assert.False(AppUpdateVersionHelper.IsArtifactVersionNewer("0.0.0", "0.0.1282-pr265"));
        Assert.True(AppUpdateVersionHelper.IsArtifactVersionNewer("0.0.1282-pr265", "0.0.0"));
    }

    /// <summary>
    /// Tests that standard version numbers compare correctly when no run number is present.
    /// </summary>
    [Fact]
    public void IsArtifactVersionNewer_SemanticVersion_ShouldCompareCorrectly()
    {
        Assert.True(AppUpdateVersionHelper.IsArtifactVersionNewer("1.2.5", "1.1.9"));
        Assert.False(AppUpdateVersionHelper.IsArtifactVersionNewer("1.1.9", "1.2.5"));
        Assert.True(AppUpdateVersionHelper.IsArtifactVersionNewer("1.2.0", "1.1.0"));
        Assert.False(AppUpdateVersionHelper.IsArtifactVersionNewer("1.1.0", "1.2.0"));
        Assert.False(AppUpdateVersionHelper.IsArtifactVersionNewer("1.0.0", "1.0.0"));
    }
}
