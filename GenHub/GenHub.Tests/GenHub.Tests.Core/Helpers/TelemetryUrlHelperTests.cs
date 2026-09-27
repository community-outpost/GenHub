using GenHub.Core.Helpers;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="TelemetryUrlHelper"/>.
/// </summary>
public class TelemetryUrlHelperTests
{
    /// <summary>
    /// Verifies that query strings and fragments are stripped from absolute URLs.
    /// </summary>
    /// <param name="url">The URL to scrub.</param>
    /// <param name="expected">The expected scrubbed URL.</param>
    [Theory]
    [InlineData("https://example.com/catalog.json?token=secret&x=1", "https://example.com/catalog.json")]
    [InlineData("https://example.com/definition.json#section", "https://example.com/definition.json")]
    [InlineData("https://example.com/a/b.json?sig=abc#frag", "https://example.com/a/b.json")]
    [InlineData("https://example.com/a/b.json", "https://example.com/a/b.json")]
    [InlineData("catalog.json", "catalog.json")]
    [InlineData("not a url", "not a url")]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void StripSensitiveUrlParts_RemovesQueryAndFragment(string? url, string? expected)
    {
        Assert.Equal(expected, TelemetryUrlHelper.StripSensitiveUrlParts(url));
    }
}
