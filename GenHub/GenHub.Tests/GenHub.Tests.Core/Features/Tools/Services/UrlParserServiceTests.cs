using System.Net.Http;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Features.Tools.ReplayManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Unit tests for <see cref="UrlParserService"/>.
/// </summary>
public sealed class UrlParserServiceTests
{
    private readonly UrlParserService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="UrlParserServiceTests"/> class.
    /// </summary>
    public UrlParserServiceTests()
    {
        var httpClient = new HttpClient();
        _service = new UrlParserService(httpClient, NullLogger<UrlParserService>.Instance);
    }

    /// <summary>
    /// Verifies source identification for various URL formats.
    /// </summary>
    /// <param name="url">The URL to test.</param>
    /// <param name="expectedSource">The expected identified source.</param>
    [Theory]
    [InlineData("https://50ea2z8yuk.ufs.sh/f/ZlHfBAzftgeLJxG1453BRquaUgnl90MjYIFymdAfOpCs67GN", ReplaySource.UploadThing)]
    [InlineData("https://ufs.sh/f/ZlHfBAzftgeLJxG1453BRquaUgnl90MjYIFymdAfOpCs67GN", ReplaySource.UploadThing)]
    [InlineData("https://utfs.io/f/legacy_uploadthing_key_123", ReplaySource.UploadThing)]
    [InlineData("https://www.playgenerals.online/viewmatch?match=12345", ReplaySource.GeneralsOnline)]
    [InlineData("12345", ReplaySource.GeneralsOnline)]
    [InlineData("https://gentool.net/data/zh/replay.rep", ReplaySource.GenTool)]
    [InlineData("https://example.com/downloads/my_match.rep", ReplaySource.DirectLink)]
    [InlineData("https://example.com/downloads/replays_pack.zip", ReplaySource.DirectLink)]
    [InlineData("https://example.com/invalid/page.html", ReplaySource.Unknown)]
    [InlineData("", ReplaySource.Unknown)]
    [InlineData("   ", ReplaySource.Unknown)]
    public void IdentifySource_ReturnsCorrectSource(string url, ReplaySource expectedSource)
    {
        var result = _service.IdentifySource(url);
        Assert.Equal(expectedSource, result);
    }

    /// <summary>
    /// Verifies that IsValidReplayUrl correctly validates known sources.
    /// </summary>
    /// <param name="url">The URL to test.</param>
    /// <param name="expectedValid">Whether the URL is expected to be valid.</param>
    [Theory]
    [InlineData("https://50ea2z8yuk.ufs.sh/f/key123", true)]
    [InlineData("https://utfs.io/f/key123", true)]
    [InlineData("https://example.com/replay.rep", true)]
    [InlineData("https://example.com/page.html", false)]
    public void IsValidReplayUrl_ReturnsExpectedValidity(string url, bool expectedValid)
    {
        var result = _service.IsValidReplayUrl(url);
        Assert.Equal(expectedValid, result);
    }

    /// <summary>
    /// Verifies that GetDirectDownloadUrlAsync directly returns UploadThing URLs.
    /// </summary>
    /// <param name="url">The UploadThing URL.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Theory]
    [InlineData("https://50ea2z8yuk.ufs.sh/f/ZlHfBAzftgeLJxG1453BRquaUgnl90MjYIFymdAfOpCs67GN")]
    [InlineData("https://utfs.io/f/legacy_uploadthing_key_123")]
    public async Task GetDirectDownloadUrlAsync_WithUploadThingUrl_ReturnsOriginalUrlAsync(string url)
    {
        var result = await _service.GetDirectDownloadUrlAsync(url);
        Assert.Equal(url, result);
    }
}
