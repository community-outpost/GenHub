using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Downloads.Services;
using GenHub.Features.Downloads.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Downloads.Services;

/// <summary>
/// Unit tests verifying catalog identity alias matching and content name matching in <see cref="ContentStateService"/>.
/// </summary>
public sealed class ContentStateServiceCatalogIdentityTests
{
    /// <summary>
    /// Tests that IsCompatiblePublisherAlias enforces exact matches and clean hyphen matches.
    /// </summary>
    /// <param name="manifestPublisher">The manifest publisher ID.</param>
    /// <param name="expectedPublisher">The expected publisher ID.</param>
    /// <param name="expectedResult">The expected boolean result.</param>
    [Theory]
    [InlineData("communityoutpost", "communityoutpost", true)]
    [InlineData("community-outpost", "communityoutpost", true)]
    [InlineData("thesuperhackers", "thesuperhackers", true)]
    [InlineData("thesuperhackers", "communityoutpost", false)]
    [InlineData("generic-catalog", "thesuperhackers", false)]
    [InlineData("genhub-test-publishers", "communityoutpost", false)]
    [InlineData("github", "githubtopics", true)]
    [InlineData("githubtopics", "github", true)]
    [InlineData("github", "githubtopic", true)]
    [InlineData("githubtopic", "github", true)]
    [InlineData("githubtopic", "githubtopics", true)]
    [InlineData("github-topics", "github", true)]
    [InlineData("githubtopic", "communityoutpost", false)]
    [InlineData("github", "github", true)]
    [InlineData("github-authorA", "github-authorB", false)]
    [InlineData("github-modder", "github-team", false)]
    [InlineData("genlauncher", "genlauncher", true)]
    [InlineData("genlauncher-zerohour", "genlauncher", true)]
    [InlineData("genlauncher", "genlauncher-generals", true)]
    [InlineData("genlauncher", "communityoutpost", false)]
    public void IsCompatiblePublisherAlias_EnforcesExactAndNormalizedOnly(
        string manifestPublisher,
        string expectedPublisher,
        bool expectedResult)
    {
        var result = ContentStateService.IsCompatiblePublisherAlias(manifestPublisher, expectedPublisher);
        Assert.Equal(expectedResult, result);
    }

    /// <summary>
    /// Tests that ContentNameMatches returns true for hyphen-variant suffixes.
    /// </summary>
    [Fact]
    public void ContentNameMatches_HyphenVariantPrefix_ReturnsTrue()
    {
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.generic-catalog.addon.lemon-controlbar-1080p"),
            TargetGame = GameType.ZeroHour,
        };

        var matches = ContentStateService.ContentNameMatches(
            manifest,
            "generic-catalog",
            "addon",
            GameType.ZeroHour,
            "lemon-controlbar");

