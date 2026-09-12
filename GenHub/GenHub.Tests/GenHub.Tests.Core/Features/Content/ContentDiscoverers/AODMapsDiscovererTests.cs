using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Features.Content.Services.ContentDiscoverers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.ContentDiscoverers;

/// <summary>
/// Regression tests for AODMaps discovery metadata displayed by download cards.
/// </summary>
public sealed class AODMapsDiscovererTests
{
    /// <summary>
    /// Verifies gallery thumbnails resolve relative to their AOD category page and player metadata is retained.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_AoaGalleryMap_UsesCategoryRelativeThumbnailAndPlayerBadgeMetadataAsync()
    {
        var result = await DiscoverAsync(
            CreateGalleryHtml(includeImage: true, downloadId: "4P_1_1"),
            new ContentSearchQuery
            {
                AODMapsCategory = AODMapsConstants.CategoryAoa,
                Take = 10,
            });

        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("https://aodmaps.com/AOA/preview.png", item.IconUrl);
        Assert.Equal(string.Empty, item.Version);
        Assert.Equal("4", item.ResolverMetadata[AODMapsConstants.PlayerCountMetadataKey]);
        Assert.Equal(AODMapsConstants.CategoryAoa, item.ResolverMetadata[AODMapsConstants.CategoryMetadataKey]);
        Assert.Contains("4 Players", item.Tags);
        Assert.Contains(AODMapsConstants.CategoryAoa, item.Tags);
        Assert.Equal("Community Art of Attack map for 4 players.", item.Description);
    }

    /// <summary>
    /// Verifies gallery maps with author and special AI/rule notes produce rich metadata and tags.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_GalleryMapWithAuthorAndNotes_ExtractsAuthorAndRichDescriptionAsync()
    {
        var html = """
            <html><body>
              <div id="gallery"><ul class="nospace clear">
                <li>
                  <a href="https://www.pashacnc.com/ccount/click.php?id=6P_3_29" download>
                    <img src="preview.png" alt="">
                    <span class="name">AOD Pasha Fire Circle [No Laser, EMP] (AI USA) V00 by Pasha</span>
                  </a>
                </li>
              </ul></div>
            </body></html>
            """;

        var result = await DiscoverAsync(
            html,
            new ContentSearchQuery
            {
                Take = 10,
            });

        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("Pasha", item.AuthorName);
        Assert.Contains("author:pasha", item.Tags);
        Assert.Equal("Community Art of Defense map for 6 players by Pasha. Notes: No Laser, EMP, AI USA.", item.Description);
    }

    /// <summary>
    /// Verifies a missing source thumbnail has a usable publisher logo instead of an empty card image.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_MapWithoutThumbnail_UsesAodMapsPublisherLogoAsync()
    {
        var result = await DiscoverAsync(
            CreateGalleryHtml(includeImage: false, downloadId: "4P_1_1"),
            new ContentSearchQuery
            {
                AODMapsCategory = AODMapsConstants.CategoryAoa,
                Take = 10,
            });

        var item = Assert.Single(result.Data!.Items);
        Assert.Equal(PublisherInfoConstants.AODMaps.LogoSource, item.IconUrl);
    }

