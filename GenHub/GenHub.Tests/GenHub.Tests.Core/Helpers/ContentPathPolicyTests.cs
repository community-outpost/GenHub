using System;
using System.IO;
using GenHub.Core.Helpers;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="ContentPathPolicy"/>.
/// </summary>
public sealed class ContentPathPolicyTests : IDisposable
{
    private readonly string _tempRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentPathPolicyTests"/> class.
    /// </summary>
    public ContentPathPolicyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "GenHub_ContentPathPolicyTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch
            {
                // Best effort cleanup in tests.
            }
        }
    }

    /// <summary>
    /// Verifies that valid contained relative paths resolve inside root directory.
    /// </summary>
    [Fact]
    public void ResolveContainedFile_ValidRelativePath_ReturnsFullPath()
    {
        var result = ContentPathPolicy.ResolveContainedFile(_tempRoot, "file.txt");
        var expected = Path.GetFullPath(Path.Combine(_tempRoot, "file.txt"));
        Assert.True(result.Success);
        Assert.Equal(expected, result.Data);
    }

    /// <summary>
    /// Verifies that nested relative paths resolve inside root directory.
    /// </summary>
    [Fact]
    public void ResolveContainedFile_NestedRelativePath_ReturnsFullPath()
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
    [InlineData(".")]
    [InlineData("sub/..")]
    [InlineData("../outside.txt")]
    [InlineData("sub/../../outside.txt")]
    [InlineData(@"..\outside.txt")]
    [InlineData("/etc/passwd")]
    [InlineData(@"\foo")]
    [InlineData(@"\foo\bar.txt")]
    [InlineData("/foo/bar.txt")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"\\server\share\file.txt")]
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
        var result = ContentPathPolicy.ResolveContainedFile(_tempRoot, invalidPath);
        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies that paths with invalid format characters return a failure result instead of throwing.
    /// </summary>
    [Fact]
    public void ResolveContainedFile_PathWithInvalidCharacters_ReturnsFailure()
    {
        var result = ContentPathPolicy.ResolveContainedFile(_tempRoot, "invalid\0file.txt");
        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies that IsContained accurately checks containment.
    /// </summary>
    [Fact]
    public void IsContained_ValidatesCorrectly()
    {
        var validChild = Path.Combine(_tempRoot, "sub", "file.txt");
        var escapingChild = Path.Combine(_tempRoot, "..", "escaped.txt");

        Assert.True(ContentPathPolicy.IsContained(_tempRoot, validChild));
        Assert.False(ContentPathPolicy.IsContained(_tempRoot, escapingChild));
        Assert.False(ContentPathPolicy.IsContained(null, validChild));
        Assert.False(ContentPathPolicy.IsContained(_tempRoot, null));
    }
}
