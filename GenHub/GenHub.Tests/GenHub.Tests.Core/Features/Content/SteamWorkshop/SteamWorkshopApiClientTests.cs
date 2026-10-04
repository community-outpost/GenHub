using GenHub.Core.Constants;
using GenHub.Features.Content.Services.SteamWorkshop;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.SteamWorkshop;

/// <summary>
/// Unit tests for <see cref="SteamWorkshopApiClient"/>.
/// </summary>
public sealed class SteamWorkshopApiClientTests : SteamWorkshopHttpTestBase
{
    private const string DetailsJson = """
        {"response":{"result":1,"resultcount":1,"publishedfiledetails":[
        {"publishedfileid":"3790356853","result":1,"creator":"76561198886953206","title":"Desert Duel","file_description":"Full description","time_created":1704067200,"time_updated":1720000000,"subscriptions":929,"favorited":11,"views":2384,"file_size":"686796","preview_url":"https://example.com/preview.jpg","tags":[{"tag":"Multiplayer"}],"previews":[{"url":"https://example.com/shot.jpg"}]}
        ]}}
        """;

    /// <summary>
    /// Verifies details lookups return the file, including numeric file sizes.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetPublishedFileDetailsAsync_Success_ReturnsFileAsync()
    {
        var sut = CreateSut(_ => SteamWorkshopTestBuilders.JsonResponse(DetailsJson));

        var result = await sut.GetPublishedFileDetailsAsync("3790356853");

        Assert.True(result.Success);
        Assert.Equal("Desert Duel", result.Data!.Title);
        Assert.Equal("Full description", result.Data.FileDescription);
        Assert.Equal(686796, result.Data.FileSize);
    }

    /// <summary>
    /// Verifies missing items surface a failure.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetPublishedFileDetailsAsync_NotFound_ReturnsFailureAsync()
    {
        var sut = CreateSut(_ => SteamWorkshopTestBuilders.JsonResponse("""{"response":{"result":1,"resultcount":0,"publishedfiledetails":[]}}"""));

        var result = await sut.GetPublishedFileDetailsAsync("123");

        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies network errors surface a failure instead of throwing.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetPublishedFileDetailsAsync_NetworkError_ReturnsFailureAsync()
    {
        var sut = CreateSut(_ => throw new IOException("connection reset"));

        var result = await sut.GetPublishedFileDetailsAsync("123");

        Assert.False(result.Success);
        Assert.Contains("connection reset", result.FirstError, StringComparison.Ordinal);
    }

    private SteamWorkshopApiClient CreateSut(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var factory = CreateHttpClientFactory(responder);
        return new SteamWorkshopApiClient(factory, Mock.Of<ILogger<SteamWorkshopApiClient>>());
    }
}
