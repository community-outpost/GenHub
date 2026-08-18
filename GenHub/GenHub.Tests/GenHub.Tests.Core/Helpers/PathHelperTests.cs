using GenHub.Core.Helpers;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Tests platform-aware filesystem path comparison behavior.
/// </summary>
public sealed class PathHelperTests
{
    /// <summary>
    /// Uses case-insensitive comparison only on Windows so case-sensitive Unix volumes remain distinct.
    /// </summary>
    [Fact]
    public void PathComparison_UsesWindowsOnlyCaseFolding()
    {
        var firstPath = Path.Combine(Path.GetTempPath(), "GenHub");
        var secondPath = Path.Combine(Path.GetTempPath(), "genhub");

        var pathsAreEqual = string.Equals(firstPath, secondPath, PathHelper.PathComparison);

        Assert.Equal(OperatingSystem.IsWindows(), pathsAreEqual);
    }

    /// <summary>
    /// Uses the same platform case behavior when paths are collection keys.
    /// </summary>
    [Fact]
    public void PathComparer_UsesWindowsOnlyCaseFolding()
    {
        var firstPath = Path.Combine(Path.GetTempPath(), "GenHub");
        var secondPath = Path.Combine(Path.GetTempPath(), "genhub");

        var pathsAreEqual = PathHelper.PathComparer.Equals(firstPath, secondPath);

        Assert.Equal(OperatingSystem.IsWindows(), pathsAreEqual);
    }

    /// <summary>
    /// Accepts the base directory itself and anything nested beneath it.
    /// </summary>
    /// <param name="relativeCandidate">A candidate path relative to the base directory.</param>
    [Theory]
    [InlineData("")]
    [InlineData("file.dat")]
    [InlineData("nested/deeper/file.dat")]
    [InlineData("nested/../file.dat")]
    public void IsPathWithinDirectory_AcceptsContainedPaths(string relativeCandidate)
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "GenHubContainment");
        var candidate = Path.Combine(baseDirectory, relativeCandidate);

        Assert.True(PathHelper.IsPathWithinDirectory(baseDirectory, candidate));
    }

    /// <summary>
    /// Rejects traversal segments, escapes that only appear after normalization, and sibling
    /// directories that merely share a name prefix with the base directory.
    /// </summary>
    /// <param name="relativeCandidate">A candidate path relative to the base directory.</param>
    [Theory]
    [InlineData("..")]
    [InlineData("../escaped.dat")]
    [InlineData("nested/../../escaped.dat")]
    [InlineData("../GenHubContainmentEvil/escaped.dat")]
    public void IsPathWithinDirectory_RejectsEscapingPaths(string relativeCandidate)
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "GenHubContainment");
        var candidate = Path.Combine(baseDirectory, relativeCandidate);

        Assert.False(PathHelper.IsPathWithinDirectory(baseDirectory, candidate));
    }

    /// <summary>
    /// Rejects a rooted candidate that resolves outside the base directory.
    /// </summary>
    [Fact]
    public void IsPathWithinDirectory_RejectsAbsolutePathOutsideBase()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "GenHubContainment");
        var candidate = Path.Combine(Path.GetTempPath(), "GenHubElsewhere", "escaped.dat");

        Assert.False(PathHelper.IsPathWithinDirectory(baseDirectory, candidate));
    }
}
