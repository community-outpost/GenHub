using GenHub.Core.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="BrowserHelper"/>.
/// </summary>
public class BrowserHelperTests
{
    /// <summary>
    /// Verifies that null logger throws <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public void TryOpenUrl_NullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => BrowserHelper.TryOpenUrl("https://example.com", null!));
    }

    /// <summary>
    /// Verifies that null, empty, or whitespace URLs return false.
    /// </summary>
    /// <param name="url">The URL under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryOpenUrl_NullOrWhitespace_ReturnsFalse(string? url)
    {
        var result = BrowserHelper.TryOpenUrl(url, NullLogger.Instance);
        Assert.False(result);
    }

    /// <summary>
    /// Verifies that non-absolute or malformed URLs return false.
    /// </summary>
    /// <param name="url">The relative or invalid URL.</param>
    [Theory]
    [InlineData("not-a-url")]
    [InlineData("/relative/path")]
    [InlineData("../parent/path")]
    public void TryOpenUrl_RelativeOrMalformed_ReturnsFalse(string url)
    {
        var result = BrowserHelper.TryOpenUrl(url, NullLogger.Instance);
        Assert.False(result);
    }

    /// <summary>
    /// Verifies that non-HTTP/HTTPS schemes (e.g. file, javascript, ftp) are rejected.
    /// </summary>
    /// <param name="url">The URL with an unsafe scheme.</param>
    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/file.txt")]
    [InlineData("data:text/html,test")]
    [InlineData("powershell:test")]
    [InlineData("mailto:user@example.com")]
    public void TryOpenUrl_UnsafeScheme_ReturnsFalse(string url)
    {
        var result = BrowserHelper.TryOpenUrl(url, NullLogger.Instance);
        Assert.False(result);
    }
}
