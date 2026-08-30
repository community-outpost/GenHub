using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Downloads.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Downloads;

/// <summary>
/// Tests for <see cref="ContentStateService"/> session-download mapping.
/// </summary>
public class ContentStateServiceTests
{
    /// <summary>
    /// After NotifyStateChanged records a download, GetStateAsync must report Downloaded for the
    /// original catalog ID even though the manifest name differs from the catalog name.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task GetStateAsync_AfterNotifyStateChanged_MapsCatalogIdToManifestAsync()
    {
        const string catalogId = "GeneralsOnline_060526_QFE1";
        const string manifestId = "1.605261.generalsonline.gameclient.60hz";

        var pool = new Mock<IContentManifestPool>();
        pool.Setup(p => p.IsManifestAcquiredAsync(It.Is<ManifestId>(m => m.Value == manifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        pool.Setup(p => p.IsManifestAcquiredAsync(It.Is<ManifestId>(m => m.Value != manifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        pool.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<System.Collections.Generic.IEnumerable<ContentManifest>>.CreateSuccess([]));

        var service = new ContentStateService(pool.Object, NullLogger<ContentStateService>.Instance);
        service.NotifyStateChanged(catalogId, ContentState.Downloaded, manifestId);

        var item = new ContentSearchResult
        {
            Id = catalogId,
            Name = "Generals Online",
            ProviderName = "generalsonline",
            ContentType = ContentType.GameClient,
        };

        var state = await service.GetStateAsync(item);
        var localId = await service.GetLocalManifestIdAsync(item);

        Assert.Equal(ContentState.Downloaded, state);
        Assert.Equal(manifestId, localId);
    }

    /// <summary>
    /// Verifies that a manifest remembers its source card and remains visible after a restart.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task GetStateAsync_AfterRestart_MatchesPersistedOriginalContentIdentityAsync()
    {
        var item = new ContentSearchResult
        {
            Id = "catalog-release-123",
            ProviderName = "GitHub Releases",
            Name = "Renamed by publisher factory",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };
        var storedManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.20250722.thesuperhackers.gameclient.zerohour"),
            OriginalProviderName = item.ProviderName,
            OriginalContentId = item.Id,
        };
        var pool = new Mock<IContentManifestPool>();
        pool.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<System.Collections.Generic.IEnumerable<ContentManifest>>.CreateSuccess([storedManifest]));

        var service = new ContentStateService(pool.Object, NullLogger<ContentStateService>.Instance);

        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(item));
        Assert.Equal(storedManifest.Id.Value, await service.GetLocalManifestIdAsync(item));
    }

    /// <summary>
    /// Verifies that legacy SuperHackers manifests mark only their matching game card as installed.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task GetStateAsync_LegacySuperHackersManifest_RecognizesOnlyItsInstalledVariantAsync()
    {
        var storedZeroHour = new ContentManifest
        {
            Id = ManifestId.Create("1.20250722.thesuperhackers.gameclient.zerohour"),
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };
        var pool = new Mock<IContentManifestPool>();
        pool.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<System.Collections.Generic.IEnumerable<ContentManifest>>.CreateSuccess([storedZeroHour]));
        pool.Setup(p => p.IsManifestAcquiredAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        var service = new ContentStateService(pool.Object, NullLogger<ContentStateService>.Instance);

        var zeroHourCard = CreateSuperHackersCard(GameType.ZeroHour);
        var generalsCard = CreateSuperHackersCard(GameType.Generals);

        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(zeroHourCard));
        Assert.Equal(storedZeroHour.Id.Value, await service.GetLocalManifestIdAsync(zeroHourCard));
        Assert.Equal(ContentState.NotDownloaded, await service.GetStateAsync(generalsCard));
    }

    /// <summary>
    /// CommunityOutpost's community-patch card has a display Name ("Community Patch
    /// (TheSuperHackers Build)") that diverges from the installed manifest content-name
    /// segment ("communitypatch"). Detection must use the card's manifest-style Id, not the
    /// drifting Name.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task GetStateAsync_CommunityPatchCard_MatchesByCardIdNotNameAsync()
    {
        var item = new ContentSearchResult
        {
            Id = "1.20260802.communityoutpost.gameclient.community-patch",
            Name = "Community Patch (TheSuperHackers Build)",
            ProviderName = PublisherTypeConstants.CommunityOutpost,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };
        item.ResolverMetadata[CommunityOutpostCatalogConstants.ContentCodeKey] = "community-patch";

        var storedManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.20260802.communityoutpost.gameclient.communitypatch"),
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };

        var pool = new Mock<IContentManifestPool>();
        pool.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<System.Collections.Generic.IEnumerable<ContentManifest>>.CreateSuccess([storedManifest]));
        pool.Setup(p => p.IsManifestAcquiredAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var service = new ContentStateService(pool.Object, NullLogger<ContentStateService>.Instance);

        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(item));
        Assert.Equal(storedManifest.Id.Value, await service.GetLocalManifestIdAsync(item));
    }

    /// <summary>
    /// A CommunityOutpost addon card whose installed manifest is one resolution variant
    /// (cbpx-720p) must resolve as downloaded via symmetric variant-suffix stripping, keyed off
    /// the card Id content segment ("cbpx").
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task GetStateAsync_VariantAddonCard_MatchesViaSuffixStrippedIdAsync()
    {
        var item = new ContentSearchResult
        {
            Id = "1.0.communityoutpost.addon.cbpx",
            Name = "Control Bar Pro (Xezon)",
            ProviderName = PublisherTypeConstants.CommunityOutpost,
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
        };

        var storedVariant = new ContentManifest
        {
            Id = ManifestId.Create("1.10.communityoutpost.addon.cbpx-720p"),
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
        };

        var pool = new Mock<IContentManifestPool>();
        pool.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<System.Collections.Generic.IEnumerable<ContentManifest>>.CreateSuccess([storedVariant]));
        pool.Setup(p => p.IsManifestAcquiredAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var service = new ContentStateService(pool.Object, NullLogger<ContentStateService>.Instance);

        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(item));
        Assert.Equal(storedVariant.Id.Value, await service.GetLocalManifestIdAsync(item));
    }

    /// <summary>
    /// GeneralsOnline exposes one parent game-client card whose installed manifests are variant
    /// siblings (60hz/144hz) with no shared content-name token. The card must resolve as
    /// downloaded via the publisher-family fallback.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task GetStateAsync_GeneralsOnlineParentCard_MatchesVariantFamilyAsync()
    {
        var item = new ContentSearchResult
        {
            Id = "GeneralsOnline_060526_QFE1",
            Name = "Generals Online",
            ProviderName = PublisherTypeConstants.GeneralsOnline,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };

        var storedVariant = new ContentManifest
        {
            Id = ManifestId.Create("1.605261.generalsonline.gameclient.60hz"),
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };

        var pool = new Mock<IContentManifestPool>();
        pool.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<System.Collections.Generic.IEnumerable<ContentManifest>>.CreateSuccess([storedVariant]));
        pool.Setup(p => p.IsManifestAcquiredAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var service = new ContentStateService(pool.Object, NullLogger<ContentStateService>.Instance);

        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(item));
        Assert.Equal(storedVariant.Id.Value, await service.GetLocalManifestIdAsync(item));
    }

    /// <summary>
    /// Two distinct CommunityOutpost addon releases must not cross-detect: a card for "gent"
    /// must not be marked downloaded when only a "cbpx" sibling manifest is installed. Guards
    /// against the publisher-family fallback over-matching within a publisher.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task GetStateAsync_DistinctSamePublisherReleases_DoNotCrossMatchAsync()
    {
        var cbpxCard = new ContentSearchResult
        {
            Id = "1.0.communityoutpost.addon.cbpx",
            Name = "Control Bar Pro (Xezon)",
            ProviderName = PublisherTypeConstants.CommunityOutpost,
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
        };
        var gentCard = new ContentSearchResult
        {
            Id = "1.0.communityoutpost.addon.gent",
            Name = "GenTool",
            ProviderName = PublisherTypeConstants.CommunityOutpost,
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
        };

        // Only the cbpx variant is installed; gent has no manifest.
        var storedCbpx = new ContentManifest
        {
            Id = ManifestId.Create("1.10.communityoutpost.addon.cbpx-720p"),
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
        };

        var pool = new Mock<IContentManifestPool>();
        pool.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<System.Collections.Generic.IEnumerable<ContentManifest>>.CreateSuccess([storedCbpx]));
        pool.Setup(p => p.IsManifestAcquiredAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var service = new ContentStateService(pool.Object, NullLogger<ContentStateService>.Instance);

        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(cbpxCard));
        Assert.Equal(ContentState.NotDownloaded, await service.GetStateAsync(gentCard));
    }

    /// <summary>
    /// A ModDB release row keyed by filename must still resolve as downloaded when the stored
    /// manifest was created from the catalog card of the same archive URL.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task GetStateAsync_ModDbRow_MatchesStoredManifestByDownloadUrlAsync()
    {
        const string downloadUrl = "https://www.moddb.com/downloads/start/313719";
        var row = new ContentSearchResult
        {
            Id = $"file:{downloadUrl}",
            Name = "GeneralsUndone_v1.0.zip",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SelectedDownloadUrl = downloadUrl,
        };
        var storedManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.20260801.moddb.addon.ccgeneralsundone"),
            Name = "C&C Generals Undone",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Files =
            [
                new ManifestFile { DownloadUrl = downloadUrl },
            ],
        };

        var pool = new Mock<IContentManifestPool>();
        pool.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<System.Collections.Generic.IEnumerable<ContentManifest>>.CreateSuccess([storedManifest]));
        pool.Setup(p => p.IsManifestAcquiredAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var service = new ContentStateService(pool.Object, NullLogger<ContentStateService>.Instance);

        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(row));
        Assert.Equal(storedManifest.Id.Value, await service.GetLocalManifestIdAsync(row));
    }

    /// <summary>
    /// Verifies that acquiring a 1080p variant only marks the 1080p card as downloaded and leaves other variants not downloaded.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task GetStateAsync_WhenVariantManifestDownloaded_OnlyMatchesMatchingVariantCardAsync()
    {
        // Stored manifest for 1080p variant
        var manifest1080p = new ContentManifest
        {
            Id = ManifestId.Create("1.103.github.addon.lemoncontrolbar1080p"),
            Name = "Control Bar Pro Lemon Edition ZH (1080p)",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                PublisherType = "github",
                Website = "https://github.com/L3-M/GeneralsControlBar",
            },
        };

        var pool = new Mock<IContentManifestPool>();
        pool.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<System.Collections.Generic.IEnumerable<ContentManifest>>.CreateSuccess([manifest1080p]));
        pool.Setup(p => p.IsManifestAcquiredAsync(manifest1080p.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        pool.Setup(p => p.IsManifestAcquiredAsync(It.Is<ManifestId>(m => m.Value != manifest1080p.Id.Value), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var service = new ContentStateService(pool.Object, NullLogger<ContentStateService>.Instance);

        var card1080p = new ContentSearchResult
        {
            Id = "github.l3-m.generalscontrolbar.v1.3.1080p",
            Name = "GeneralsControlBar (1080p)",
            ProviderName = "github",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://github.com/L3-M/GeneralsControlBar",
        };

        var card720p = new ContentSearchResult
        {
            Id = "github.l3-m.generalscontrolbar.v1.3.720p",
            Name = "GeneralsControlBar (720p)",
            ProviderName = "github",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://github.com/L3-M/GeneralsControlBar",
        };

        var card900p = new ContentSearchResult
        {
            Id = "github.l3-m.generalscontrolbar.v1.3.900p",
            Name = "GeneralsControlBar (900p)",
            ProviderName = "github",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://github.com/L3-M/GeneralsControlBar",
        };

        var card1440p = new ContentSearchResult
        {
            Id = "github.l3-m.generalscontrolbar.v1.3.1440p",
            Name = "GeneralsControlBar (1440p)",
            ProviderName = "github",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://github.com/L3-M/GeneralsControlBar",
        };

        var card4k = new ContentSearchResult
        {
            Id = "github.l3-m.generalscontrolbar.v1.3.4k",
            Name = "GeneralsControlBar (4K)",
            ProviderName = "github",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://github.com/L3-M/GeneralsControlBar",
        };

        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(card1080p));
        Assert.Equal(ContentState.NotDownloaded, await service.GetStateAsync(card720p));
        Assert.Equal(ContentState.NotDownloaded, await service.GetStateAsync(card900p));
        Assert.Equal(ContentState.NotDownloaded, await service.GetStateAsync(card1440p));
        Assert.Equal(ContentState.NotDownloaded, await service.GetStateAsync(card4k));
    }

    /// <summary>
    /// Verifies that a generic non-variant manifest does not match any variant card.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task GetStateAsync_WhenNonVariantManifestInPool_DoesNotMatchVariantCardsAsync()
    {
        var genericManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.github.addon.controlbarprolemoneditionzh"),
            Name = "Control Bar Pro Lemon Edition ZH",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                PublisherType = "github",
                Website = "https://github.com/L3-M/GeneralsControlBar",
            },
        };

        var pool = new Mock<IContentManifestPool>();
        pool.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<System.Collections.Generic.IEnumerable<ContentManifest>>.CreateSuccess([genericManifest]));
        pool.Setup(p => p.IsManifestAcquiredAsync(genericManifest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        pool.Setup(p => p.IsManifestAcquiredAsync(It.Is<ManifestId>(m => m.Value != genericManifest.Id.Value), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var service = new ContentStateService(pool.Object, NullLogger<ContentStateService>.Instance);

        var card1080p = new ContentSearchResult
        {
            Id = "github.l3-m.generalscontrolbar.v1.3.1080p",
            Name = "GeneralsControlBar (1080p)",
            ProviderName = "github",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://github.com/L3-M/GeneralsControlBar",
        };

        Assert.Equal(ContentState.NotDownloaded, await service.GetStateAsync(card1080p));
    }

    /// <summary>
    /// Verifies that installing one ModDB mod does not mark distinct ModDB mods as downloaded.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task GetStateAsync_DistinctModDBReleases_DoNotCrossMatchAsync()
    {
        var generalsUndoneCard = new ContentSearchResult
        {
            Id = "1.20260807.moddb.mod.generalsundonev101patch",
            Name = "Generals Undone v1.01 Patch",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            LastUpdated = new DateTime(2026, 8, 7),
        };
        generalsUndoneCard.ResolverMetadata[ModDBConstants.ContentIdMetadataKey] = "314093";

        var rebornOmegaCard = new ContentSearchResult
        {
            Id = "1.20260715.moddb.mod.rebornomega104",
            Name = "Reborn Omega 1.04",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            LastUpdated = new DateTime(2026, 7, 15),
        };
        rebornOmegaCard.ResolverMetadata[ModDBConstants.ContentIdMetadataKey] = "reborn-omega";

        var storedGeneralsUndone = new ContentManifest
        {
            Id = ManifestId.Create("1.20260807.moddb.mod.generalsundonev101patch"),
            Name = "Generals Undone v1.01 Patch",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                PublisherType = "moddb",
            },
        };

        var pool = new Mock<IContentManifestPool>();
        pool.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<System.Collections.Generic.IEnumerable<ContentManifest>>.CreateSuccess([storedGeneralsUndone]));
        pool.Setup(p => p.IsManifestAcquiredAsync(storedGeneralsUndone.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        pool.Setup(p => p.IsManifestAcquiredAsync(It.Is<ManifestId>(m => m.Value != storedGeneralsUndone.Id.Value), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var service = new ContentStateService(pool.Object, NullLogger<ContentStateService>.Instance);

        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(generalsUndoneCard));
        Assert.Equal(storedGeneralsUndone.Id.Value, await service.GetLocalManifestIdAsync(generalsUndoneCard));
        Assert.Equal(ContentState.NotDownloaded, await service.GetStateAsync(rebornOmegaCard));
        Assert.Null(await service.GetLocalManifestIdAsync(rebornOmegaCard));
    }

    /// <summary>
    /// Verifies that discovering a newer release date for an installed ModDB mod reports update available.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task GetStateAsync_ModDBUpdateAvailable_WhenNewerReleaseDateAsync()
    {
        var storedManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.20260801.moddb.mod.generalsundone"),
            Name = "Generals Undone",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                PublisherType = "moddb",
            },
        };

        var newerCard = new ContentSearchResult
        {
            Id = "1.20260810.moddb.mod.generalsundone",
            Name = "Generals Undone",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            LastUpdated = new DateTime(2026, 8, 10),
        };
        newerCard.ResolverMetadata[ModDBConstants.ContentIdMetadataKey] = "cc-generals-undone";

        var pool = new Mock<IContentManifestPool>();
        pool.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<System.Collections.Generic.IEnumerable<ContentManifest>>.CreateSuccess([storedManifest]));
        pool.Setup(p => p.IsManifestAcquiredAsync(storedManifest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        pool.Setup(p => p.IsManifestAcquiredAsync(It.Is<ManifestId>(m => m.Value != storedManifest.Id.Value), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var service = new ContentStateService(pool.Object, NullLogger<ContentStateService>.Instance);

        Assert.Equal(ContentState.UpdateAvailable, await service.GetStateAsync(newerCard));
        Assert.Equal(storedManifest.Id.Value, await service.GetLocalManifestIdAsync(newerCard));
    }

    /// <summary>
    /// Verifies that downloading a patch from a ModDB mod does not mark the un-downloaded main release
    /// or its release row as downloaded, even though they share the parent mod page URL.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task GetStateAsync_ModDbMainRelease_DoesNotMatchDownloadedPatchManifest_WhenSharingModSourceUrlAsync()
    {
        const string modPageUrl = "https://www.moddb.com/mods/cc-generals-undone/downloads/cc-generals-undone";
        const string patchDownloadUrl = "https://www.moddb.com/downloads/start/314093";
        const string mainReleaseDownloadUrl = "https://www.moddb.com/downloads/start/313719";

        var storedPatchManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.20260807.moddb.mod.generalsundonev101patch"),
            Name = "Generals Undone v1.01 Patch",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                PublisherType = "moddb",
                SupportUrl = modPageUrl,
            },
            Files =
            [
                new ManifestFile { DownloadUrl = patchDownloadUrl },
            ],
        };

        var pool = new Mock<IContentManifestPool>();
        pool.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<System.Collections.Generic.IEnumerable<ContentManifest>>.CreateSuccess([storedPatchManifest]));
        pool.Setup(p => p.IsManifestAcquiredAsync(storedPatchManifest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        pool.Setup(p => p.IsManifestAcquiredAsync(It.Is<ManifestId>(m => m.Value != storedPatchManifest.Id.Value), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var service = new ContentStateService(pool.Object, NullLogger<ContentStateService>.Instance);

        var patchRow = new ContentSearchResult
        {
            Id = $"file:{patchDownloadUrl}",
            Name = "Generals Undone v1.01 Patch",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceUrl = modPageUrl,
            SelectedDownloadUrl = patchDownloadUrl,
            LastUpdated = new DateTime(2026, 8, 7),
        };

        var mainReleaseCard = new ContentSearchResult
        {
            Id = "1.20260801.moddb.mod.ccgeneralsundone",
            Name = "C&C Generals Undone",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceUrl = modPageUrl,
            LastUpdated = new DateTime(2026, 8, 1),
        };
        mainReleaseCard.ResolverMetadata[ModDBConstants.ContentIdMetadataKey] = "cc-generals-undone";

        var mainReleaseRow = new ContentSearchResult
        {
            Id = $"file:{mainReleaseDownloadUrl}",
            Name = "C&C Generals Undone",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceUrl = modPageUrl,
            SelectedDownloadUrl = mainReleaseDownloadUrl,
            LastUpdated = new DateTime(2026, 8, 2),
        };

        // The downloaded patch row must resolve as downloaded
        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(patchRow));
        Assert.Equal(storedPatchManifest.Id.Value, await service.GetLocalManifestIdAsync(patchRow));

        // The un-downloaded main release card and row must NOT match the patch
        Assert.Equal(ContentState.NotDownloaded, await service.GetStateAsync(mainReleaseCard));
        Assert.Null(await service.GetLocalManifestIdAsync(mainReleaseCard));

        Assert.Equal(ContentState.NotDownloaded, await service.GetStateAsync(mainReleaseRow));
        Assert.Null(await service.GetLocalManifestIdAsync(mainReleaseRow));
    }

    /// <summary>
    /// Verifies that when only one release of a ModDB mod is downloaded, other older releases in the releases
    /// list resolve as not downloaded and return null local manifest ID.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task GetStateAsync_ModDbMultipleReleases_OnlyDownloadedReleaseIsMarkedDownloadedAsync()
    {
        const string modPageUrl = "https://www.moddb.com/mods/janus-syndicate/downloads/admiral-z-v92a";
        const string urlV92a = "https://www.moddb.com/downloads/start/307616";
        const string urlV091a = "https://www.moddb.com/downloads/start/297149";
        const string urlV09a = "https://www.moddb.com/downloads/start/297079";

        var storedManifestV92a = new ContentManifest
        {
            Id = ManifestId.Create("1.20260413.moddb.mod.admiralzv92a"),
            Name = "Admiral Z v92a",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                PublisherType = "moddb",
                SupportUrl = modPageUrl,
            },
            Files =
            [
                new ManifestFile { DownloadUrl = urlV92a },
            ],
            OriginalContentId = $"file:{urlV92a}",
            OriginalProviderName = "ModDB",
        };

        var pool = new Mock<IContentManifestPool>();
        pool.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<System.Collections.Generic.IEnumerable<ContentManifest>>.CreateSuccess([storedManifestV92a]));
        pool.Setup(p => p.IsManifestAcquiredAsync(storedManifestV92a.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        pool.Setup(p => p.IsManifestAcquiredAsync(It.Is<ManifestId>(m => m.Value != storedManifestV92a.Id.Value), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var service = new ContentStateService(pool.Object, NullLogger<ContentStateService>.Instance);

        var row92a = new ContentSearchResult
        {
            Id = $"file:{urlV92a}",
            Name = "Admiral Z v92a",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://www.moddb.com/mods/janus-syndicate/downloads/admiral-z-v92a",
            SelectedDownloadUrl = urlV92a,
            LastUpdated = new DateTime(2026, 4, 13),
        };
        row92a.ResolverMetadata[ModDBConstants.ContentIdMetadataKey] = "admiral-z-v92a";

        var row091a = new ContentSearchResult
        {
            Id = $"file:{urlV091a}",
            Name = "Admiral Z v0.91a",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://www.moddb.com/mods/janus-syndicate/downloads/admiral-z-v091a",
            SelectedDownloadUrl = urlV091a,
            LastUpdated = new DateTime(2025, 9, 24),
        };
        row091a.ResolverMetadata[ModDBConstants.ContentIdMetadataKey] = "admiral-z-v091a";

        var row09a = new ContentSearchResult
        {
            Id = $"file:{urlV09a}",
            Name = "AdmiralZ v0.9a",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://www.moddb.com/mods/janus-syndicate/downloads/admiral-z-v09a",
            SelectedDownloadUrl = urlV09a,
            LastUpdated = new DateTime(2025, 9, 21),
        };
        row09a.ResolverMetadata[ModDBConstants.ContentIdMetadataKey] = "admiral-z-v09a";

        // Downloaded row is Downloaded
        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(row92a));
        Assert.Equal(storedManifestV92a.Id.Value, await service.GetLocalManifestIdAsync(row92a));

        // Other release rows are NotDownloaded
        Assert.Equal(ContentState.NotDownloaded, await service.GetStateAsync(row091a));
        Assert.Null(await service.GetLocalManifestIdAsync(row091a));

        Assert.Equal(ContentState.NotDownloaded, await service.GetStateAsync(row09a));
        Assert.Null(await service.GetLocalManifestIdAsync(row09a));
    }

    /// <summary>
    /// Verifies that when a newer version of content is installed, an older prospective release is not
    /// reported as downloaded.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task GetStateAsync_OlderProspectiveRelease_WhenNewerManifestInstalled_ReturnsNotDownloadedAsync()
    {
        var storedManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.20260413.moddb.mod.admiralz"),
            Name = "Admiral Z",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                PublisherType = "moddb",
                SupportUrl = "https://www.moddb.com/mods/janus-syndicate",
            },
        };

        var pool = new Mock<IContentManifestPool>();
        pool.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<System.Collections.Generic.IEnumerable<ContentManifest>>.CreateSuccess([storedManifest]));
        pool.Setup(p => p.IsManifestAcquiredAsync(storedManifest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        pool.Setup(p => p.IsManifestAcquiredAsync(It.Is<ManifestId>(m => m.Value != storedManifest.Id.Value), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var service = new ContentStateService(pool.Object, NullLogger<ContentStateService>.Instance);

        var olderRelease = new ContentSearchResult
        {
            Id = "1.20250924.moddb.mod.admiralz",
            Name = "Admiral Z",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            LastUpdated = new DateTime(2025, 9, 24),
        };

        var sameRelease = new ContentSearchResult
        {
            Id = "1.20260413.moddb.mod.admiralz",
            Name = "Admiral Z",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            LastUpdated = new DateTime(2026, 4, 13),
        };

        var newerRelease = new ContentSearchResult
        {
            Id = "1.20260901.moddb.mod.admiralz",
            Name = "Admiral Z",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            LastUpdated = new DateTime(2026, 9, 1),
        };

        // Older release is NotDownloaded
        Assert.Equal(ContentState.NotDownloaded, await service.GetStateAsync(olderRelease));
        Assert.Null(await service.GetLocalManifestIdAsync(olderRelease));

        // Same release is Downloaded
        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(sameRelease));
        Assert.Equal(storedManifest.Id.Value, await service.GetLocalManifestIdAsync(sameRelease));

        // Newer release has UpdateAvailable
        Assert.Equal(ContentState.UpdateAvailable, await service.GetStateAsync(newerRelease));
        Assert.Equal(storedManifest.Id.Value, await service.GetLocalManifestIdAsync(newerRelease));
    }

    /// <summary>
    /// When content was installed via a catalog with a semantic version (e.g. 103 for v1.3),
    /// discovering it on GitHub with a release date (e.g. 20251114) must not falsely report
    /// UpdateAvailable due to integer comparison between date and semver.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task GetStateAsync_DateProspectiveId_AgainstSemanticLocalManifest_DoesNotReportUpdateAvailableAsync()
    {
        var item = new ContentSearchResult
        {
            Id = "1.13.l3m.addon.generalscontrolbar1080p",
            Name = "GeneralsControlBar (1080p)",
            ProviderName = "github",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Version = "v1.3",
            LastUpdated = new DateTime(2025, 11, 14),
            SourceUrl = "https://github.com/L3-M/GeneralsControlBar",
        };

        var storedManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.103.github.addon.lemoncontrolbarresolution1080p"),
            Name = "Control Bar Pro Lemon Edition ZH (1080p)",
            Version = "1.3",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                PublisherType = "github",
                Website = "https://github.com/L3-M/GeneralsControlBar",
            },
        };

        var pool = new Mock<IContentManifestPool>();
        pool.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<System.Collections.Generic.IEnumerable<ContentManifest>>.CreateSuccess([storedManifest]));
        pool.Setup(p => p.IsManifestAcquiredAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var service = new ContentStateService(pool.Object, NullLogger<ContentStateService>.Instance);

        var state = await service.GetStateAsync(item);
        var localId = await service.GetLocalManifestIdAsync(item);

        Assert.Equal(ContentState.Downloaded, state);
        Assert.Equal(storedManifest.Id.Value, localId);
    }

    /// <summary>
    /// Verifies that IsNewerVersion returns false when comparing an 8-digit date to a semver integer.
    /// </summary>
    [Fact]
    public void IsNewerVersion_MixedDateAndSemver_ReturnsFalse()
    {
        const string dateId = "1.20251114.github.addon.generalscontrolbar1080p";
        const string semverId = "1.103.github.addon.lemoncontrolbarresolution1080p";

        Assert.False(ContentStateService.IsNewerVersion(dateId, semverId));
        Assert.False(ContentStateService.IsNewerVersion(semverId, dateId));
    }

    /// <summary>
    /// Verifies that IsNewerVersion correctly compares two date versions.
    /// </summary>
    [Fact]
    public void IsNewerVersion_BothDates_ComparesNumerically()
    {
        const string newerDateId = "1.20260815.thesuperhackers.gameclient.zerohour";
        const string olderDateId = "1.20260808.thesuperhackers.gameclient.zerohour";

        Assert.True(ContentStateService.IsNewerVersion(newerDateId, olderDateId));
        Assert.False(ContentStateService.IsNewerVersion(olderDateId, newerDateId));
    }

    /// <summary>
    /// Verifies that IsNewerVersion correctly compares two semantic versions.
    /// </summary>
    [Fact]
    public void IsNewerVersion_BothSemvers_ComparesNumerically()
    {
        const string newerSemverId = "1.104.publisher.mod.mycoolmod";
        const string olderSemverId = "1.103.publisher.mod.mycoolmod";

        Assert.True(ContentStateService.IsNewerVersion(newerSemverId, olderSemverId));
        Assert.False(ContentStateService.IsNewerVersion(olderSemverId, newerSemverId));
    }

    /// <summary>
    /// Verifies that IsNewerVersion recognizes equal version strings (e.g. v1.3 vs 1.3) and returns false.
    /// </summary>
    [Fact]
    public void IsNewerVersion_MatchingVersionStrings_ReturnsFalse()
    {
        const string dateId = "1.20251114.github.addon.generalscontrolbar1080p";
        const string semverId = "1.103.github.addon.lemoncontrolbarresolution1080p";

        Assert.False(ContentStateService.IsNewerVersion(dateId, semverId, prospectiveVersionStr: "v1.3", localVersionStr: "1.3"));
    }

    /// <summary>
    /// Verifies that IsNewerVersion recognizes equal Generals Online version strings (081326 vs 081326) and returns false.
    /// </summary>
    [Fact]
    public void IsNewerVersion_MatchingGeneralsOnlineVersions_ReturnsFalse()
    {
        const string prospectiveId = "1.20260813.generalsonline.mappack.generalsonlinequickmatchmaps";
        const string localId = "1.813262.generalsonline.mappack.quickmatchmaps";

        Assert.False(ContentStateService.IsNewerVersion(prospectiveId, localId, prospectiveVersionStr: "081326", localVersionStr: "081326"));
    }

    /// <summary>
    /// Verifies that ModDB content with the same SourceUrl/SupportUrl evaluates to Downloaded,
    /// even if the discovery prospective date (e.g. publication date 2016-03-18) differs from
    /// the downloaded manifest date (e.g. submission date 2016-03-16).
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetStateAsync_ModDBContent_WithSameSourceUrlAndDifferentDate_ReturnsDownloadedAsync()
    {
        // Arrange
        const string detailUrl = "https://www.moddb.com/mods/cc-shockwave/downloads/shockwave-v1201";
        var item = new ContentSearchResult
        {
            Id = "1.20160318.moddb.mod.shockwaveversion1201",
            Name = "ShockWave Version 1.201",
            ProviderName = ModDBConstants.DiscovererSourceName,
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceUrl = detailUrl,
            LastUpdated = new DateTime(2016, 3, 18),
        };

        var storedManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.20160316.moddb.mod.shockwaveversion1201"),
            Name = "ShockWave Version 1.201",
            Version = "20160316",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            OriginalProviderName = ModDBConstants.DiscovererSourceName,
            OriginalContentId = detailUrl,
            Publisher = new PublisherInfo
            {
                PublisherType = ModDBConstants.PublisherPrefix,
                SupportUrl = detailUrl,
                Website = "https://www.moddb.com",
            },
        };

        var pool = new Mock<IContentManifestPool>();
        pool.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([storedManifest]));
        pool.Setup(p => p.IsManifestAcquiredAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManifestId id, CancellationToken _) => OperationResult<bool>.CreateSuccess(id == storedManifest.Id));

        var service = new ContentStateService(pool.Object, NullLogger<ContentStateService>.Instance);

        // Act
        var state = await service.GetStateAsync(item);
        var localManifestId = await service.GetLocalManifestIdAsync(item);

        // Assert
        Assert.Equal(ContentState.Downloaded, state);
        Assert.Equal(storedManifest.Id.Value, localManifestId);
    }

    /// <summary>
    /// Verifies that when a full version of a mod is installed, a patch release row with the same
    /// display name but a different release date and download URL is not marked downloaded.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetStateAsync_ModDBReleaseRows_WithSameNameAndDifferentDates_OnlyDownloadedWhenExactFileAcquiredAsync()
    {
        // Arrange
        const string fullModUrl = "https://www.moddb.com/downloads/start/115960";
        const string patchUrl = "https://www.moddb.com/downloads/start/170000";

        var storedFullManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.20161219.moddb.mod.shwchaos"),
            Name = "SHW Chaos",
            Version = "20161219",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            OriginalProviderName = "ModDB",
            OriginalContentId = $"file:{fullModUrl}",
            Publisher = new PublisherInfo
            {
                PublisherType = "moddb",
                SupportUrl = "https://www.moddb.com/mods/cc-shockwave-chaos/downloads/shw-chaos-mod",
                Website = "https://www.moddb.com",
            },
        };

        var pool = new Mock<IContentManifestPool>();
        pool.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([storedFullManifest]));
        pool.Setup(p => p.IsManifestAcquiredAsync(storedFullManifest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        pool.Setup(p => p.IsManifestAcquiredAsync(It.Is<ManifestId>(m => m.Value != storedFullManifest.Id.Value), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var service = new ContentStateService(pool.Object, NullLogger<ContentStateService>.Instance);

        var patchRow = new ContentSearchResult
        {
            Id = $"file:{patchUrl}",
            Name = "SHW Chaos",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://www.moddb.com/mods/cc-shockwave-chaos/downloads/shw-chaos-patch",
            SelectedDownloadUrl = patchUrl,
            LastUpdated = new DateTime(2020, 9, 5),
        };

        var fullRow = new ContentSearchResult
        {
            Id = $"file:{fullModUrl}",
            Name = "SHW Chaos",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://www.moddb.com/mods/cc-shockwave-chaos/downloads/shw-chaos-mod",
            SelectedDownloadUrl = fullModUrl,
            LastUpdated = new DateTime(2016, 12, 19),
        };

        // Act & Assert
        // The un-downloaded patch row must NOT be reported as Downloaded or have a local manifest ID.
        Assert.Equal(ContentState.NotDownloaded, await service.GetStateAsync(patchRow));
        Assert.Null(await service.GetLocalManifestIdAsync(patchRow));

        // The downloaded full version row must be reported as Downloaded with its on-disk manifest ID.
        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(fullRow));
        Assert.Equal(storedFullManifest.Id.Value, await service.GetLocalManifestIdAsync(fullRow));
    }

    private static ContentSearchResult CreateSuperHackersCard(GameType gameType)
    {
        var item = new ContentSearchResult
        {
            Id = $"github.thesuperhackers.generalsgamecode.weekly-2025-07-22.{gameType}",
            ProviderName = ContentSourceNames.GitHubDiscoverer,
            ContentType = ContentType.GameClient,
            TargetGame = gameType,
        };
        item.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = PublisherTypeConstants.TheSuperHackers;
        item.ResolverMetadata[GitHubConstants.TagMetadataKey] = "weekly-2025-07-22";
        return item;
    }
}
