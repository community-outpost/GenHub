using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services;
using GenHub.Features.Content.Services.ContentDiscoverers;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.ContentDiscoverers;

/// <summary>
/// Regression tests for the offline downloaded-content library discoverer.
/// </summary>
public sealed class DownloadedContentDiscovererTests
{
    /// <summary>
    /// Verifies that a content-type query returns only manifests of that type.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_WithContentTypeFilter_ReturnsOnlyMatchingManifestsAsync()
    {
        var discoverer = CreateDiscoverer(
        [
            CreateManifest("1.20260101.test.mod.alpha", "Alpha Mod", ContentType.Mod, GameType.ZeroHour),
            CreateManifest("1.20260102.test.map.bravo", "Bravo Map", ContentType.Map, GameType.ZeroHour),
        ]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { ContentType = ContentType.Map, Take = 10 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("Bravo Map", item.Name);
        Assert.Equal(1, result.Data.TotalItems);
    }

    /// <summary>
    /// Verifies that a target-game query returns only manifests for that game.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_WithGameFilter_ReturnsOnlyMatchingGameAsync()
    {
        var discoverer = CreateDiscoverer(
        [
            CreateManifest("1.20260101.test.mod.alpha", "Alpha Mod", ContentType.Mod, GameType.Generals),
            CreateManifest("1.20260102.test.mod.bravo", "Bravo Mod", ContentType.Mod, GameType.ZeroHour),
        ]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { TargetGame = GameType.ZeroHour, Take = 10 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("Bravo Mod", item.Name);
    }

    /// <summary>
    /// Verifies that a search term matches against manifest names and IDs.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_WithSearchTerm_MatchesNameOrIdAsync()
    {
        var discoverer = CreateDiscoverer(
        [
            CreateManifest("1.20260101.test.mod.alpha", "Alpha Mod", ContentType.Mod, GameType.ZeroHour),
            CreateManifest("1.20260102.test.mod.bravo", "Bravo Mod", ContentType.Mod, GameType.ZeroHour),
        ]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { SearchTerm = "brav", Take = 10 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("Bravo Mod", item.Name);
    }

    /// <summary>
    /// Verifies that pagination returns the requested page in alphabetical order with correct totals.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_WithPagination_ReturnsRequestedPageAsync()
    {
        var discoverer = CreateDiscoverer(
        [
            CreateManifest("1.20260101.test.mod.charlie", "Charlie", ContentType.Mod, GameType.ZeroHour),
            CreateManifest("1.20260102.test.mod.alpha", "Alpha", ContentType.Mod, GameType.ZeroHour),
            CreateManifest("1.20260103.test.mod.bravo", "Bravo", ContentType.Mod, GameType.ZeroHour),
        ]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 1, Page = 2 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("Bravo", item.Name);
        Assert.True(result.Data.HasMoreItems);
        Assert.Equal(3, result.Data.TotalItems);
    }

    /// <summary>
    /// Verifies that manifest metadata is projected onto the search result for card rendering.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_MapsManifestMetadata_ToSearchResultAsync()
    {
        var manifest = CreateManifest("1.20260101.test.mod.alpha", "Alpha Mod", ContentType.Mod, GameType.ZeroHour);
        manifest.Version = "2.1";
        manifest.Metadata.Description = "A test mod.";
        manifest.Metadata.Tags.Add("classic");
        manifest.Metadata.ScreenshotUrls.Add("https://example.com/shot.png");
        manifest.Files.Add(new ManifestFile { RelativePath = "alpha.big", Size = 1024 });
        manifest.Publisher = new PublisherInfo { Name = "Test Author", PublisherType = "moddb" };

        var discoverer = CreateDiscoverer([manifest]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 10 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("1.20260101.test.mod.alpha", item.Id);
        Assert.Equal("2.1", item.Version);
        Assert.Equal("A test mod.", item.Description);
        Assert.Equal("Test Author", item.AuthorName);
        Assert.Equal(1024, item.DownloadSize);
        Assert.Contains("classic", item.Tags);
        Assert.Contains("https://example.com/shot.png", item.ScreenshotUrls);
        Assert.False(item.RequiresResolution);
        Assert.False(result.Data.HasMoreItems);
    }

    /// <summary>
    /// Verifies that launcher-managed installation manifests are excluded from the library.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_ExcludesGameInstallationManifestsAsync()
    {
        var discoverer = CreateDiscoverer(
        [
            CreateManifest("1.20260101.steam.gameinstallation.zerohour", "Steam Zero Hour", ContentType.GameInstallation, GameType.ZeroHour),
            CreateManifest("1.20260102.test.mod.bravo", "Bravo Mod", ContentType.Mod, GameType.ZeroHour),
        ]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 10 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("Bravo Mod", item.Name);
    }

    /// <summary>
    /// Verifies that persisted local artwork wins over remote manifest URLs.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_PrefersLocalArtworkOverRemoteUrlsAsync()
    {
        var manifest = CreateManifest("1.20260101.test.mod.alpha", "Alpha Mod", ContentType.Mod, GameType.ZeroHour);
        manifest.Metadata.IconUrl = "https://example.com/icon.png";
        manifest.Metadata.CoverUrl = "https://example.com/cover.png";

        var artworkService = CreateArtworkService();
        artworkService
            .Setup(service => service.GetLocalArtworkPath(manifest.Id.Value, ContentArtworkKind.Icon))
            .Returns("/artwork/alpha/icon.png");
        artworkService
            .Setup(service => service.GetLocalArtworkPath(manifest.Id.Value, ContentArtworkKind.Cover))
            .Returns("/artwork/alpha/cover.png");

        var discoverer = CreateDiscoverer([manifest], artworkService);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 10 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("/artwork/alpha/icon.png", item.IconUrl);
        Assert.Equal("/artwork/alpha/cover.png", item.BannerUrl);
    }

    /// <summary>
    /// Verifies that iconless publisher-downloaded game clients fall back to bundled per-game covers.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_UsesGameCoverFallbackForIconlessClientsAsync()
    {
        var discoverer = CreateDiscoverer(
        [
            CreateManifest("1.20260101.generalsonline.gameclient.zerohour", "Zero Hour", ContentType.GameClient, GameType.ZeroHour),
        ]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 10 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal(ContentArtworkConstants.ZeroHourCoverSource, item.BannerUrl);
    }

    /// <summary>
    /// Verifies that locally detected game clients are excluded while publisher-downloaded
    /// clients remain listed.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_ExcludesLocallyDetectedGameClientsAsync()
    {
        var retailClient = CreateManifest("1.20260102.custom.gameclient.generals", "Retail Generals", ContentType.GameClient, GameType.Generals);
        retailClient.Publisher = new PublisherInfo { Name = "Retail", PublisherType = PublisherTypeConstants.Retail };
        var downloadedClient = CreateManifest("1.20260103.generalsonline.gameclient.zerohour", "GO Zero Hour", ContentType.GameClient, GameType.ZeroHour);
        downloadedClient.Publisher = new PublisherInfo { Name = "Generals Online", PublisherType = PublisherTypeConstants.GeneralsOnline };

        var discoverer = CreateDiscoverer(
        [
            CreateManifest("1.20260101.steam.gameclient.zerohour", "Steam Zero Hour", ContentType.GameClient, GameType.ZeroHour),
            retailClient,
            downloadedClient,
        ]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 10 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("GO Zero Hour", item.Name);
        Assert.Equal(1, result.Data.TotalItems);
    }

    /// <summary>
    /// Verifies that the flat GitHub publisher id resolves to the GitHub logo fallback.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_ResolvesFlatGitHubPublisherLogoAsync()
    {
        var manifest = CreateManifest("1.20260101.github.mod.alpha", "Alpha Mod", ContentType.Mod, GameType.ZeroHour);
        manifest.Publisher = new PublisherInfo { Name = "GitHub", PublisherType = PublisherTypeConstants.GitHub };

        var discoverer = CreateDiscoverer([manifest]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 10 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal(PublisherInfoConstants.GitHub.LogoSource, item.IconUrl);
    }

    /// <summary>
    /// Verifies that background artwork prefetch receives the caller's cancellation token.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_PassesCallerTokenToArtworkPrefetchAsync()
    {
        var manifest = CreateManifest("1.20260101.test.mod.alpha", "Alpha Mod", ContentType.Mod, GameType.ZeroHour);
        manifest.Metadata.IconUrl = "https://example.com/icon.png";

        var capturedToken = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var artworkService = CreateArtworkService();
        artworkService
            .Setup(service => service.PrefetchArtworkAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .Callback<ContentManifest, CancellationToken>((_, token) => capturedToken.TrySetResult(token))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var discoverer = CreateDiscoverer([manifest], artworkService);
        using var cts = new CancellationTokenSource();

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 10 }, cts.Token);

        Assert.True(result.Success);
        var completed = await Task.WhenAny(capturedToken.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(completed == capturedToken.Task, "Artwork prefetch was not invoked.");
        Assert.Equal(cts.Token, await capturedToken.Task);
    }

    /// <summary>
    /// Verifies that a cancelled query token suppresses the background artwork prefetch.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_WithCancelledToken_SkipsArtworkPrefetchAsync()
    {
        var manifest = CreateManifest("1.20260101.test.mod.alpha", "Alpha Mod", ContentType.Mod, GameType.ZeroHour);
        manifest.Metadata.IconUrl = "https://example.com/icon.png";

        var artworkService = CreateArtworkService();
        var discoverer = CreateDiscoverer([manifest], artworkService);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 10 }, cts.Token);

        Assert.True(result.Success);
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        artworkService.Verify(
            service => service.PrefetchArtworkAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that iconless content from known publishers falls back to the publisher logo.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_UsesPublisherLogoFallbackForIconlessContentAsync()
    {
        var manifest = CreateManifest("1.20260101.test.mod.alpha", "Alpha Mod", ContentType.Mod, GameType.ZeroHour);
        manifest.Publisher = new PublisherInfo { Name = "GO", PublisherType = PublisherTypeConstants.GeneralsOnline };

        var discoverer = CreateDiscoverer([manifest]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 10 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal(PublisherInfoConstants.GeneralsOnline.LogoSource, item.IconUrl);
    }

    /// <summary>
    /// Verifies that a manifest-pool failure surfaces as a failed discovery result.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_WhenPoolFails_ReturnsFailureAsync()
    {
        var manifestPool = new Mock<IContentManifestPool>();
        manifestPool
            .Setup(pool => pool.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateFailure("Pool unavailable"));
        var discoverer = new DownloadedContentDiscoverer(
            manifestPool.Object,
            CreateArtworkService().Object,
            new Mock<ILogger<DownloadedContentDiscoverer>>().Object);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 10 });

        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies that GenLauncher and ModDB downloads surface in the library with no filters.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_GenLauncherAndModDbManifests_AppearInLibraryAsync()
    {
        var discoverer = CreateDiscoverer(
        [
            CreateManifest("1.0.genlauncher-zerohour.mod.rise-of-the-reds", "Rise of the Reds", ContentType.Mod, GameType.ZeroHour),
            CreateManifest("1.20150401.moddb.map.rise-of-the-reds-version-185", "Rise of the Reds Version 1.85", ContentType.Map, GameType.ZeroHour),
        ]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 10 });

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.TotalItems);
        Assert.Single(result.Data.Items, item => item.Name == "Rise of the Reds");
        Assert.Single(result.Data.Items, item => item.Name == "Rise of the Reds Version 1.85");
    }

    /// <summary>
    /// Verifies that legacy GeneralsOnline pool entries without stored grouping collapse by
    /// full version (QFE included) while other releases stay separate.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_LegacyGeneralsOnlineManifests_ShareVersionGroupAsync()
    {
        var client = CreateGeneralsOnlineManifest("1.329261.generalsonline.gameclient.60hz", "GeneralsOnline 60Hz", ContentType.GameClient, "032926_QFE1");
        var gameData = CreateGeneralsOnlineManifest("1.329261.generalsonline.patch.gamedata", "GeneralsOnline Game Data", ContentType.Patch, "032926_QFE1");
        var nextQfe = CreateGeneralsOnlineManifest("1.329262.generalsonline.gameclient.60hz", "GeneralsOnline 60Hz", ContentType.GameClient, "032926_QFE2");

        var discoverer = CreateDiscoverer([client, gameData, nextQfe]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 10 });

        Assert.True(result.Success);
        Assert.Equal(3, result.Data!.Items.Count());
        var clientItem = Assert.Single(result.Data.Items, item => item.Id == client.Id.Value);
        var gameDataItem = Assert.Single(result.Data.Items, item => item.Id == gameData.Id.Value);
        var nextQfeItem = Assert.Single(result.Data.Items, item => item.Id == nextQfe.Id.Value);
        Assert.NotNull(clientItem.VariantGroupId);
        Assert.Equal(clientItem.VariantGroupId, gameDataItem.VariantGroupId);
        Assert.NotEqual(clientItem.VariantGroupId, nextQfeItem.VariantGroupId);
        Assert.Equal("Generals Online 032926_QFE1", clientItem.VariantFamilyName);
    }

    /// <summary>
    /// Verifies that coincidental version equality never groups non-GeneralsOnline content.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_NonGeneralsOnlineSameVersion_DoesNotGroupAsync()
    {
        var first = CreateManifest("1.0.test.mod.alpha", "Alpha Mod", ContentType.Mod, GameType.ZeroHour);
        first.Version = "1.0";
        var second = CreateManifest("1.0.test.map.bravo", "Bravo Map", ContentType.Map, GameType.ZeroHour);
        second.Version = "1.0";

        var discoverer = CreateDiscoverer([first, second]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 10 });

        Assert.True(result.Success);
        Assert.All(result.Data!.Items, item => Assert.Null(item.VariantGroupId));
    }

    /// <summary>
    /// Verifies that a stored variant group id takes precedence over derived grouping.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_StoredVariantGroupId_TakesPrecedenceAsync()
    {
        var manifest = CreateGeneralsOnlineManifest("1.329261.generalsonline.gameclient.60hz", "GeneralsOnline 60Hz", ContentType.GameClient, "032926_QFE1");
        manifest.Metadata = new ContentMetadata { VariantGroupId = "custom-group", VariantFamilyName = "Custom Family" };

        var discoverer = CreateDiscoverer([manifest]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 10 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("custom-group", item.VariantGroupId);
        Assert.Equal("Custom Family", item.VariantFamilyName);
    }

    /// <summary>
    /// Verifies that an unmappable manifest neither consumes a page slot nor inflates
    /// totals: the page fills with valid entries and counts exclude the skipped one.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_WithUnmappableManifest_FillsPageAndExcludesFromTotalsAsync()
    {
        var broken = CreateManifest("1.20260101.test.mod.alpha", "Alpha Mod", ContentType.Mod, GameType.ZeroHour);
        var valid = CreateManifest("1.20260102.test.mod.bravo", "Bravo Mod", ContentType.Mod, GameType.ZeroHour);

        var artworkService = CreateArtworkService();
        artworkService
            .Setup(service => service.GetLocalArtworkPath(broken.Id.Value, It.IsAny<ContentArtworkKind>()))
            .Throws(new InvalidOperationException("Corrupt artwork index"));

        var discoverer = CreateDiscoverer([broken, valid], artworkService);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 1, Page = 1 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("Bravo Mod", item.Name);
        Assert.Equal(1, result.Data.TotalItems);
        Assert.False(result.Data.HasMoreItems);
    }

    private static DownloadedContentDiscoverer CreateDiscoverer(
        IReadOnlyList<ContentManifest> manifests,
        Mock<IContentArtworkService>? artworkService = null)
    {
        var manifestPool = new Mock<IContentManifestPool>();
        manifestPool
            .Setup(pool => pool.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(manifests));
        return new DownloadedContentDiscoverer(
            manifestPool.Object,
            (artworkService ?? CreateArtworkService()).Object,
            new Mock<ILogger<DownloadedContentDiscoverer>>().Object);
    }

    private static Mock<IContentArtworkService> CreateArtworkService()
    {
        var artworkService = new Mock<IContentArtworkService>();
        artworkService
            .Setup(service => service.PrefetchArtworkAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        return artworkService;
    }

    private static ContentManifest CreateManifest(string id, string name, ContentType contentType, GameType game)
    {
        return new ContentManifest
        {
            Id = ManifestId.Create(id),
            Name = name,
            ContentType = contentType,
            TargetGame = game,
        };
    }

    private static ContentManifest CreateGeneralsOnlineManifest(string id, string name, ContentType contentType, string version)
    {
        return new ContentManifest
        {
            Id = ManifestId.Create(id),
            Name = name,
            Version = version,
            ContentType = contentType,
            TargetGame = GameType.ZeroHour,
            OriginalProviderName = PublisherTypeConstants.GeneralsOnline,
            Publisher = new PublisherInfo { Name = "Generals Online", PublisherType = PublisherTypeConstants.GeneralsOnline },
        };
    }
}
