using System;
using System.Security.Cryptography;
using System.Text;
using GenHub.Core.Helpers;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="ModDbHelper"/>.
/// </summary>
public sealed class ModDbHelperTests
{
    /// <summary>
    /// Verifies that ExtractModDbIdFromUrl returns an empty string when the URL is null or whitespace.
    /// </summary>
    /// <param name="url">The URL string.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ExtractModDbIdFromUrl_WhenNullOrWhitespace_ReturnsEmptyString(string? url)
    {
        var result = ModDbHelper.ExtractModDbIdFromUrl(url!);
        Assert.Equal(string.Empty, result);
    }

    /// <summary>
    /// Verifies that ExtractModDbIdFromUrl returns the last path segment from a valid ModDB URL.
    /// </summary>
    /// <param name="url">The URL string.</param>
    /// <param name="expectedId">The expected ModDB ID segment.</param>
    [Theory]
    [InlineData("https://www.moddb.com/mods/shockwave", "shockwave")]
    [InlineData("https://www.moddb.com/downloads/start/12345", "12345")]
    [InlineData("https://www.moddb.com/addons/maps/tournament-island/", "tournament-island")]
    public void ExtractModDbIdFromUrl_WhenValidUrlWithSegments_ReturnsLastSegment(string url, string expectedId)
    {
        var result = ModDbHelper.ExtractModDbIdFromUrl(url);
        Assert.Equal(expectedId, result);
    }

    /// <summary>
    /// Verifies that ExtractModDbIdFromUrl returns a deterministic lowercase SHA256 hex string when given a URL without path segments or an invalid URL format.
    /// </summary>
    [Fact]
    public void ExtractModDbIdFromUrl_WhenUrlHasNoPathSegments_ReturnsDeterministicSha256Digest()
    {
        var url = "https://www.moddb.com/";
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();

        var result = ModDbHelper.ExtractModDbIdFromUrl(url);

        Assert.Equal(expectedHash, result);
    }
}
