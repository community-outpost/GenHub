using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Downloads.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Downloads;

/// <summary>
/// Comprehensive integration tests for ContentBundle downloading, component synchronization, and profile preparation.
/// </summary>
public sealed class ContentBundleDownloadIntegrationTests
{
    /// <summary>
    /// Verifies that downloading missing bundle members marks all components as downloaded and readies the bundle for profile addition.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DownloadBundleComponents_AllMembersAcquired_MarksComponentsDownloadedAndReadiesBundleAsync()
    {
        // Arrange
        var contentStateServiceMock = new Mock<IContentStateService>();

        var patchComponent = new BundleComponentViewModel
        {
            CatalogContentId = "community-patch",
            Name = "Community Outpost Game Client (Community Patch)",
            ContentTypeDisplay = "Game Client",
            IsOptional = false,
        };
        patchComponent.AddVariant(
            new InstallableVariant { Name = "Default", ManifestId = "community-patch" },
            new ContentSearchResult
            {
                Id = "community-patch",
                Name = "Community Outpost Game Client (Community Patch)",
                ContentType = ContentType.GameClient,
                TargetGame = GameType.ZeroHour,
            });

        var genToolComponent = new BundleComponentViewModel
        {
            CatalogContentId = "gent",
            Name = "GenTool",
            ContentTypeDisplay = "Addon",
            IsOptional = false,
        };
        genToolComponent.AddVariant(
            new InstallableVariant { Name = "Default", ManifestId = "gent" },
            new ContentSearchResult
            {
                Id = "gent",
                Name = "GenTool",
                ContentType = ContentType.Addon,
                TargetGame = GameType.ZeroHour,
            });

        var controlBarComponent = new BundleComponentViewModel
        {
            CatalogContentId = "lemon-controlbar",
            Name = "Control Bar Pro Lemon Edition ZH",
            ContentTypeDisplay = "Addon",
            IsOptional = false,
        };
        controlBarComponent.AddVariant(
            new InstallableVariant { Name = "Default", ManifestId = "lemon-controlbar" },
            new ContentSearchResult
            {
                Id = "lemon-controlbar",
                Name = "Control Bar Pro Lemon Edition ZH",
                ContentType = ContentType.Addon,
                TargetGame = GameType.ZeroHour,
            });

        var hotkeysComponent = new BundleComponentViewModel
        {
            CatalogContentId = "hleg",
            Name = "Legionnaire's Hotkeys",
            ContentTypeDisplay = "Addon",
            IsOptional = false,
        };
        hotkeysComponent.AddVariant(
            new InstallableVariant { Name = "Default", ManifestId = "hleg" },
            new ContentSearchResult
            {
                Id = "hleg",
                Name = "Legionnaire's Hotkeys",
                ContentType = ContentType.Addon,
                TargetGame = GameType.ZeroHour,
            });

        var searchResult = new ContentSearchResult
        {
            Id = "1.20260731.genhubtestpublishers.contentbundle.communityoutpostpatchstack",
            Name = "Community Outpost Patch Stack",
            ContentType = ContentType.ContentBundle,
            TargetGame = GameType.ZeroHour,
            ProviderName = "GenHub Test Publishers",
        };

        var bundleGridItem = new ContentGridItemViewModel(
            searchResult,
            contentStateService: contentStateServiceMock.Object,
            logger: NullLogger<ContentGridItemViewModel>.Instance);

        bundleGridItem.BundleComponents.Add(patchComponent);
        bundleGridItem.BundleComponents.Add(genToolComponent);
        bundleGridItem.BundleComponents.Add(controlBarComponent);
        bundleGridItem.BundleComponents.Add(hotkeysComponent);

        // Act - 1: Verify missing download targets
        var downloadTargets = BundleComponentViewModel.GetRequiredDownloadTargets(bundleGridItem.BundleComponents);
        Assert.Equal(4, downloadTargets.Count);

        // Simulate successful download of all 4 targets
        var patchManifestId = "1.20260802.communityoutpost.gameclient.communitypatch";
        var genToolManifestId = "1.809.communityoutpost.addon.gentool89suite";
        var controlBarManifestId = "1.0.github.addon.controlbarprolemoneditionzh";
        var hotkeysManifestId = "1.0.communityoutpost.addon.legionnaireshotkeyszh";

        patchComponent.MarkDownloaded("community-patch", patchManifestId);
        genToolComponent.MarkDownloaded("gent", genToolManifestId);
        controlBarComponent.MarkDownloaded("lemon-controlbar", controlBarManifestId);
        hotkeysComponent.MarkDownloaded("hleg", hotkeysManifestId);

        // Setup ContentStateService mocks for the downloaded manifests
        contentStateServiceMock
            .Setup(c => c.GetStateByManifestIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.Downloaded);

        contentStateServiceMock
            .Setup(c => c.GetLocalManifestIdAsync(It.Is<ContentSearchResult>(r => r.Id == patchManifestId || r.Id == "community-patch"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(patchManifestId);
        contentStateServiceMock
            .Setup(c => c.GetLocalManifestIdAsync(It.Is<ContentSearchResult>(r => r.Id == genToolManifestId || r.Id == "gent"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(genToolManifestId);
        contentStateServiceMock
            .Setup(c => c.GetLocalManifestIdAsync(It.Is<ContentSearchResult>(r => r.Id == controlBarManifestId || r.Id == "lemon-controlbar"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(controlBarManifestId);
        contentStateServiceMock
            .Setup(c => c.GetLocalManifestIdAsync(It.Is<ContentSearchResult>(r => r.Id == hotkeysManifestId || r.Id == "hleg"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hotkeysManifestId);

        // Act - 2: Verify readiness and profile manifest collection
        var profileManifestIds = await BundleComponentViewModel.GetRequiredProfileManifestIdsAsync(
            bundleGridItem.BundleComponents,
            contentStateServiceMock.Object,
            CancellationToken.None);

        // Assert
        Assert.True(bundleGridItem.AreBundleComponentsReadyForProfile);
        Assert.Equal(4, profileManifestIds.Count);
        Assert.Contains(patchManifestId, profileManifestIds);
        Assert.Contains(genToolManifestId, profileManifestIds);
        Assert.Contains(controlBarManifestId, profileManifestIds);
        Assert.Contains(hotkeysManifestId, profileManifestIds);
    }
}
