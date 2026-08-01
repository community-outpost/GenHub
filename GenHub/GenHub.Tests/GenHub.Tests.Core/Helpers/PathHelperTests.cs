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
}
