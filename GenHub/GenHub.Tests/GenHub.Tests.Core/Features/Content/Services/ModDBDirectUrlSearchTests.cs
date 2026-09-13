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

    /// <summary>
    /// Verifies that IsDetailPage correctly identifies single mod, file download, and addon URLs.
    /// </summary>
    /// <param name="url">The URL to evaluate.</param>
    /// <param name="expected">Expected detail page determination.</param>
    [Theory]
    [InlineData("https://www.moddb.com/mods/rise-of-the-reds", true)]
    [InlineData("https://www.moddb.com/mods/cc-shockwave", true)]
    [InlineData("https://www.moddb.com/downloads/contra-009", true)]
    [InlineData("https://www.moddb.com/addons/lemuria-2026-fixes", true)]
    [InlineData("https://www.moddb.com/mods/rise-of-the-reds/downloads/rotr-patch-186-release", true)]
    [InlineData("https://www.moddb.com/mods/shockwave/addons/some-map", true)]
    [InlineData("https://www.moddb.com/games/cc-generals-zero-hour/downloads/genbigedit", true)]
    [InlineData("https://www.moddb.com/games/cc-generals-zero-hour/addons/custom-map", true)]
    [InlineData("https://www.moddb.com/games/cc-generals-zero-hour/mods/shockwave", true)]
    [InlineData("https://www.moddb.com/mods/rise-of-the-reds/downloads", false)]
    [InlineData("https://www.moddb.com/mods/rise-of-the-reds/addons", false)]
    [InlineData("https://www.moddb.com/games/cc-generals-zero-hour/downloads", false)]
    [InlineData("https://www.moddb.com/games/cc-generals-zero-hour/mods", false)]
    [InlineData("https://www.moddb.com/games/cc-generals-zero-hour/addons", false)]
    [InlineData("https://www.moddb.com/mods/cc-shockwave/news/zero-hour-community-anniversary", false)]
    [InlineData("https://www.moddb.com/mods/rise-of-the-reds/articles/june-update", false)]
    [InlineData("https://www.moddb.com/mods/cc-shockwave/tutorials/ai-tutorial", false)]
    [InlineData("https://www.moddb.com/mods/cc-shockwave/videos/trailer", false)]
    [InlineData("https://www.moddb.com/mods/cc-shockwave/images/screenshot", false)]
    public void IsDetailPage_ClassifiesDetailVersusListingAndNonContentUrls(string url, bool expected)
    {
        // Act
        var result = ModDBDiscoverer.IsDetailPage(url);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that IsNonContentUrl detects article, news, tutorial, media, and profile subpaths.
    /// </summary>
    /// <param name="url">The URL or relative path to check.</param>
    /// <param name="expected">Expected non-content determination.</param>
    [Theory]
    [InlineData("https://www.moddb.com/mods/cc-shockwave/news/zero-hour-community-anniversary", true)]
    [InlineData("/mods/rise-of-the-reds/news/june-update-time-to-spread-the-word", true)]
    [InlineData("/mods/rise-of-the-reds/articles/june-update", true)]
    [InlineData("/mods/cc-shockwave/tutorials/ai-tutorial", true)]
    [InlineData("/mods/cc-shockwave/videos/some-video", true)]
    [InlineData("/mods/cc-shockwave/images/some-image", true)]
    [InlineData("/members/swr-productions", true)]
    [InlineData("/company/swr-productions", true)]
    [InlineData("https://www.moddb.com/mods/rise-of-the-reds", false)]
    [InlineData("https://www.moddb.com/downloads/contra-009", false)]
    [InlineData("/mods/rise-of-the-reds/downloads/rotr-patch-186-release", false)]
    public void IsNonContentUrl_DetectsNonDownloadablePaths(string url, bool expected)
    {
        // Act
        var result = ModDBDiscoverer.IsNonContentUrl(url);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that IsValidContentDetailUrl excludes articles, news, and media while allowing valid mod/download items.
    /// </summary>
    /// <param name="href">The relative or absolute link href.</param>
    /// <param name="expected">Expected validity.</param>
    [Theory]
    [InlineData("/mods/cc-shockwave/news/zero-hour-community-anniversary", false)]
    [InlineData("/mods/rise-of-the-reds/news/june-update-time-to-spread-the-word", false)]
    [InlineData("/mods/rise-of-the-reds/articles", false)]
    [InlineData("/mods/cc-shockwave/tutorials/ai-tutorial", false)]
    [InlineData("/mods/cc-shockwave/videos/some-video", false)]
    [InlineData("/mods/cc-shockwave/images/some-image", false)]
    [InlineData("/members/swr-productions", false)]
    [InlineData("/company/swr-productions", false)]
    [InlineData("/mods/rise-of-the-reds", true)]
    [InlineData("/mods/cc-shockwave", true)]
    [InlineData("/mods/rise-of-the-reds/downloads/rotr-patch-186-release", true)]
    [InlineData("/downloads/contra-009", true)]
    [InlineData("/addons/some-map", true)]
    public void IsValidContentDetailUrl_FiltersOutNonContentLinks(string href, bool expected)
    {
        // Act
        var result = ModDBDiscoverer.IsValidContentDetailUrl(href);

        // Assert
        Assert.Equal(expected, result);
    }
}
