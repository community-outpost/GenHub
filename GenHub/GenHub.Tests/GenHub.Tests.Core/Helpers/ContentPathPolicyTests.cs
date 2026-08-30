using System;
using System.IO;
using GenHub.Core.Helpers;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="ContentPathPolicy"/>.
/// </summary>
public class ContentPathPolicyTests
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "GenHubTests_PathPolicy_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentPathPolicyTests"/> class.
    /// </summary>
    public ContentPathPolicyTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    /// <summary>
    /// Verifies that valid contained relative paths resolve successfully.
    /// </summary>
    [Fact]
    public void ResolveContainedFile_ValidRelativePath_ResolvesCorrectly()
    {
        var result = ContentPathPolicy.ResolveContainedFile(_tempRoot, "sub/file.txt");
        var expected = Path.GetFullPath(Path.Combine(_tempRoot, "sub", "file.txt"));
        Assert.True(result.Success);
        Assert.Equal(expected, result.Data);
    }

    /// <summary>
    /// Verifies that directory traversal sequences return a failure result.
    /// </summary>
    /// <param name="maliciousPath">The traversal path to test.</param>
    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("sub/../../outside.txt")]
    [InlineData("..\\outside.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    [InlineData("\\\\server\\share\\file.txt")]
    public void ResolveContainedFile_PathEscapesRoot_ReturnsFailure(string maliciousPath)
    {
        var result = ContentPathPolicy.ResolveContainedFile(_tempRoot, maliciousPath);
        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies that null or whitespace inputs return a failure result.
    /// </summary>
    /// <param name="invalidPath">The invalid path to test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveContainedFile_NullOrEmptyRelativePath_ReturnsFailure(string? invalidPath)
    {
        var result = ContentPathPolicy.ResolveContainedFile(_tempRoot, invalidPath!);
        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies that <see cref="ContentPathPolicy.IsContained"/> accurately detects containment.
    /// </summary>
    [Fact]
    public void IsContained_ValidAndInvalidPaths_ReturnsExpectedBoolean()
    {
        var inside = Path.Combine(_tempRoot, "nested", "file.dll");
        var outside = Path.Combine(Path.GetTempPath(), "other_dir", "file.dll");

        Assert.True(ContentPathPolicy.IsContained(_tempRoot, inside));
        Assert.False(ContentPathPolicy.IsContained(_tempRoot, outside));
    }
}
