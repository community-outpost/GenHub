using GenHub.Core.Helpers;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="TelemetryUrlHelper"/>.
/// </summary>
public class TelemetryUrlHelperTests
{
    /// <summary>
    /// Verifies that user info, query strings, and fragments are stripped from absolute URLs,
    /// and that non-absolute values are omitted.
    /// </summary>
    /// <param name="url">The URL to scrub.</param>
    /// <param name="expected">The expected scrubbed URL.</param>
    [Theory]
    [InlineData("https://example.com/catalog.json?token=secret&x=1", "https://example.com/catalog.json")]
    [InlineData("https://example.com/definition.json#section", "https://example.com/definition.json")]
    [InlineData("https://example.com/a/b.json?sig=abc#frag", "https://example.com/a/b.json")]
    [InlineData("https://example.com/a/b.json", "https://example.com/a/b.json")]
    [InlineData("https://user:password@example.com/download", "https://example.com/download")]
    [InlineData("https://user:password@example.com/download?token=secret#frag", "https://example.com/download")]
    [InlineData("catalog.json", null)]
    [InlineData("not a url", null)]
    [InlineData("file:///C:/Users/someone/catalogs/catalog.json", null)]
    [InlineData("file:///home/someone/catalogs/catalog.json", null)]
    [InlineData("ftp://example.com/catalog.json", null)]
    [InlineData("http://example.com/catalog.json", "http://example.com/catalog.json")]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void StripSensitiveUrlParts_RemovesCredentialsQueryAndFragment(string? url, string? expected)
    {
        Assert.Equal(expected, TelemetryUrlHelper.StripSensitiveUrlParts(url));
    }
}