    /// <summary>
    /// Verifies combined category + player filters use the category page and keep only matching maps.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_CategoryAndPlayerCount_FiltersToMatchingMapsFromCategoryPageAsync()
    {
        var html = """
            <html><body>
              <div id="gallery"><ul class="nospace clear">
                <li>
                  <a href="https://www.pashacnc.com/ccount/click.php?id=2P_1_1" download>
                    <span class="name">Two Player Map</span>
                  </a>
                </li>
                <li>
                  <a href="https://www.pashacnc.com/ccount/click.php?id=4P_2_1" download>
                    <span class="name">Four Player Map</span>
                  </a>
                </li>
              </ul></div>
            </body></html>
            """;

        CapturingHandler? handler = null;
        var result = await DiscoverAsync(
            html,
            new ContentSearchQuery
            {
                AODMapsCategory = AODMapsConstants.CategoryAoa,
                AODMapsPlayerCount = "4 Players",
                Take = 10,
            },
            h => handler = h);

        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("Four Player Map", item.Name);
        Assert.Equal("4", item.ResolverMetadata[AODMapsConstants.PlayerCountMetadataKey]);
        Assert.Equal(AODMapsConstants.CategoryAoa, item.ResolverMetadata[AODMapsConstants.CategoryMetadataKey]);
        Assert.NotNull(handler);
        Assert.Contains(AODMapsConstants.AoaMapsUrl, handler!.RequestedUrls, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(handler.RequestedUrls, url => url.Contains("/Players/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies map maker items extract multi-paragraph descriptions, hints, and author metadata.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_MapMakerItem_ExtractsMultiParagraphDescriptionAndAuthorAsync()
    {
        var html = """
            <html><body>
              <main class="hoc container clear">
                <div class="content">
                  <h1> - [AOD] Phantom Attack V2 fixed lag by SaMPoSa</h1>
                  <p1>- Type: Survival & Hold The Line - Difficultly: Extreme Brutal - Number of Players: 3 Players</p1>
                  <img class="imgl borderedbox inspace-5" src="SaMPoSaPhoto/ilk.jpg" alt="">
                  <p>-No need to restart the map.</p>
                  <p>-Every 6 Waves are Base Attacks.</p>
                  <a href="https://www.aodmaps.com/ccount/click.php?id=3P_8_3" download><span class="name">DOWNLOAD the Map</span></a>
                </div>
              </main>
            </body></html>
            """;

        var result = await DiscoverAsync(
            html,
            new ContentSearchQuery
            {
                Take = 10,
            });

        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("[AOD] Phantom Attack V2 fixed lag by SaMPoSa", item.Name);
        Assert.Equal("SaMPoSa", item.AuthorName);
        Assert.Contains("author:samposa", item.Tags);
        Assert.Contains("Type: Survival & Hold The Line - Difficultly: Extreme Brutal - Number of Players: 3 Players", item.Description);
        Assert.Contains("No need to restart the map", item.Description);
        Assert.Contains("Every 6 Waves are Base Attacks", item.Description);
    }

    /// <summary>
    /// Verifies the Contra filter label resolves to the Contra AOD gallery instead of New Maps.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_ContraCategory_UsesContraAodUrlAsync()
    {
        CapturingHandler? handler = null;
        var result = await DiscoverAsync(
            CreateGalleryHtml(includeImage: false, downloadId: "6P_9_1"),
            new ContentSearchQuery
            {
                AODMapsCategory = AODMapsConstants.CategoryContra,
                Take = 10,
            },
            h => handler = h);

        Assert.True(result.Success);
        Assert.NotNull(handler);
        Assert.Contains(AODMapsConstants.ContraAodUrl, handler!.RequestedUrls, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<GenHub.Core.Models.Results.OperationResult<GenHub.Core.Models.Results.Content.ContentDiscoveryResult>> DiscoverAsync(
        string html,
        ContentSearchQuery query,
        Action<CapturingHandler>? configureHandler = null)
    {
        var handler = new CapturingHandler(html);
        configureHandler?.Invoke(handler);

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient(AODMapsConstants.DiscovererSourceName))
            .Returns(new HttpClient(handler));

        var discoverer = new AODMapsDiscoverer(
            httpClientFactory.Object,
            new Mock<ILogger<AODMapsDiscoverer>>().Object);

        return await discoverer.DiscoverAsync(query);
    }

    private static string CreateGalleryHtml(bool includeImage, string downloadId)
    {
        var image = includeImage ? "<img src=\"preview.png\" alt=\"Map preview\">" : string.Empty;
        return $"""
            <html><body>
              <div id="gallery"><ul class="nospace clear">
                <li>
                  <a href="https://www.pashacnc.com/ccount/click.php?id={downloadId}" download>
                    {image}
                    <span class="name">AOD Test Map</span>
                  </a>
                </li>
              </ul></div>
            </body></html>
            """;
    }

    private sealed class CapturingHandler(string html) : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = [];

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUrls.Add(request.RequestUri?.AbsoluteUri ?? string.Empty);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html),
            });
        }
    }
}
