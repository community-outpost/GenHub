using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Downloads.Services;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Downloads;

/// <summary>
/// Comprehensive integration tests for SuperHackers bundle components and profile reconciliation.
/// </summary>
public sealed class SuperHackersBundleIntegrationTests
{
    /// <summary>
    /// Verifies that bundle component variant search results map the target game from game-type variant axes.
    /// </summary>
    [Fact]
    public void BundleComponent_GameTypeVariant_SetsCorrectTargetGame()
    {
        // arrange
        var descriptors = new List<CatalogBundleComponentDescriptor>
        {
            new CatalogBundleComponentDescriptor
            {
                ContentId = "zerohour",
                Name = "TheSuperHackers Zero Hour Game Code",
                PublisherId = PublisherTypeConstants.TheSuperHackers,
                ContentType = ContentType.GameClient.ToString(),
                Variants =
                [
                    new CatalogBundleComponentVariantDescriptor
                    {
                        Axis = "game-type",
                        Label = "Zero Hour",
                        CatalogId = "1.20260814.thesuperhackers.gameclient.zerohour-game-type-zero-hour",
                        IsDefault = true,
                    },
                    new CatalogBundleComponentVariantDescriptor
                    {
                        Axis = "game-type",
                        Label = "Generals",
                        CatalogId = "1.20260814.thesuperhackers.gameclient.zerohour-game-type-generals",
                        IsDefault = false,
                    },
                ],
            },
        };

        var bundleResult = new ContentSearchResult
        {
            Id = "1.20260731.thesuperhackers.contentbundle.bundlethesuperhackerslateststack",
            Name = "TheSuperHackers Latest Stack",
            ContentType = ContentType.ContentBundle,
            TargetGame = GameType.ZeroHour,
            ProviderName = "TheSuperHackers",
        };
        bundleResult.ResolverMetadata[CatalogConstants.BundleComponentsJsonMetadataKey] =
            JsonSerializer.Serialize(descriptors);

        // act
        var components = BundleComponentViewModel.CreateFromSearchResult(bundleResult);

        // assert
        Assert.Single(components);
        var component = components[0];
        Assert.Equal(2, component.Variants.Count);

        component.SelectedVariant = component.Variants[0]; // Zero Hour
        var zhSearchResult = component.GetSelectedSearchResult();
        Assert.NotNull(zhSearchResult);
        Assert.Equal(GameType.ZeroHour, zhSearchResult.TargetGame);

        component.SelectedVariant = component.Variants[1]; // Generals
        var genSearchResult = component.GetSelectedSearchResult();
        Assert.NotNull(genSearchResult);
        Assert.Equal(GameType.Generals, genSearchResult.TargetGame);
    }

