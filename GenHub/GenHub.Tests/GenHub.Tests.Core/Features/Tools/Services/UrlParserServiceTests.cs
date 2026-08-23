using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Features.Tools.ReplayManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
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
    [InlineData("https://strata.gamereplays.org/zh/match/3489856", ReplaySource.Strata)]
    [InlineData("https://strata.gamereplays.org/gen/match/12345", ReplaySource.Strata)]
    [InlineData("https://gamereplays.org/zh/match/12345", ReplaySource.Strata)]
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
    [InlineData("https://strata.gamereplays.org/zh/match/3489856", true)]
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

    /// <summary>
    /// Verifies that GetDirectDownloadUrlsAsync extracts multiple replays from a Strata match HTML page.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetDirectDownloadUrlsAsync_WithStrataMatchPage_ExtractsAllReplaysAsync()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        const string matchHtml = """
            <html>
                <body>
                    <h2>Match #3489856</h2>
                    <a href="https://matchdata.playgenerals.online/replays/2026/8/23/match_3489856/user_1/match_3489856_user_1_replay.rep">Player 1 Replay</a>
                    <a href="https://matchdata.playgenerals.online/replays/2026/8/23/match_3489856/user_2/match_3489856_user_2_replay.rep">Player 2 Replay</a>
                </body>
            </html>
            """;

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(matchHtml),
            });

        var client = new HttpClient(mockHandler.Object);
        var service = new UrlParserService(client, NullLogger<UrlParserService>.Instance);

        var result = await service.GetDirectDownloadUrlsAsync("https://strata.gamereplays.org/zh/match/3489856");

        Assert.Equal(2, result.Count);
        Assert.Contains("https://matchdata.playgenerals.online/replays/2026/8/23/match_3489856/user_1/match_3489856_user_1_replay.rep", result);
        Assert.Contains("https://matchdata.playgenerals.online/replays/2026/8/23/match_3489856/user_2/match_3489856_user_2_replay.rep", result);
    }
}
