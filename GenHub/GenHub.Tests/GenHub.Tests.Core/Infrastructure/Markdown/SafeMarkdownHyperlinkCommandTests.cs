using System;
using GenHub.Infrastructure.Markdown;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Markdown;

/// <summary>
/// Unit tests for <see cref="SafeMarkdownHyperlinkCommand"/>.
/// </summary>
public sealed class SafeMarkdownHyperlinkCommandTests
{
    private readonly SafeMarkdownHyperlinkCommand _command = new();

    /// <summary>
    /// Verifies that CanExecute returns true for valid HTTP and HTTPS URLs.
    /// </summary>
    /// <param name="url">The URL string to evaluate.</param>
    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/path?foo=bar#hash")]
    [InlineData("https://github.com/community-outpost/GenHub")]
    public void CanExecute_ValidHttpUrls_ReturnsTrue(string url)
    {
        Assert.True(_command.CanExecute(url));
    }

    /// <summary>
    /// Verifies that CanExecute returns false for dangerous, non-HTTP schemes or invalid URLs.
    /// </summary>
    /// <param name="url">The unsafe or invalid URL string.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("file:///C:/Users/Public/payload.exe")]
    [InlineData("file:///etc/passwd")]
    [InlineData("\\\\192.168.1.1\\share\\payload.bat")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>")]
    [InlineData("powershell:start")]
    [InlineData("relative/path/to/file")]
    public void CanExecute_UnsafeOrInvalidUrls_ReturnsFalse(string? url)
    {
        Assert.False(_command.CanExecute(url));
    }

    /// <summary>
    /// Verifies that Execute does not throw when passed unsafe or null URLs.
    /// </summary>
    /// <param name="url">The unsafe URL string.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("file:///C:/Users/Public/payload.exe")]
    [InlineData("javascript:alert(1)")]
    public void Execute_UnsafeUrls_DoesNotThrow(string? url)
    {
        var exception = Record.Exception(() => _command.Execute(url));
        Assert.Null(exception);
    }
}