    /// <summary>
    /// Verifies that ContentStateService matches a SuperHackers Zero Hour manifest by catalog content ID and target game.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ContentStateService_SuperHackersCatalogItem_MatchesZeroHourManifestAsync()
    {
        // arrange
        var manifestPoolMock = new Mock<IContentManifestPool>();
        var zhManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.20260814.thesuperhackers.gameclient.zerohour"),
            Name = "SuperHackers - Zero Hour",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                PublisherType = PublisherTypeConstants.TheSuperHackers,
                Name = "TheSuperHackers",
            },
        };
        var genManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.20260814.thesuperhackers.gameclient.generals"),
            Name = "SuperHackers - Generals",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.Generals,
            Publisher = new PublisherInfo
            {
                PublisherType = PublisherTypeConstants.TheSuperHackers,
                Name = "TheSuperHackers",
            },
        };

        manifestPoolMock.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([genManifest, zhManifest]));
        manifestPoolMock.Setup(p => p.IsManifestAcquiredAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManifestId id, CancellationToken _) =>
                OperationResult<bool>.CreateSuccess(id.Value == zhManifest.Id.Value || id.Value == genManifest.Id.Value));

        var stateService = new ContentStateService(
            manifestPoolMock.Object,
            NullLogger<ContentStateService>.Instance);

        var searchResult = new ContentSearchResult
        {
            Id = "1.20260814.thesuperhackers.gameclient.zerohour-game-type-zero-hour",
            Name = "TheSuperHackers Zero Hour Game Code (Zero Hour)",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            ProviderName = "TheSuperHackers",
            Version = "2026.08.14",
        };
        searchResult.ResolverMetadata[CatalogConstants.CatalogContentIdMetadataKey] = "zerohour";

        // act
        var localManifestId = await stateService.GetLocalManifestIdAsync(searchResult);

        // assert
        Assert.Equal("1.20260814.thesuperhackers.gameclient.zerohour", localManifestId);
    }

    /// <summary>
    /// Verifies that ProfileContentService successfully creates a profile when SuperHackers Zero Hour client is combined with Zero Hour addons.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ProfileContentService_SuperHackersLatestStackResolution_ResolvesZeroHourProfileAsync()
    {
        // arrange
        var zhClientManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.20260814.thesuperhackers.gameclient.zerohour"),
            Name = "SuperHackers - Zero Hour",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.TheSuperHackers },
            Dependencies =
            [
                new ContentDependency
                {
                    Id = "1.0.ea.gameinstallation.zerohour",
                    DependencyType = ContentType.GameInstallation,
                    InstallBehavior = DependencyInstallBehavior.RequireExisting,
                    CompatibleGameTypes = [GameType.ZeroHour],
                },
            ],
        };

        var genToolManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.809.communityoutpost.addon.gent"),
            Name = "GenTool",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = "1.0.ea.gameinstallation.zerohour",
                    DependencyType = ContentType.GameInstallation,
                    InstallBehavior = DependencyInstallBehavior.RequireExisting,
                    CompatibleGameTypes = [GameType.ZeroHour],
                },
            ],
        };

        var controlBarManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.20260710.github.addon.lemon-controlbar-1080p"),
            Name = "Control Bar Pro Lemon Edition ZH (1080p)",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = "1.0.ea.gameinstallation.zerohour",
                    DependencyType = ContentType.GameInstallation,
                    InstallBehavior = DependencyInstallBehavior.RequireExisting,
                    CompatibleGameTypes = [GameType.ZeroHour],
                },
            ],
        };

        var hotkeysManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.20260701.communityoutpost.addon.hleg"),
            Name = "Legionnaire's Hotkeys",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = "1.0.ea.gameinstallation.zerohour",
                    DependencyType = ContentType.GameInstallation,
                    InstallBehavior = DependencyInstallBehavior.RequireExisting,
                    CompatibleGameTypes = [GameType.ZeroHour],
                },
            ],
        };

        var manifestsById = new Dictionary<string, ContentManifest>(StringComparer.OrdinalIgnoreCase)
        {
            [zhClientManifest.Id.Value] = zhClientManifest,
            [genToolManifest.Id.Value] = genToolManifest,
            [controlBarManifest.Id.Value] = controlBarManifest,
            [hotkeysManifest.Id.Value] = hotkeysManifest,
        };

        var manifestPoolMock = new Mock<IContentManifestPool>();
        manifestPoolMock
            .Setup(pool => pool.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManifestId manifestId, CancellationToken _) =>
                manifestsById.TryGetValue(manifestId.Value, out var manifest)
                    ? OperationResult<ContentManifest?>.CreateSuccess(manifest)
                    : OperationResult<ContentManifest?>.CreateSuccess(null));
        manifestPoolMock
            .Setup(pool => pool.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(manifestsById.Values));

        var dependencyResolver = new DependencyResolver(
            manifestPoolMock.Object,
            NullLogger<DependencyResolver>.Instance);

        var createdProfile = new GameProfile
        {
            Id = "new-profile-id",
            Name = "SuperHackers - Zero Hour",
        };

        var profileManagerMock = new Mock<IGameProfileManager>();
        profileManagerMock.Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(createdProfile));
        profileManagerMock.Setup(p => p.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(createdProfile));

        var testInstallation = new GameInstallation("C:\\ZH", GameInstallationType.Steam);
        testInstallation.AvailableGameClients.Add(new GameClient
        {
            Id = "1.104.steam.gameclient.zerohour",
            Name = "Steam Zero Hour",
            GameType = GameType.ZeroHour,
            ExecutablePath = "C:\\ZH\\generals.exe",
        });

        var installationServiceMock = new Mock<IGameInstallationService>();
        installationServiceMock.Setup(i => i.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([testInstallation]));

        var profileContentService = new ProfileContentService(
            profileManagerMock.Object,
            manifestPoolMock.Object,
            dependencyResolver,
            installationServiceMock.Object,
            Mock.Of<IContentOrchestrator>(),
            Mock.Of<INotificationService>(),
            NullLogger<ProfileContentService>.Instance);

        var bundleIds = new List<string>
        {
            zhClientManifest.Id.Value,
            genToolManifest.Id.Value,
            controlBarManifest.Id.Value,
            hotkeysManifest.Id.Value,
        };

        // act
        var resolutionResult = await profileContentService.CreateProfileWithContentAsync(
            "SuperHackers - Zero Hour",
            bundleIds,
            CancellationToken.None);

        // assert
        Assert.True(resolutionResult.Success);
        Assert.NotNull(resolutionResult.Data);
        Assert.Equal("SuperHackers - Zero Hour", resolutionResult.Data.Name);
    }

    /// <summary>
    /// Verifies that CatalogBundleComponentBuilder filters out incompatible game variants when building a Zero Hour bundle.
    /// </summary>
    [Fact]
    public void CatalogBundleComponentBuilder_ZeroHourBundle_FiltersOutIncompatibleGeneralsVariant()
    {
        // arrange
        var shContent = new CatalogContentItem
        {
            Id = "zerohour",
            Name = "TheSuperHackers Zero Hour Game Code",
            ContentType = ContentType.GameClient,
            PublisherType = PublisherTypeConstants.TheSuperHackers,
            TargetGame = GameType.ZeroHour,
            Releases =
            [
                new ContentRelease
                {
                    Version = "2026.07.31",
                    Artifacts =
                    [
                        new ReleaseArtifact
                        {
                            Filename = "generalszh-weekly.zip",
                            VariantAxis = "game-type",
                            Variant = "Zero Hour",
                            IsDefaultVariant = true,
                            IsPrimary = true,
                        },
                        new ReleaseArtifact
                        {
                            Filename = "generals-weekly.zip",
                            VariantAxis = "game-type",
                            Variant = "Generals",
                            IsDefaultVariant = false,
                            IsPrimary = false,
                        },
                    ],
                },
            ],
        };

        var bundleContent = new CatalogContentItem
        {
            Id = "bundle-thesuperhackers-latest-stack",
            Name = "TheSuperHackers Latest Stack",
            ContentType = ContentType.ContentBundle,
            TargetGame = GameType.ZeroHour,
        };

        var bundleRelease = new ContentRelease
        {
            Version = "2026.07.31",
            Dependencies =
            [
                new CatalogDependency
                {
                    PublisherId = PublisherTypeConstants.TheSuperHackers,
                    ContentId = "zerohour",
                    ContentType = ContentType.GameClient.ToString(),
                },
            ],
        };

        var catalog = new PublisherCatalog
        {
            Publisher = new PublisherProfile
            {
                Id = "genhub-test-publishers",
                Name = "GenHub Test Publishers",
            },
            Content = [shContent, bundleContent],
        };

        // act
        var components = CatalogBundleComponentBuilder.Build(catalog, bundleContent, bundleRelease);

        // assert
        Assert.Single(components);
        var shComponent = components[0];
        Assert.Single(shComponent.Variants);
        Assert.Equal("Zero Hour", shComponent.Variants[0].Label);
    }

    /// <summary>
    /// Verifies that ProfileContentService creates a Generals profile when adding standalone Generals game client.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ProfileContentService_SuperHackersGeneralsResolution_ResolvesGeneralsProfileAsync()
    {
        // arrange
        var genClientManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.20260814.thesuperhackers.gameclient.generals"),
            Name = "SuperHackers - Generals",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.Generals,
            Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.TheSuperHackers },
            Dependencies =
            [
                new ContentDependency
                {
                    Id = "1.0.ea.gameinstallation.generals",
                    DependencyType = ContentType.GameInstallation,
                    InstallBehavior = DependencyInstallBehavior.RequireExisting,
                    CompatibleGameTypes = [GameType.Generals],
                },
            ],
        };

        var manifestsById = new Dictionary<string, ContentManifest>(StringComparer.OrdinalIgnoreCase)
        {
            [genClientManifest.Id.Value] = genClientManifest,
        };

        var manifestPoolMock = new Mock<IContentManifestPool>();
        manifestPoolMock
            .Setup(pool => pool.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManifestId manifestId, CancellationToken _) =>
                manifestsById.TryGetValue(manifestId.Value, out var manifest)
                    ? OperationResult<ContentManifest?>.CreateSuccess(manifest)
                    : OperationResult<ContentManifest?>.CreateSuccess(null));
        manifestPoolMock
            .Setup(pool => pool.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(manifestsById.Values));

        var dependencyResolver = new DependencyResolver(
            manifestPoolMock.Object,
            NullLogger<DependencyResolver>.Instance);

        var createdProfile = new GameProfile
        {
            Id = "generals-profile-id",
            Name = "SuperHackers - Generals",
        };

        var profileManagerMock = new Mock<IGameProfileManager>();
        profileManagerMock.Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(createdProfile));
        profileManagerMock.Setup(p => p.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(createdProfile));

        var testInstallation = new GameInstallation("C:\\Generals", GameInstallationType.EaApp);
        testInstallation.AvailableGameClients.Add(new GameClient
        {
            Id = "1.108.eaapp.gameclient.generals",
            Name = "EA App Generals",
            GameType = GameType.Generals,
            ExecutablePath = "C:\\Generals\\generals.exe",
        });

        var installationServiceMock = new Mock<IGameInstallationService>();
        installationServiceMock.Setup(i => i.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([testInstallation]));

        var profileContentService = new ProfileContentService(
            profileManagerMock.Object,
            manifestPoolMock.Object,
            dependencyResolver,
            installationServiceMock.Object,
            Mock.Of<IContentOrchestrator>(),
            Mock.Of<INotificationService>(),
            NullLogger<ProfileContentService>.Instance);

        // act
        var resolutionResult = await profileContentService.CreateProfileWithContentAsync(
            "SuperHackers - Generals",
            genClientManifest.Id.Value,
            CancellationToken.None);

        // assert
        Assert.True(resolutionResult.Success);
        Assert.NotNull(resolutionResult.Data);
        Assert.Equal("SuperHackers - Generals", resolutionResult.Data.Name);
    }
}
