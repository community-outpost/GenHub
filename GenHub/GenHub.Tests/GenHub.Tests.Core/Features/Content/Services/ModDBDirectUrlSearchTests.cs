using System;
using GenHub.Features.Content.Services.ContentDiscoverers;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Unit tests for ModDB direct URL search normalization and detection.
/// </summary>
public sealed class ModDBDirectUrlSearchTests
{
    /// <summary>
    /// Verifies that valid ModDB URLs are normalized into full absolute URLs.
    /// </summary>
    /// <param name="input">The raw URL input.</param>
    /// <param name="expectedUrl">The expected normalized URL.</param>
    [Theory]
    [InlineData("https://www.moddb.com/mods/rise-of-the-reds", "https://www.moddb.com/mods/rise-of-the-reds")]
    [InlineData("http://moddb.com/downloads/contra-009", "http://moddb.com/downloads/contra-009")]
    [InlineData("www.moddb.com/mods/shockwave/addons/maps", "https://www.moddb.com/mods/shockwave/addons/maps")]
    [InlineData("moddb.com/games/cc-generals-zero-hour/downloads", "https://moddb.com/games/cc-generals-zero-hour/downloads")]
    [InlineData("/mods/rise-of-the-reds", "https://www.moddb.com/mods/rise-of-the-reds")]
    [InlineData("/downloads/contra-009", "https://www.moddb.com/downloads/contra-009")]
    [InlineData("/addons/some-addon", "https://www.moddb.com/addons/some-addon")]
    public void TryNormalizeModDBUrl_ValidModDBUrls_ReturnsTrueAndNormalizedUrl(string input, string expectedUrl)
    {
        // Act
        var isValid = ModDBDiscoverer.TryNormalizeModDBUrl(input, out var normalizedUrl);

        // Assert
        Assert.True(isValid);
        Assert.NotNull(normalizedUrl);
        Assert.Equal(expectedUrl, normalizedUrl);
    }

    /// <summary>
    /// Verifies that non-ModDB URLs or text keywords return false from URL normalization.
    /// </summary>
    /// <param name="input">The raw text search term input.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("rise of the reds")]
    [InlineData("shockwave mod")]
    [InlineData("https://google.com")]
    [InlineData("https://github.com/community-outpost/GenHub")]
    [InlineData("http://www.cnclabs.com/downloads/details.aspx?id=1234")]
    public void TryNormalizeModDBUrl_NonModDBUrlsOrKeywords_ReturnsFalse(string? input)
    {
        // Act
        var isValid = ModDBDiscoverer.TryNormalizeModDBUrl(input, out var normalizedUrl);

        // Assert
        Assert.False(isValid);
        Assert.Null(normalizedUrl);
    }

    /// <summary>
    /// Verifies that ExtractModDBIdFromUrl extracts the trailing slug from ModDB URLs.
    /// </summary>
    [Fact]
    public void ExtractModDBIdFromUrl_ExtractsLastSlug()
    {
        // Act & Assert
        Assert.Equal("rise-of-the-reds", ModDBDiscoverer.ExtractModDBIdFromUrl("https://www.moddb.com/mods/rise-of-the-reds"));
        Assert.Equal("shockwave-1201-full", ModDBDiscoverer.ExtractModDBIdFromUrl("https://www.moddb.com/mods/shockwave/downloads/shockwave-1201-full"));
        Assert.Equal("contra-009", ModDBDiscoverer.ExtractModDBIdFromUrl("https://www.moddb.com/downloads/contra-009"));
    }
}
