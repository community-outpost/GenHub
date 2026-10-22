using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.SteamWorkshop;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.SteamWorkshop;

/// <summary>
/// Unit tests for <see cref="SteamWorkshopDiscoverer"/>.
/// </summary>
public sealed class SteamWorkshopDiscovererTests : SteamWorkshopHttpTestBase
{
    private const string BrowseHtml = """
        <div>552 entries matching filters</div>
        <div><a href="https://steamcommunity.com/sharedfiles/filedetails/?id=3790356853" class="t"><img src="https://example.com/preview.jpg" alt="Desert Duel"/></a></div><div><a href="https://steamcommunity.com/sharedfiles/filedetails/?id=3790356853">Desert Duel</a></div><div><a href="https://steamcommunity.com/profiles/1/myworkshopfiles/?appid=2732960">By Mapper</a></div>
        """;

    /// <summary>
    /// Verifies null queries fail fast.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_NullQuery_ReturnsFailureAsync()
    {
        var sut = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await sut.DiscoverAsync(null!);

        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies anonymous discovery parses browse pages.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_ParsesBrowsePageAsync()
    {
        var sut = CreateSut(_ => SteamWorkshopTestBuilders.HtmlResponse(BrowseHtml));

        var result = await sut.DiscoverAsync(new ContentSearchQuery { TargetGame = GameType.ZeroHour, Page = 1 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("steamworkshop.3790356853", item.Id);
        Assert.Equal("Desert Duel", item.Name);
        Assert.Equal("Mapper", item.AuthorName);
        Assert.Equal(GameType.ZeroHour, item.TargetGame);
        Assert.Equal(ContentType.Map, item.ContentType);
        Assert.True(item.RequiresResolution);
        Assert.Equal(SteamWorkshopConstants.ResolverId, item.ResolverId);
        Assert.True(result.Data.HasMoreItems);
        Assert.Equal(552, result.Data.TotalItems);
        Assert.Equal(SteamWorkshopConstants.MapDescriptionTemplate, item.Description);
    }

    /// <summary>
    /// Verifies browse placeholders use the localized description when available.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_UsesLocalizedDescriptionAsync()
    {
        var mockLocalization = new Mock<ILocalizationService>();
        mockLocalization.Setup(l => l.GetString("Downloads.SteamWorkshop.Description.PendingResolution", It.IsAny<object?[]>()))
            .Returns("localized-pending-description");
        var sut = CreateSut(_ => SteamWorkshopTestBuilders.HtmlResponse(BrowseHtml), mockLocalization.Object);

        var result = await sut.DiscoverAsync(new ContentSearchQuery { TargetGame = GameType.ZeroHour, Page = 1 });

        Assert.True(result.Success);
        Assert.Equal("localized-pending-description", Assert.Single(result.Data!.Items).Description);
    }

    /// <summary>
    /// Verifies required tag filters are sent to the browse page.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithRequiredTags_SendsTagFiltersAsync()
    {
        var requests = new List<HttpRequestMessage>();
        var sut = CreateSut(request =>
        {
            requests.Add(request);
            return SteamWorkshopTestBuilders.HtmlResponse(BrowseHtml);
        });
        var query = new ContentSearchQuery { TargetGame = GameType.ZeroHour, Page = 1 };
        query.SteamWorkshopRequiredTags.Add("1v1");

        var result = await sut.DiscoverAsync(query);

        Assert.True(result.Success);
        var browseRequest = Assert.Single(requests, r => !r.RequestUri!.Query.Contains("xml=1", StringComparison.Ordinal));
        Assert.Contains("requiredtags[]=1v1", browseRequest.RequestUri!.Query, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies browse discovery populates creator avatars from embedded React data.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithEmbeddedAvatars_PopulatesCreatorAvatarAsync()
    {
        const string htmlWithEmbedded = """
            <div>552 entries matching filters</div>
            <script>
            {"publishedfileid":"3790356853","creator":"76561198886953206"}
            {"steamid":"76561198886953206","sha_digest_avatar":{"_t":0,"v":[81,55,7,252,234,117,113,228,14,232,1,101,202,242,150,249,236,160,10,49]}}
            </script>
            <div><a href="https://steamcommunity.com/sharedfiles/filedetails/?id=3790356853"><img src="https://example.com/preview.jpg" alt="Desert Duel"/></a></div><div><a href="https://steamcommunity.com/sharedfiles/filedetails/?id=3790356853">Desert Duel</a></div><div><a href="https://steamcommunity.com/profiles/76561198886953206/myworkshopfiles/?appid=2732960">By SUPZH</a></div>
            """;

        var sut = CreateSut(_ => SteamWorkshopTestBuilders.HtmlResponse(htmlWithEmbedded));

        var result = await sut.DiscoverAsync(new ContentSearchQuery { TargetGame = GameType.ZeroHour, Page = 1 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("https://avatars.fastly.steamstatic.com/513707fcea7571e40ee80165caf296f9eca00a31_full.jpg", item.Metadata[SteamWorkshopConstants.CreatorAvatarUrlMetadataKey]);
        Assert.Equal("76561198886953206", item.ResolverMetadata[SteamWorkshopConstants.CreatorIdMetadataKey]);
    }

    /// <summary>
    /// Verifies browse discovery falls back to public profile XML when embedded avatar is missing.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithProfileXmlFallback_PopulatesCreatorAvatarAsync()
    {
        const string xmlResponse = """
            <profile>
                <steamID><![CDATA[Mapper]]></steamID>
                <avatarFull><![CDATA[https://avatars.fastly.steamstatic.com/xml_avatar_full.jpg]]></avatarFull>
            </profile>
            """;

        var sut = CreateSut(request => request.RequestUri!.Query.Contains("xml=1", StringComparison.Ordinal)
            ? SteamWorkshopTestBuilders.HtmlResponse(xmlResponse)
            : SteamWorkshopTestBuilders.HtmlResponse(BrowseHtml));

        var result = await sut.DiscoverAsync(new ContentSearchQuery { TargetGame = GameType.ZeroHour, Page = 1 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("https://avatars.fastly.steamstatic.com/xml_avatar_full.jpg", item.Metadata[SteamWorkshopConstants.CreatorAvatarUrlMetadataKey]);
    }

    /// <summary>
    /// Verifies browse discovery succeeds even if a profile XML request times out.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_ProfileXmlTimeout_DoesNotFailDiscoveryAsync()
    {
        var sut = CreateSut(request =>
        {
            if (request.RequestUri!.Query.Contains("xml=1", StringComparison.Ordinal))
            {
                throw new TaskCanceledException("The operation was canceled.");
            }

            return SteamWorkshopTestBuilders.HtmlResponse(BrowseHtml);
        });

        var result = await sut.DiscoverAsync(new ContentSearchQuery { TargetGame = GameType.ZeroHour, Page = 1 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.False(item.Metadata.ContainsKey(SteamWorkshopConstants.CreatorAvatarUrlMetadataKey));
    }

    /// <summary>
    /// Verifies pagination fetches multiple browse pages when query.Page is not set and needed items exceed page size.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithoutPage_FetchesMultipleBrowsePagesForSkipAndTakeAsync()
    {
        var fetchedPages = new List<string>();
        var sut = CreateSut(request =>
        {
            if (request.RequestUri?.Query.Contains("xml=1", StringComparison.Ordinal) == false)
            {
                fetchedPages.Add(request.RequestUri.Query);
            }

            return SteamWorkshopTestBuilders.HtmlResponse(BrowseHtml);
        });

        var query = new ContentSearchQuery { TargetGame = GameType.ZeroHour, Skip = 30, Take = 30 };
        var result = await sut.DiscoverAsync(query);

        Assert.True(result.Success);
        Assert.True(fetchedPages.Count >= 2);
        Assert.Contains(fetchedPages, q => q.Contains("p=1", StringComparison.Ordinal));
        Assert.Contains(fetchedPages, q => q.Contains("p=2", StringComparison.Ordinal));
    }

    private SteamWorkshopDiscoverer CreateSut(Func<HttpRequestMessage, HttpResponseMessage> responder, ILocalizationService? localizationService = null)
    {
        var factory = CreateHttpClientFactory(responder);
        return new SteamWorkshopDiscoverer(factory, Mock.Of<ILogger<SteamWorkshopDiscoverer>>(), localizationService);
    }
}
