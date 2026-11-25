using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.SteamWorkshop;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.SteamWorkshop;

/// <summary>
/// Unit tests for <see cref="SteamWorkshopResolver"/>.
/// </summary>
public sealed class SteamWorkshopResolverTests : SteamWorkshopHttpTestBase
{
    private const string DetailsHtml = """
        <html><head><title>Steam Workshop::Desert Duel</title></head><body>
        <img id="previewImageMain" class="workshopItemPreviewImageMain" src="https://example.com/main.jpg"/>
        <div class="workshopItemDescription" id="highlightContent">Great skirmish map.</div>
        <div class="friendBlockContent">Mapper<br></div>
        <div class="detailsStatsContainerLeft"><div class="detailsStatLeft">File Size </div></div>
        <div class="detailsStatsContainerRight"><div class="detailsStatRight">1.5 MB</div></div>
        <div class="workshopTags"><a href="#">Skirmish</a><a href="#">Co-op</a></div>
        </body></html>
        """;

    /// <summary>
    /// Verifies null items fail fast.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_NullItem_ReturnsFailureAsync()
    {
        var sut = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await sut.ResolveAsync(null!);

        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies items without an ID fail fast.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_MissingId_ReturnsFailureAsync()
    {
        var sut = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var item = new ContentSearchResult { Id = "something-else", Name = "No ID" };

        var result = await sut.ResolveAsync(item);

        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies anonymous resolution parses the details page into a manifest.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_ParsesDetailsPageAsync()
    {
        var sut = CreateSut(_ => SteamWorkshopTestBuilders.HtmlResponse(DetailsHtml));
        var item = new ContentSearchResult
        {
            Id = "steamworkshop.3790356853",
            Name = "Desert Duel",
            TargetGame = GameType.ZeroHour,
            SourceUrl = SteamWorkshopHelper.BuildFileDetailsUrl("3790356853"),
            ResolverMetadata =
            {
                [SteamWorkshopConstants.PublishedFileIdMetadataKey] = "3790356853",
            },
        };

        var result = await sut.ResolveAsync(item);

        Assert.True(result.Success);
        Assert.Equal("desert-duel", result.Data!.Name);
        Assert.Equal(GameType.ZeroHour, result.Data.TargetGame);
        Assert.Equal(SteamWorkshopConstants.PublisherType, result.Data.Publisher!.PublisherType);
        Assert.Equal("steamworkshop.3790356853", result.Data.OriginalContentId);
        Assert.Equal(SteamWorkshopConstants.PublisherPrefix, result.Data.OriginalProviderName);
    }

    /// <summary>
    /// Verifies foreign parent content IDs never overwrite the canonical workshop ID.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_ForeignParentId_UsesCanonicalContentIdAsync()
    {
        var sut = CreateSut(_ => SteamWorkshopTestBuilders.HtmlResponse(DetailsHtml));
        var item = new ContentSearchResult
        {
            Id = "steamworkshop.3790356853",
            Name = "Desert Duel",
            TargetGame = GameType.ZeroHour,
            SourceUrl = SteamWorkshopHelper.BuildFileDetailsUrl("3790356853"),
            ResolverMetadata =
            {
                [SteamWorkshopConstants.PublishedFileIdMetadataKey] = "3790356853",
                [ContentConstants.ParentContentIdMetadataKey] = "cnclabs.map.1",
            },
        };

        var result = await sut.ResolveAsync(item);

        Assert.True(result.Success);
        Assert.Equal("steamworkshop.3790356853", result.Data!.OriginalContentId);
    }

    /// <summary>
    /// Verifies workshop-shaped parent content IDs are honored.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_WorkshopParentId_HonorsParentAsync()
    {
        var sut = CreateSut(_ => SteamWorkshopTestBuilders.HtmlResponse(DetailsHtml));
        var item = new ContentSearchResult
        {
            Id = "steamworkshop.3790356853",
            Name = "Desert Duel",
            TargetGame = GameType.ZeroHour,
            SourceUrl = SteamWorkshopHelper.BuildFileDetailsUrl("3790356853"),
            ResolverMetadata =
            {
                [SteamWorkshopConstants.PublishedFileIdMetadataKey] = "3790356853",
                [ContentConstants.ParentContentIdMetadataKey] = "steamworkshop.123",
            },
        };

        var result = await sut.ResolveAsync(item);

        Assert.True(result.Success);
        Assert.Equal("steamworkshop.123", result.Data!.OriginalContentId);
    }

    /// <summary>
    /// Verifies anonymous resolution echoes display data back onto the item.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_EchoesDisplayDataAsync()
    {
        var sut = CreateSut(_ => SteamWorkshopTestBuilders.HtmlResponse(DetailsHtml));
        var item = new ContentSearchResult
        {
            Id = "steamworkshop.3790356853",
            Name = "Desert Duel",
            Description = SteamWorkshopConstants.MapDescriptionTemplate,
            TargetGame = GameType.ZeroHour,
            SourceUrl = SteamWorkshopHelper.BuildFileDetailsUrl("3790356853"),
            ResolverMetadata =
            {
                [SteamWorkshopConstants.PublishedFileIdMetadataKey] = "3790356853",
            },
        };

        var result = await sut.ResolveAsync(item);

        Assert.True(result.Success);
        Assert.Equal("Great skirmish map.", item.Description);
        Assert.True(item.DownloadSize > 0);
        Assert.Contains("https://example.com/main.jpg", item.ScreenshotUrls);
    }

    /// <summary>
    /// Verifies Co-op tags classify resolved items as missions.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_CoopTag_ResolvesMissionAsync()
    {
        var sut = CreateSut(_ => SteamWorkshopTestBuilders.HtmlResponse(DetailsHtml));
        var item = new ContentSearchResult
        {
            Id = "steamworkshop.3790356853",
            Name = "Desert duel",
            TargetGame = GameType.ZeroHour,
            ResolverMetadata =
            {
                [SteamWorkshopConstants.PublishedFileIdMetadataKey] = "3790356853",
            },
        };

        var result = await sut.ResolveAsync(item);

        Assert.True(result.Success);
        Assert.Equal(ContentType.Mission, result.Data!.ContentType);
        Assert.Contains("Co-op", item.Tags);
    }

    /// <summary>
    /// Verifies items marked with placeholder description metadata have their description replaced and LastUpdated populated.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_WithPlaceholderMetadata_OverwritesDescriptionAndSetsLastUpdatedAsync()
    {
        const string htmlWithUpdated = """
            <html><head><title>Steam Workshop::Desert Duel</title></head><body>
            <img id="previewImageMain" class="workshopItemPreviewImageMain" src="https://example.com/main.jpg"/>
            <div class="workshopItemDescription" id="highlightContent">Great skirmish map.</div>
            <div class="friendBlockContent">Mapper<br></div>
            <div class="detailsStatsContainerLeft"><div class="detailsStatLeft">File Size </div><div class="detailsStatLeft">Updated </div></div>
            <div class="detailsStatsContainerRight"><div class="detailsStatRight">1.5 MB</div><div class="detailsStatRight">30 Sep, 2026 @ 1:00am</div></div>
            </body></html>
            """;
        var sut = CreateSut(_ => SteamWorkshopTestBuilders.HtmlResponse(htmlWithUpdated));
        var item = new ContentSearchResult
        {
            Id = "steamworkshop.3790356853",
            Name = "Desert Duel",
            Description = "Initial placeholder browse description",
            TargetGame = GameType.ZeroHour,
            SourceUrl = SteamWorkshopHelper.BuildFileDetailsUrl("3790356853"),
            ResolverMetadata =
            {
                [SteamWorkshopConstants.PublishedFileIdMetadataKey] = "3790356853",
                [SteamWorkshopConstants.PlaceholderDescriptionMetadataKey] = "true",
            },
        };

        var result = await sut.ResolveAsync(item);

        Assert.True(result.Success);
        Assert.Equal("Great skirmish map.", item.Description);
        Assert.NotNull(item.LastUpdated);
    }

    private SteamWorkshopResolver CreateSut(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var factory = CreateHttpClientFactory(responder);
        var manifestFactory = new SteamWorkshopManifestFactory(
            () => SteamWorkshopTestBuilders.CreateBuilder(),
            Mock.Of<IProviderDefinitionLoader>(),
            Mock.Of<IFileHashProvider>(),
            Mock.Of<ILogger<SteamWorkshopManifestFactory>>());
        return new SteamWorkshopResolver(factory, manifestFactory, Mock.Of<ILogger<SteamWorkshopResolver>>());
    }
}