        Assert.True(matches);
    }

    /// <summary>
    /// Tests that ContentNameMatches returns false for reverse or non-hyphen variant prefixes.
    /// </summary>
    [Fact]
    public void ContentNameMatches_ReverseOrNoHyphenPrefix_ReturnsFalse()
    {
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.generic-catalog.addon.lemon-controlbar"),
            TargetGame = GameType.ZeroHour,
        };

        var matches = ContentStateService.ContentNameMatches(
            manifest,
            "generic-catalog",
            "addon",
            GameType.ZeroHour,
            "lemon-controlbar-1080p");

        Assert.False(matches);
    }

    /// <summary>
    /// Tests that ContentNameMatches returns false for distinct content names sharing an initial hyphen-delimited token.
    /// </summary>
    [Fact]
    public void ContentNameMatches_DistinctContentsSharingFirstToken_ReturnsFalse()
    {
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.github.mod.generals-gameplay"),
            TargetGame = GameType.ZeroHour,
        };

        var matches = ContentStateService.ContentNameMatches(
            manifest,
            "github",
            "mod",
            GameType.ZeroHour,
            "generals-tools");

        Assert.False(matches);
    }

    /// <summary>
    /// Tests that ContentNameMatches returns false for generic GitHub publishers even if content names match,
    /// preventing cross-owner false positives.
    /// </summary>
    [Fact]
    public void ContentNameMatches_GenericGitHubPublisher_ReturnsFalse()
    {
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.github.mod.cool-mod"),
            TargetGame = GameType.ZeroHour,
        };

        var matches = ContentStateService.ContentNameMatches(
            manifest,
            "github",
            "mod",
            GameType.ZeroHour,
            "cool-mod");

        Assert.False(matches);
    }

    /// <summary>
    /// Verifies that two different GitHub authors who publish content with the same name
    /// do not incorrectly share downloaded state.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetStateAsync_WhenDifferentGitHubAuthorsPublishSameContent_DoesNotShareDownloadedStateAsync()
    {
        var poolMock = new Mock<IContentManifestPool>();

        var authorOneManifest = new ContentManifest
        {
            Id = ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId("AuthorOne", ContentType.Mod, "cool-mod", userVersion: 0)),
            Name = "cool-mod",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            OriginalProviderName = "GitHub",
            Publisher = new PublisherInfo
            {
                Name = "AuthorOne",
                PublisherType = "github",
                Website = "https://github.com/AuthorOne/cool-mod",
            },
        };

        poolMock.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(new List<ContentManifest> { authorOneManifest }));
        poolMock.Setup(p => p.IsManifestAcquiredAsync(authorOneManifest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        poolMock.Setup(p => p.IsManifestAcquiredAsync(It.Is<ManifestId>(m => m != authorOneManifest.Id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        poolMock.Setup(p => p.GetManifestAsync(authorOneManifest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(authorOneManifest));

        var service = new ContentStateService(poolMock.Object, NullLogger<ContentStateService>.Instance);

        var authorOneCard = new ContentSearchResult
        {
            Id = "github.authorone.cool-mod",
            Name = "cool-mod",
            AuthorName = "AuthorOne",
            ProviderName = "GitHub",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://github.com/AuthorOne/cool-mod",
        };
        authorOneCard.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = "AuthorOne";

        var authorTwoCard = new ContentSearchResult
        {
            Id = "github.authortwo.cool-mod",
            Name = "cool-mod",
            AuthorName = "AuthorTwo",
            ProviderName = "GitHub",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://github.com/AuthorTwo/cool-mod",
        };
        authorTwoCard.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = "AuthorTwo";

        // Act & Assert
        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(authorOneCard));
        Assert.Equal(authorOneManifest.Id.Value, await service.GetLocalManifestIdAsync(authorOneCard));

        Assert.Equal(ContentState.NotDownloaded, await service.GetStateAsync(authorTwoCard));
        Assert.Null(await service.GetLocalManifestIdAsync(authorTwoCard));
    }

    /// <summary>
    /// Verifies that a card whose pool holds a newer build of the same content source stays
    /// Downloaded (and still resolves for profiles) instead of prompting a re-download of
    /// older bytes.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetStateAsync_WhenLocalBuildIsNewerThanCard_StaysDownloadedAndResolvesAsync()
    {
        var poolMock = new Mock<IContentManifestPool>();

        var localManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.10001.genericcatalog.mappack.glacampaignbytklyo"),
            Name = "GLA Campaign by TKlyo",
            Version = "1.0.1",
            ContentType = ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
        };

        poolMock.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(new List<ContentManifest> { localManifest }));
        poolMock.Setup(p => p.IsManifestAcquiredAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var service = new ContentStateService(poolMock.Object, NullLogger<ContentStateService>.Instance);

        var card = new ContentSearchResult
        {
            Id = "1.10000.genericcatalog.mappack.glacampaignbytklyo",
            Name = "GLA Campaign by TKlyo",
            Version = "1.0.0",
            ProviderName = "generic-catalog",
            ContentType = ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
        };

        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(card));
        Assert.Equal(localManifest.Id.Value, await service.GetLocalManifestIdAsync(card));
    }

    /// <summary>
    /// Verifies that a catalog card tracking a GitHub upstream repository resolves to
    /// Downloaded when the same repository was acquired through the GitHub provider.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetStateAsync_CatalogUpstreamGitHubCard_MatchesGitHubManifestAsync()
    {
        var poolMock = new Mock<IContentManifestPool>();

        var gitHubManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.13.l3m.addon.generalscontrolbar1080p"),
            Name = "GeneralsControlBar1080p",
            Version = "v1.3",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                Name = "L3-M",
                PublisherType = "github",
                Website = "https://github.com/L3-M",
            },
            Metadata = new ContentMetadata
            {
                ChangelogUrl = "https://github.com/L3-M/GeneralsControlBar/releases/tag/v1.3",
            },
        };

        poolMock.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(new List<ContentManifest> { gitHubManifest }));
        poolMock.Setup(p => p.IsManifestAcquiredAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        poolMock.Setup(p => p.IsManifestAcquiredAsync(gitHubManifest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var service = new ContentStateService(poolMock.Object, NullLogger<ContentStateService>.Instance);

        var catalogCard = new ContentSearchResult
        {
            Id = "1.13.github.addon.l3mcontrolbarresolution1080p",
            Name = "L3M Modern HD Control Bar (1080p)",
            Version = "1.3",
            ProviderName = "undead2146",
            AuthorName = "undead2146",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://github.com/L3-M/GeneralsControlBar",
        };
        catalogCard.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = "L3-M";
        catalogCard.ResolverMetadata[GitHubConstants.RepoMetadataKey] = "GeneralsControlBar";
        catalogCard.ResolverMetadata[GitHubConstants.TagMetadataKey] = "v1.3";

        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(catalogCard));
        Assert.Equal(gitHubManifest.Id.Value, await service.GetLocalManifestIdAsync(catalogCard));
    }

    /// <summary>
    /// Verifies that a GitHub card resolves to Downloaded when the same repository was
    /// acquired through a catalog that stamps the upstream repository URL.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetStateAsync_GitHubCard_MatchesCatalogUpstreamManifestAsync()
    {
        var poolMock = new Mock<IContentManifestPool>();

        var catalogManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.13.github.addon.l3mcontrolbar1080p"),
            Name = "L3M Modern HD Control Bar (1080p)",
            Version = "1.3",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            OriginalProviderName = "github",
            OriginalContentId = "1.13.github.addon.l3mcontrolbar1080p",
            Publisher = new PublisherInfo
            {
                Name = "undead2146",
                PublisherType = "github",
                Website = "https://undead2146.example.com",
                SupportUrl = "https://github.com/L3-M/GeneralsControlBar",
            },
            Metadata = new ContentMetadata
            {
                ChangelogUrl = "https://github.com/L3-M/GeneralsControlBar",
            },
        };

        poolMock.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(new List<ContentManifest> { catalogManifest }));
        poolMock.Setup(p => p.IsManifestAcquiredAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        poolMock.Setup(p => p.IsManifestAcquiredAsync(catalogManifest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var service = new ContentStateService(poolMock.Object, NullLogger<ContentStateService>.Instance);

        var gitHubCard = new ContentSearchResult
        {
            Id = "1.13.l3m.addon.generalscontrolbar1080p",
            Name = "GeneralsControlBar (1080p)",
            Version = "v1.3",
            ProviderName = "GitHub",
            AuthorName = "L3-M",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://github.com/L3-M/GeneralsControlBar",
        };
        gitHubCard.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = "L3-M";
        gitHubCard.ResolverMetadata[GitHubConstants.RepoMetadataKey] = "GeneralsControlBar";
        gitHubCard.ResolverMetadata[GitHubConstants.TagMetadataKey] = "v1.3";

        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(gitHubCard));
        Assert.Equal(catalogManifest.Id.Value, await service.GetLocalManifestIdAsync(gitHubCard));
    }

    /// <summary>
    /// An unknown-game item must not resolve when manifests span several games,
    /// otherwise version/date tiebreaks could pick the wrong game.
    /// </summary>
    [Fact]
    public void FindGitHubRepoMatch_UnknownGameSpanningMultipleGames_ReturnsNull()
    {
        var manifests = new List<ContentManifest>
        {
            CreateGitHubManifest(GameType.Generals, "1.0.github.addon.testwidget"),
            CreateGitHubManifest(GameType.ZeroHour, "2.0.github.addon.testwidget"),
        };

        var result = ContentStateService.FindGitHubRepoMatch(manifests, CreateGitHubItem(GameType.Unknown));

        Assert.Null(result);
    }

    /// <summary>
    /// An unknown-game item still resolves when every candidate targets one game.
    /// </summary>
    [Fact]
    public void FindGitHubRepoMatch_UnknownGameSingleGame_ReturnsManifest()
    {
        var manifests = new List<ContentManifest>
        {
            CreateGitHubManifest(GameType.ZeroHour, "2.0.github.addon.testwidget"),
        };

        var result = ContentStateService.FindGitHubRepoMatch(manifests, CreateGitHubItem(GameType.Unknown));

        Assert.NotNull(result);
        Assert.Equal(GameType.ZeroHour, result.TargetGame);
    }

    /// <summary>
    /// A known-game item resolves to its own game even when both games publish.
    /// </summary>
    [Fact]
    public void FindGitHubRepoMatch_KnownGame_PicksMatchingGame()
    {
        var manifests = new List<ContentManifest>
        {
            CreateGitHubManifest(GameType.Generals, "1.0.github.addon.testwidget"),
            CreateGitHubManifest(GameType.ZeroHour, "2.0.github.addon.testwidget"),
        };

        var result = ContentStateService.FindGitHubRepoMatch(manifests, CreateGitHubItem(GameType.ZeroHour));

        Assert.NotNull(result);
        Assert.Equal(GameType.ZeroHour, result.TargetGame);
    }

    /// <summary>
    /// A cross-type card must not resolve another deliverable's manifest from the same
    /// repository: a Mod card carrying its own asset name must not match a Patch
    /// manifest recording a different asset.
    /// </summary>
    [Fact]
    public void FindGitHubRepoMatch_CrossTypeDifferentAssets_ReturnsNull()
    {
        var manifest = CreateGitHubManifest(GameType.ZeroHour, "1.0.github.patch.testwidget");
        manifest.ContentType = ContentType.Patch;
        manifest.Files.Add(new ManifestFile
        {
            RelativePath = "patch-asset.zip",
            DownloadUrl = "https://github.com/Owner/Repo/releases/download/v1/patch-asset.zip",
        });

        var item = CreateGitHubItem(GameType.ZeroHour);
        item.ContentType = ContentType.Mod;
        item.ResolverMetadata[GitHubConstants.AssetNameMetadataKey] = "mod-asset.zip";

        var result = ContentStateService.FindGitHubRepoMatch(new List<ContentManifest> { manifest }, item);

        Assert.Null(result);
    }

    /// <summary>
    /// Cross-type tolerance still matches the same deliverable: a Mod card whose asset
    /// name appears in a Patch manifest resolves, preserving cross-publisher identity.
    /// </summary>
    [Fact]
    public void FindGitHubRepoMatch_CrossTypeSameAsset_ReturnsManifest()
    {
        var manifest = CreateGitHubManifest(GameType.ZeroHour, "1.0.github.patch.testwidget");
        manifest.ContentType = ContentType.Patch;
        manifest.Files.Add(new ManifestFile
        {
            RelativePath = "shared-asset.zip",
            DownloadUrl = "https://github.com/Owner/Repo/releases/download/v1/shared-asset.zip",
        });

        var item = CreateGitHubItem(GameType.ZeroHour);
        item.ContentType = ContentType.Mod;
        item.ResolverMetadata[GitHubConstants.AssetNameMetadataKey] = "shared-asset.zip";

        var result = ContentStateService.FindGitHubRepoMatch(new List<ContentManifest> { manifest }, item);

        Assert.NotNull(result);
        Assert.Equal(manifest.Id.Value, result.Id.Value);
    }

    /// <summary>
    /// A manifest with null files must not throw during cross-type matching.
    /// </summary>
    [Fact]
    public void FindGitHubRepoMatch_CrossTypeNullFiles_ReturnsNull()
    {
        var manifest = CreateGitHubManifest(GameType.ZeroHour, "1.0.github.patch.testwidget");
        manifest.ContentType = ContentType.Patch;
        manifest.Files = null!;

        var item = CreateGitHubItem(GameType.ZeroHour);
        item.ContentType = ContentType.Mod;
        item.ResolverMetadata[GitHubConstants.AssetNameMetadataKey] = "mod-asset.zip";

        var result = ContentStateService.FindGitHubRepoMatch(new List<ContentManifest> { manifest }, item);

        Assert.Null(result);
    }

    /// <summary>
    /// Items carrying no asset identity keep the legacy cross-type behavior so
    /// catalog-authored cards without asset metadata still resolve.
    /// </summary>
    [Fact]
    public void FindGitHubRepoMatch_CrossTypeWithoutAssetIdentity_ReturnsManifest()
    {
        var manifest = CreateGitHubManifest(GameType.ZeroHour, "1.0.github.patch.testwidget");
        manifest.ContentType = ContentType.Patch;
        manifest.Files.Add(new ManifestFile
        {
            RelativePath = "patch-asset.zip",
            DownloadUrl = "https://github.com/Owner/Repo/releases/download/v1/patch-asset.zip",
        });

        var item = CreateGitHubItem(GameType.ZeroHour);
        item.ContentType = ContentType.Mod;

        var result = ContentStateService.FindGitHubRepoMatch(new List<ContentManifest> { manifest }, item);

        Assert.NotNull(result);
        Assert.Equal(manifest.Id.Value, result.Id.Value);
    }

    /// <summary>
    /// Verifies that a bundle component built through production
    /// <see cref="BundleComponentViewModel.CreateFromSearchResult"/> resolves to Downloaded
    /// when the same upstream GitHub files were acquired through the GitHub provider.
    /// This pins the bundle-card regression where components showed Download while the
    /// standalone cards showed Add to Profile.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetStateAsync_BundleComponentWithUpstreamIdentity_MatchesGitHubManifestAsync()
    {
        const string assetUrl = "https://github.com/L3-M/GeneralsControlBar/releases/download/v1.3/GeneralsControlBar_1080p.zip";
        var poolMock = new Mock<IContentManifestPool>();

        var gitHubManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.13.l3m.addon.generalscontrolbar1080p"),
            Name = "GeneralsControlBar1080p",
            Version = "v1.3",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            OriginalContentId = "1.13.l3m.addon.generalscontrolbar1080p",
            OriginalProviderName = "github",
            Publisher = new PublisherInfo
            {
                Name = "L3-M",
                PublisherType = "github",
                Website = "https://github.com/L3-M",
            },
            Metadata = new ContentMetadata
            {
                ChangelogUrl = "https://github.com/L3-M/GeneralsControlBar/releases/tag/v1.3",
            },
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "GeneralsControlBar_1080p.zip",
                    DownloadUrl = assetUrl,
                },
            ],
        };

        poolMock.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(new List<ContentManifest> { gitHubManifest }));
        poolMock.Setup(p => p.IsManifestAcquiredAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        poolMock.Setup(p => p.IsManifestAcquiredAsync(gitHubManifest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var service = new ContentStateService(poolMock.Object, NullLogger<ContentStateService>.Instance);

        var sibling = new CatalogContentItem
        {
            Id = "l3m-controlbar",
            Name = "L3M Modern HD Control Bar",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            PublisherType = "undead2146",
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = CatalogConstants.UpstreamProviders.GitHubReleases,
                Repository = "L3-M/GeneralsControlBar",
            },
        };

        var release = new ContentRelease
        {
            Version = "1.3",
            IsLatest = true,
            Artifacts =
            [
                new ReleaseArtifact
                {
                    Filename = "GeneralsControlBar_1080p.zip",
                    DownloadUrl = assetUrl,
                    VariantAxis = "resolution",
                    Variant = "1080p",
                    IsDefaultVariant = true,
                },
            ],
        };

        var descriptors = new List<CatalogBundleComponentDescriptor>
        {
            new CatalogBundleComponentDescriptor
            {
                ContentId = "l3m-controlbar",
                Name = "L3M Modern HD Control Bar",
                PublisherId = "undead2146",
                ContentType = ContentType.Addon.ToString(),
                CatalogItemJson = JsonSerializer.Serialize(sibling),
                Variants =
                [
                    new CatalogBundleComponentVariantDescriptor
                    {
                        Axis = "resolution",
                        Label = "1080p",
                        CatalogId = "1.13.github.addon.l3mcontrolbar1080p",
                        ReleaseJson = JsonSerializer.Serialize(release),
                        IsDefault = true,
                    },
                ],
            },
        };

        var bundleResult = new ContentSearchResult
        {
            Id = "1.20260731.undead2146.contentbundle.stack",
            Name = "Stack",
            ContentType = ContentType.ContentBundle,
            TargetGame = GameType.ZeroHour,
            ProviderName = "undead2146",
            AuthorName = "undead2146",
        };
        bundleResult.ResolverMetadata[CatalogConstants.BundleComponentsJsonMetadataKey] =
            JsonSerializer.Serialize(descriptors);

        var component = BundleComponentViewModel.CreateFromSearchResult(bundleResult).Single();
        var componentResult = component.GetSelectedSearchResult();
        Assert.NotNull(componentResult);

        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(componentResult));
        Assert.Equal(gitHubManifest.Id.Value, await service.GetLocalManifestIdAsync(componentResult));
    }

    /// <summary>
    /// Verifies that a bundle component carrying upstream GitHub identity still resolves
    /// when the acquired manifest has no per-file download URLs to overlap, matching the
    /// standalone card through repository identity instead of file identity.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetStateAsync_BundleComponentWithoutFileUrlOverlap_MatchesGitHubManifestAsync()
    {
        var poolMock = new Mock<IContentManifestPool>();

        var gitHubManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.13.l3m.addon.generalscontrolbar1080p"),
            Name = "GeneralsControlBar1080p",
            Version = "v1.3",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            OriginalContentId = "1.13.l3m.addon.generalscontrolbar1080p",
            OriginalProviderName = "github",
            Publisher = new PublisherInfo
            {
                Name = "L3-M",
                PublisherType = "github",
                Website = "https://github.com/L3-M",
            },
            Metadata = new ContentMetadata
            {
                ChangelogUrl = "https://github.com/L3-M/GeneralsControlBar/releases/tag/v1.3",
            },
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "GeneralsControlBar_1080p.zip",
                },
            ],
        };

        poolMock.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(new List<ContentManifest> { gitHubManifest }));
        poolMock.Setup(p => p.IsManifestAcquiredAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        poolMock.Setup(p => p.IsManifestAcquiredAsync(gitHubManifest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var service = new ContentStateService(poolMock.Object, NullLogger<ContentStateService>.Instance);

        var sibling = new CatalogContentItem
        {
            Id = "l3m-controlbar",
            Name = "L3M Modern HD Control Bar",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            PublisherType = "undead2146",
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = CatalogConstants.UpstreamProviders.GitHubReleases,
                Repository = "L3-M/GeneralsControlBar",
            },
        };

        var release = new ContentRelease
        {
            Version = "1.3",
            IsLatest = true,
            Artifacts =
            [
                new ReleaseArtifact
                {
                    Filename = "GeneralsControlBar_1080p.zip",
                    DownloadUrl = "https://example.invalid/mirror/GeneralsControlBar_1080p.zip",
                    VariantAxis = "resolution",
                    Variant = "1080p",
                    IsDefaultVariant = true,
                },
            ],
        };

        var descriptors = new List<CatalogBundleComponentDescriptor>
        {
            new CatalogBundleComponentDescriptor
            {
                ContentId = "l3m-controlbar",
                Name = "L3M Modern HD Control Bar",
                PublisherId = "undead2146",
                ContentType = ContentType.Addon.ToString(),
                CatalogItemJson = JsonSerializer.Serialize(sibling),
                Variants =
                [
                    new CatalogBundleComponentVariantDescriptor
                    {
                        Axis = "resolution",
                        Label = "1080p",
                        CatalogId = "1.13.github.addon.l3mcontrolbar1080p",
                        ReleaseJson = JsonSerializer.Serialize(release),
                        IsDefault = true,
                    },
                ],
            },
        };

        var bundleResult = new ContentSearchResult
        {
            Id = "1.20260731.undead2146.contentbundle.stack",
            Name = "Stack",
            ContentType = ContentType.ContentBundle,
            TargetGame = GameType.ZeroHour,
            ProviderName = "undead2146",
            AuthorName = "undead2146",
        };
        bundleResult.ResolverMetadata[CatalogConstants.BundleComponentsJsonMetadataKey] =
            JsonSerializer.Serialize(descriptors);

        var component = BundleComponentViewModel.CreateFromSearchResult(bundleResult).Single();
        var componentResult = component.GetSelectedSearchResult();
        Assert.NotNull(componentResult);

        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(componentResult));
        Assert.Equal(gitHubManifest.Id.Value, await service.GetLocalManifestIdAsync(componentResult));
    }

    /// <summary>
    /// Verifies that a catalog card tracking a Community Outpost upstream content code resolves to
    /// Downloaded when the content was acquired through the Community Outpost provider.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetStateAsync_CatalogUpstreamCommunityOutpostCard_MatchesCommunityOutpostManifestAsync()
    {
        var poolMock = new Mock<IContentManifestPool>();

        var outpostManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.10000.communityoutpost.addon.hleizerohouren"),
            Name = "Leikeze Competitive Hotkeys (Zero Hour - English)",
            Version = "1.0",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                Name = "Community Outpost",
                PublisherType = "communityoutpost",
                Website = "https://communityoutpost.org",
            },
            Metadata = new ContentMetadata
            {
                Tags = ["content-code:hlei", "variant:zerohour-en", "selected-variant:zerohour-en"],
            },
        };

        poolMock.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(new List<ContentManifest> { outpostManifest }));
        poolMock.Setup(p => p.IsManifestAcquiredAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        poolMock.Setup(p => p.IsManifestAcquiredAsync(outpostManifest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var service = new ContentStateService(poolMock.Object, NullLogger<ContentStateService>.Instance);

        var catalogCard = new ContentSearchResult
        {
            Id = "1.10000.undead2146.addon.leikezehotkeys",
            Name = "Leikeze Competitive Hotkeys",
            Version = "1.0",
            ProviderName = "undead2146",
            AuthorName = "undead2146",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://www.gentool.net/data/hlei.dat",
        };
        catalogCard.ResolverMetadata[CommunityOutpostCatalogConstants.ContentCodeKey] = "hlei";

        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(catalogCard));
        Assert.Equal(outpostManifest.Id.Value, await service.GetLocalManifestIdAsync(catalogCard));
    }

    /// <summary>
    /// Verifies that a bundle component built through production
    /// <see cref="BundleComponentViewModel.CreateFromSearchResult"/> resolves to Downloaded
    /// when tracking an upstream Community Outpost artifact.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetStateAsync_BundleComponentWithCommunityOutpostUpstreamIdentity_MatchesCommunityOutpostManifestAsync()
    {
        var poolMock = new Mock<IContentManifestPool>();

        var outpostManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.10000.communityoutpost.addon.hlei-zerohour-en"),
            Name = "Leikeze Competitive Hotkeys (Zero Hour - English)",
            Version = "1.0",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                Name = "Community Outpost",
                PublisherType = "communityoutpost",
                Website = "https://communityoutpost.org",
            },
            Metadata = new ContentMetadata
            {
                Tags = ["content-code:hlei", "variant:zerohour-en", "selected-variant:zerohour-en"],
            },
        };

        poolMock.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(new List<ContentManifest> { outpostManifest }));
        poolMock.Setup(p => p.IsManifestAcquiredAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        poolMock.Setup(p => p.IsManifestAcquiredAsync(outpostManifest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var service = new ContentStateService(poolMock.Object, NullLogger<ContentStateService>.Instance);

        var sibling = new CatalogContentItem
        {
            Id = "leikeze-hotkeys",
            Name = "Leikeze Competitive Hotkeys",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            PublisherType = "undead2146",
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = "CommunityOutpost",
                ContentCode = "hlei",
            },
        };

        var release = new ContentRelease
        {
            Version = "1.0",
            IsLatest = true,
            Artifacts =
            [
                new ReleaseArtifact
                {
                    Filename = "hlei.dat",
                    DownloadUrl = "https://www.gentool.net/data/hlei.dat",
                    VariantAxis = "language",
                    Variant = "English",
                    IsDefaultVariant = true,
                },
            ],
        };

        var descriptors = new List<CatalogBundleComponentDescriptor>
        {
            new CatalogBundleComponentDescriptor
            {
                ContentId = "leikeze-hotkeys",
                Name = "Leikeze Competitive Hotkeys",
                PublisherId = "undead2146",
                ContentType = ContentType.Addon.ToString(),
                CatalogItemJson = JsonSerializer.Serialize(sibling),
                Variants =
                [
                    new CatalogBundleComponentVariantDescriptor
                    {
                        Axis = "language",
                        Label = "English",
                        CatalogId = "1.10000.undead2146.addon.leikezehotkeys",
                        ReleaseJson = JsonSerializer.Serialize(release),
                        IsDefault = true,
                    },
                ],
            },
        };

        var bundleResult = new ContentSearchResult
        {
            Id = "1.20260731.undead2146.contentbundle.stack",
            Name = "Stack",
            ContentType = ContentType.ContentBundle,
            TargetGame = GameType.ZeroHour,
            ProviderName = "undead2146",
            AuthorName = "undead2146",
        };
        bundleResult.ResolverMetadata[CatalogConstants.BundleComponentsJsonMetadataKey] =
            JsonSerializer.Serialize(descriptors);

        var component = BundleComponentViewModel.CreateFromSearchResult(bundleResult).Single();
        var componentResult = component.GetSelectedSearchResult();
        Assert.NotNull(componentResult);

        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(componentResult));
        Assert.Equal(outpostManifest.Id.Value, await service.GetLocalManifestIdAsync(componentResult));
    }

    private ContentManifest CreateGitHubManifest(GameType game, string id)
    {
        return new ContentManifest
        {
            Id = ManifestId.Create(id),
            Name = "TestWidget",
            ContentType = ContentType.Addon,
            TargetGame = game,
            Publisher = new PublisherInfo
            {
                Name = "Owner",
                PublisherType = "github",
                Website = "https://github.com/Owner/Repo",
            },
        };
    }

    private ContentSearchResult CreateGitHubItem(GameType game)
    {
        return new ContentSearchResult
        {
            Id = "testwidget",
            Name = "TestWidget",
            ContentType = ContentType.Addon,
            TargetGame = game,
            AuthorName = "Owner",
            SourceUrl = "https://github.com/Owner/Repo",
        };
    }
}
