using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
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
    /// Verifies that GeneralsOnline pool entries collapse all content types (game client, patch,
    /// mappack) of the same release version into a single variant group, while other releases stay separate.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_GeneralsOnlineManifests_GroupsByReleaseVersionAsync()
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
        Assert.Equal("generalsonline-032926_qfe1", clientItem.VariantGroupId);
        Assert.NotEqual(clientItem.VariantGroupId, nextQfeItem.VariantGroupId);
        Assert.Equal("generalsonline-032926_qfe2", nextQfeItem.VariantGroupId);
        Assert.Equal("Generals Online 032926_QFE1", clientItem.VariantFamilyName);
        Assert.Equal("Generals Online 032926_QFE1", gameDataItem.VariantFamilyName);
        Assert.Equal("Generals Online 032926_QFE2", nextQfeItem.VariantFamilyName);
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
    /// Verifies that legacy Community Outpost variant pool entries without stored grouping
    /// collapse by content code and version while other releases stay separate.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_LegacyCommunityOutpostVariants_ShareVersionedGroupAsync()
    {
        var pro1080 = CreateCommunityOutpostManifest("1.0.communityoutpost.addon.cbpr-1080p", "Control Bar Pro (ExiLe) - 1080p", "1.0", "1080p");
        var pro720 = CreateCommunityOutpostManifest("1.0.communityoutpost.addon.cbpr-720p", "Control Bar Pro (ExiLe) - 720p", "1.0", "720p");
        var nextRelease = CreateCommunityOutpostManifest("1.1.communityoutpost.addon.cbpr-1080p", "Control Bar Pro (ExiLe) - 1080p", "v2026.07.15", "1080p");

        var discoverer = CreateDiscoverer([pro1080, pro720, nextRelease]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 10 });

        Assert.True(result.Success);
        Assert.Equal(3, result.Data!.Items.Count());
        var current = result.Data.Items.Where(item => item.Version == "1.0").ToList();
        var next = result.Data.Items.Where(item => item.Version == "v2026.07.15").ToList();
        Assert.Equal(2, current.Count);
        var updated = Assert.Single(next);
        Assert.Equal("communityoutpost.addon.cbpr.1.0", current[0].VariantGroupId);
        Assert.Equal(current[0].VariantGroupId, current[1].VariantGroupId);
        Assert.Equal("communityoutpost.addon.cbpr.v2026.07.15", updated.VariantGroupId);
        Assert.Equal("Control Bar Pro (ExiLe)", current[0].VariantFamilyName);
    }

    /// <summary>
    /// Verifies that Community Outpost singles keep a null group id so they render as
    /// plain cards without a variant picker.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_CommunityOutpostSingle_DoesNotGroupAsync()
    {
        var single = CreateManifest("1.0.communityoutpost.addon.cbhd", "Control Bar HD", ContentType.Addon, GameType.ZeroHour);
        single.Version = "1.0";
        single.Publisher = new PublisherInfo { Name = "Community Outpost", PublisherType = CommunityOutpostConstants.PublisherType };
        single.Metadata.Tags.Add("contentCode:cbhd");

        var discoverer = CreateDiscoverer([single]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 10 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Null(item.VariantGroupId);
        Assert.Null(item.VariantFamilyName);
    }

    /// <summary>
    /// Verifies that variant-looking manifests from other publishers never join
    /// Community Outpost groups.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_NonCommunityOutpostVariantLike_DoesNotGroupAsync()
    {
        var foreign = CreateManifest("1.0.test.addon.cbpr-1080p", "Foreign Control Bar", ContentType.Addon, GameType.ZeroHour);
        foreign.Version = "1.0";
        foreign.Publisher = new PublisherInfo { Name = "Test", PublisherType = "test" };
        foreign.Metadata.Tags.Add("contentCode:cbpr");
        foreign.Metadata.Tags.Add("variant:1080p");

        var discoverer = CreateDiscoverer([foreign]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 10 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Null(item.VariantGroupId);
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
    /// Verifies that legacy GitHub per-game downloads of one multi-variant release
    /// share a group id while other releases stay separate.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_LegacyGitHubGameClients_ShareReleaseGroupAsync()
    {
        var zeroHour = CreateGitHubManifest(
            "1.20260925.thesuperhackers.gameclient.zerohour",
            "SuperHackers - Zero Hour",
            GameType.ZeroHour,
            "weekly-2026-09-25",
            "github.TheSuperHackers.GeneralsGameCode.weekly-2026-09-25.zerohour");
        var generals = CreateGitHubManifest(
            "1.20260925.thesuperhackers.gameclient.generals",
            "SuperHackers - Generals",
            GameType.Generals,
            "weekly-2026-09-25",
            "github.TheSuperHackers.GeneralsGameCode.weekly-2026-09-25.generals");
        var olderWeekly = CreateGitHubManifest(
            "1.20260918.thesuperhackers.gameclient.zerohour",
            "SuperHackers - Zero Hour",
            GameType.ZeroHour,
            "weekly-2026-09-18",
            "github.TheSuperHackers.GeneralsGameCode.weekly-2026-09-18.zerohour");

        var discoverer = CreateDiscoverer([zeroHour, generals, olderWeekly]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 10 });

        Assert.True(result.Success);
        Assert.Equal(3, result.Data!.Items.Count());
        var zeroHourItem = Assert.Single(result.Data.Items, item => item.Id == zeroHour.Id.Value);
        var generalsItem = Assert.Single(result.Data.Items, item => item.Id == generals.Id.Value);
        var olderItem = Assert.Single(result.Data.Items, item => item.Id == olderWeekly.Id.Value);
        Assert.Equal("github.TheSuperHackers.GeneralsGameCode.weekly-2026-09-25", zeroHourItem.VariantGroupId);
        Assert.Equal(zeroHourItem.VariantGroupId, generalsItem.VariantGroupId);
        Assert.Equal("weekly-2026-09-25", zeroHourItem.VariantFamilyName);
        Assert.Equal(zeroHourItem.VariantFamilyName, generalsItem.VariantFamilyName);
        Assert.NotEqual(zeroHourItem.VariantGroupId, olderItem.VariantGroupId);
    }

    /// <summary>
    /// Verifies that GitHub single-asset downloads keep a null group id so they
    /// render as plain cards without a variant picker.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_GitHubSingleAsset_DoesNotGroupAsync()
    {
        var single = CreateGitHubManifest(
            "1.100.thesuperhackers.patch.generalsgamepatch2",
            "Community Patch 2",
            GameType.ZeroHour,
            "1.100",
            "github.TheSuperHackers.GeneralsGameCode.1.100");

        var discoverer = CreateDiscoverer([single]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 10 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Null(item.VariantGroupId);
        Assert.Null(item.VariantFamilyName);
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

    /// <summary>
    /// Verifies that GeneralsOnline group id resolves to the release version group.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_GeneralsOnlineGroupId_ResolvesToVersionGroupAsync()
    {
        var manifest = CreateGeneralsOnlineManifest("1.329261.generalsonline.gameclient.60hz", "GeneralsOnline 60Hz", ContentType.GameClient, "032926_QFE1");
        manifest.Metadata = new ContentMetadata { VariantGroupId = "generalsonline-gameclient-032926_qfe1" };

        var discoverer = CreateDiscoverer([manifest]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 10 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("generalsonline-032926_qfe1", item.VariantGroupId);
        Assert.Equal(GeneralsOnlineConstants.CoverSource, item.BannerUrl);
    }

    /// <summary>
    /// Verifies that GeneralsOnline family name uses the release version.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_GeneralsOnline_ResolvesVariantFamilyNameAsync()
    {
        var manifest = CreateGeneralsOnlineManifest("1.329261.generalsonline.gameclient.60hz", "GeneralsOnline 60Hz", ContentType.GameClient, "032926_QFE1");

        var discoverer = CreateDiscoverer([manifest]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Take = 10 });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("Generals Online 032926_QFE1", item.VariantFamilyName);
    }

    private static DownloadedContentDiscoverer CreateDiscoverer(
        IReadOnlyList<ContentManifest> manifests,
        Mock<IContentArtworkService>? artworkService = null,
        Mock<ILocalizationService>? localizationService = null)
    {
        var manifestPool = new Mock<IContentManifestPool>();
        manifestPool
            .Setup(pool => pool.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(manifests));
        return new DownloadedContentDiscoverer(
            manifestPool.Object,
            (artworkService ?? CreateArtworkService()).Object,
            new Mock<ILogger<DownloadedContentDiscoverer>>().Object,
            localizationService?.Object);
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

    private static ContentManifest CreateCommunityOutpostManifest(string id, string name, string version, string variantId)
    {
        return new ContentManifest
        {
            Id = ManifestId.Create(id),
            Name = name,
            Version = version,
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            OriginalProviderName = CommunityOutpostConstants.PublisherType,
            Publisher = new PublisherInfo { Name = "Community Outpost", PublisherType = CommunityOutpostConstants.PublisherType },
            Metadata = new ContentMetadata
            {
                Tags = ["contentCode:cbpr", $"variant:{variantId}", $"selectedVariant:{variantId}"],
                SelectedVariantId = variantId,
            },
        };
    }

    private static ContentManifest CreateGitHubManifest(string id, string name, GameType game, string version, string originalContentId)
    {
        return new ContentManifest
        {
            Id = ManifestId.Create(id),
            Name = name,
            Version = version,
            ContentType = ContentType.GameClient,
            TargetGame = game,
            OriginalProviderName = PublisherTypeConstants.GitHub,
            OriginalContentId = originalContentId,
            Publisher = new PublisherInfo { Name = "The Super Hackers", PublisherType = PublisherTypeConstants.TheSuperHackers },
        };
    }
}
