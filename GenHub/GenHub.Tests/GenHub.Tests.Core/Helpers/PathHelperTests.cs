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

    /// <summary>
    /// Rejects a candidate that reads as contained but leaves the base directory through a symbolic
    /// link, which textual normalization alone cannot see. GenHub builds symlinked workspaces, so a
    /// link inside a directory being written to is an ordinary shape rather than a contrived one.
    /// </summary>
    [Fact]
    public void IsPathWithinDirectory_RejectsCandidateLeavingThroughASymbolicLink()
    {
        var root = CreateWorkingDirectory();

        try
        {
            var baseDirectory = Path.Combine(root, "extract");
            var outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(baseDirectory);
            Directory.CreateDirectory(outside);

            if (!TryCreateDirectorySymbolicLink(Path.Combine(baseDirectory, "link"), outside))
            {
                return;
            }

            var candidate = Path.Combine(baseDirectory, "link", "escaped.dat");

            Assert.False(PathHelper.IsPathWithinDirectory(baseDirectory, candidate));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Rejects a candidate that leaves the base directory through an intermediate symbolic link
    /// when the target file on the outside destination already exists on disk.
    /// </summary>
    [Fact]
    public void IsPathWithinDirectory_RejectsCandidateLeavingThroughASymbolicLink_WhenOutsideTargetFileExists()
    {
        var root = CreateWorkingDirectory();

        try
        {
            var baseDirectory = Path.Combine(root, "extract");
            var outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(baseDirectory);
            Directory.CreateDirectory(outside);

            var outsideFile = Path.Combine(outside, "installer.exe");
            File.WriteAllText(outsideFile, "payload");

            if (!TryCreateDirectorySymbolicLink(Path.Combine(baseDirectory, "link"), outside))
            {
                return;
            }

            var candidate = Path.Combine(baseDirectory, "link", "installer.exe");

            Assert.False(PathHelper.IsPathWithinDirectory(baseDirectory, candidate));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Accepts a candidate beneath a symbolic link that stays inside the base directory, so
    /// following links tightens the check without refusing content a link merely reorganizes.
    /// </summary>
    [Fact]
    public void IsPathWithinDirectory_AcceptsCandidateBehindASymbolicLinkThatStaysInside()
    {
        var root = CreateWorkingDirectory();

        try
        {
            var baseDirectory = Path.Combine(root, "extract");
            var inside = Path.Combine(baseDirectory, "real");
            Directory.CreateDirectory(inside);

            if (!TryCreateDirectorySymbolicLink(Path.Combine(baseDirectory, "link"), inside))
            {
                return;
            }

            var candidate = Path.Combine(baseDirectory, "link", "contained.dat");

            Assert.True(PathHelper.IsPathWithinDirectory(baseDirectory, candidate));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Rejects a candidate that is a direct file symbolic link pointing to a file outside the base directory.
    /// </summary>
    [Fact]
    public void IsPathWithinDirectory_RejectsCandidateThatIsDirectFileSymbolicLink_PointingOutside()
    {
        var root = CreateWorkingDirectory();

        try
        {
            var baseDirectory = Path.Combine(root, "extract");
            var outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(baseDirectory);
            Directory.CreateDirectory(outside);

            var outsideFile = Path.Combine(outside, "secret.dat");
            File.WriteAllText(outsideFile, "secret");

            var linkFile = Path.Combine(baseDirectory, "link_file.dat");
            if (!TryCreateFileSymbolicLink(linkFile, outsideFile))
            {
                return;
            }

            Assert.False(PathHelper.IsPathWithinDirectory(baseDirectory, linkFile));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that NormalizeRelativePath standardizes path separators.
    /// </summary>
    [Fact]
    public void NormalizeRelativePath_StandardizesSeparators()
    {
        var input = @"folder\subfolder/file.exe";
        var normalized = PathHelper.NormalizeRelativePath(input);

        var expected = Path.Combine("folder", "subfolder", "file.exe");
        Assert.Equal(expected, normalized);
    }

    private static string CreateWorkingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "GenHubContainmentLinks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        return root;
    }

    private static bool TryCreateDirectorySymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryCreateFileSymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
