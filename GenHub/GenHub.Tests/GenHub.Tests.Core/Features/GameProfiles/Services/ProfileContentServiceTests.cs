using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Regression tests for profile game-foundation reconciliation.
/// </summary>
public sealed class ProfileContentServiceTests
{
    /// <summary>
    /// Verifies that an add-on with only an installation requirement preserves a compatible publisher client.
    /// </summary>
    [Fact]
    public void SelectReconciledGameClient_AddonOnlyRequirement_PreservesCompatiblePublisherClient()
    {
        // Arrange
        var publisherClient = new GameClient
        {
            Id = "1.0.thesuperhackers.gameclient.zerohour",
            Name = "SuperHackers Zero Hour",
            GameType = GameType.ZeroHour,
            ExecutablePath = "C:\\Games\\GeneralsOnlineZH_60.exe",
            InstallationId = "installation-id",
        };
        var installationClient = new GameClient
        {
            Id = "1.104.steam.gameclient.zerohour",
            Name = "Steam Zero Hour",
            GameType = GameType.ZeroHour,
            ExecutablePath = "C:\\Games\\game.dat",
            InstallationId = "installation-id",
        };

        // Act
        var result = ProfileContentService.SelectReconciledGameClient(
            requiredGameClient: null,
            currentGameClient: publisherClient,
            installationClient,
            GameType.ZeroHour,
            "installation-id");

        // Assert
        Assert.Same(publisherClient, result);
        Assert.Equal("C:\\Games\\GeneralsOnlineZH_60.exe", result.ExecutablePath);
    }

    /// <summary>
    /// Verifies that a selected dependency with a game-client requirement replaces an incompatible
    /// profile client with the required publisher client bound to the selected installation.
    /// </summary>
    [Fact]
    public void SelectReconciledGameClient_RequiredZeroHourClient_ReplacesIncompatibleGeneralsClient()
    {
        // Arrange
        var requiredClient = new ContentManifest
        {
            Id = ManifestId.Create("1.0.communityoutpost.gameclient.communitypatch"),
            Name = "Community Patch",
            Version = "1.0",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
        };
        var incompatibleClient = new GameClient
        {
            Id = "1.108.steam.gameclient.generals",
            Name = "Steam Generals",
            GameType = GameType.Generals,
            ExecutablePath = "C:\\Games\\generals.exe",
            InstallationId = "generals-installation",
        };
        var zeroHourInstallationClient = new GameClient
        {
            Id = "1.104.steam.gameclient.zerohour",
            Name = "Steam Zero Hour",
            GameType = GameType.ZeroHour,
            ExecutablePath = "C:\\Games\\game.dat",
            WorkingDirectory = "C:\\Games",
            InstallationId = "zerohour-installation",
        };

        // Act
        var result = ProfileContentService.SelectReconciledGameClient(
            requiredClient,
            incompatibleClient,
            zeroHourInstallationClient,
            GameType.ZeroHour,
            "zerohour-installation");

        // Assert
        Assert.Equal(requiredClient.Id.Value, result.Id);
        Assert.Equal(GameType.ZeroHour, result.GameType);
        Assert.Equal("zerohour-installation", result.InstallationId);
        Assert.Equal(zeroHourInstallationClient.ExecutablePath, result.ExecutablePath);
        Assert.NotEqual(incompatibleClient.Id, result.Id);
    }

    /// <summary>
    /// Reproduces the Legionnaire Hotkeys regression: GenTool was acquired as 1.10 while the
    /// downloaded Hotkeys manifest declares the legacy 1.1 identifier. The existing GenTool must
    /// satisfy the logical dependency locally, then the hotkeys must be enabled as a fourth item
    /// rather than leaving the profile at its stale three-item snapshot.
    /// </summary>
    /// <returns>A task that completes when profile reconciliation finishes.</returns>
    [Fact]
    public async Task AddContentToProfileAsync_ExistingVersionedGenTool_ReconcilesLegionnaireDependencyClosureAsync()
    {
        // Arrange
        const string hotkeysId = "1.10.communityoutpost.addon.hlegenglish";
        const string genToolId = "1.10.communityoutpost.addon.gent";
        const string declaredGenToolId = "1.1.communityoutpost.addon.gent";
        const string installationId = "1.104.steam.gameinstallation.zerohour";
        const string clientId = "1.0.communityoutpost.gameclient.communitypatch";

        var hotkeys = new ContentManifest
        {
            Id = ManifestId.Create(hotkeysId),
            Name = "Legionnaire's Hotkeys",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
            Metadata = new ContentMetadata { Tags = ["contentCode:hleg"] },
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create(declaredGenToolId),
                    Name = "GenTool (required for Legionnaire's Hotkeys)",
                    DependencyType = ContentType.Addon,
                    InstallBehavior = DependencyInstallBehavior.AutoInstall,
                },
            ],
        };
        var genTool = new ContentManifest
        {
            Id = ManifestId.Create(genToolId),
            Name = "GenTool",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
            Metadata = new ContentMetadata { Tags = ["contentCode:gent"] },
        };
        var installation = new ContentManifest
        {
            Id = ManifestId.Create(installationId),
            Name = "Zero Hour Installation",
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.ZeroHour,
        };
        var client = new ContentManifest
        {
            Id = ManifestId.Create(clientId),
            Name = "Community Patch",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
        };
        var profile = new GameProfile
        {
            Id = "profile-id",
            Name = "Community Patch",
            GameClient = new GameClient { Id = clientId, GameType = GameType.ZeroHour },
            EnabledContentIds = [installationId, clientId, genToolId],
        };
        var manifests = new Dictionary<string, ContentManifest>(StringComparer.OrdinalIgnoreCase)
        {
            [hotkeysId] = hotkeys,
            [genToolId] = genTool,
            [installationId] = installation,
            [clientId] = client,
        };
        var profileManager = new Mock<IGameProfileManager>();
        var manifestPool = new Mock<IContentManifestPool>();
        var installationService = new Mock<IGameInstallationService>();
        var contentOrchestrator = new Mock<IContentOrchestrator>();
        var notifications = new Mock<INotificationService>();
        UpdateProfileRequest? updateRequest = null;

        profileManager
            .Setup(manager => manager.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        profileManager
            .Setup(manager => manager.UpdateProfileAsync(profile.Id, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, UpdateProfileRequest request, CancellationToken _) =>
            {
                updateRequest = request;
                return ProfileOperationResult<GameProfile>.CreateSuccess(profile);
            });
        manifestPool
            .Setup(pool => pool.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManifestId manifestId, CancellationToken _) =>
                manifests.TryGetValue(manifestId.Value, out var manifest)
                    ? OperationResult<ContentManifest?>.CreateSuccess(manifest)
                    : OperationResult<ContentManifest?>.CreateSuccess(null));
        manifestPool
            .Setup(pool => pool.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(manifests.Values));
        var service = new ProfileContentService(
            profileManager.Object,
            manifestPool.Object,
            new DependencyResolver(
                manifestPool.Object,
                NullLogger<DependencyResolver>.Instance),
            installationService.Object,
            contentOrchestrator.Object,
            notifications.Object,
            NullLogger<ProfileContentService>.Instance);

        // Act
        var result = await service.AddContentToProfileAsync(profile.Id, hotkeysId);

        // Assert
        Assert.True(result.Success, result.FirstError);
        var savedRequest = Assert.IsType<UpdateProfileRequest>(updateRequest);
        var savedContentIds = Assert.IsType<List<string>>(savedRequest.EnabledContentIds);
        Assert.Equal(4, savedContentIds.Count);
        Assert.Contains(genToolId, savedContentIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(hotkeysId, savedContentIds, StringComparer.OrdinalIgnoreCase);
        contentOrchestrator.Verify(
            orchestrator => orchestrator.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that missing base dependencies (such as hlen) are auto-acquired directly using the
    /// authoritative GenPatcher files endpoint (https://legi.cc/gp2/f/{code}.dat) rather than the HTML patch page.
    /// </summary>
    /// <returns>A task that completes when the operation finishes.</returns>
    [Fact]
    public async Task AddContentToProfileAsync_MissingBaseDependency_AcquiresFromGenPatcherFilesEndpointAsync()
    {
        // Arrange
        const string hotkeysId = "1.0.communityoutpost.addon.hleg";
        const string hlenId = "1.0.communityoutpost.addon.hlen";
        const string installationId = "1.104.steam.gameinstallation.zerohour";
        const string clientId = "1.0.communityoutpost.gameclient.communitypatch";

        var hotkeys = new ContentManifest
        {
            Id = ManifestId.Create(hotkeysId),
            Name = "Legionnaire's Hotkeys",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
            Metadata = new ContentMetadata { Tags = ["contentCode:hleg"] },
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create(hlenId),
                    Name = "Hotkeys Indicators",
                    DependencyType = ContentType.Addon,
                    InstallBehavior = DependencyInstallBehavior.AutoInstall,
                },
            ],
        };
        var hlenAcquired = new ContentManifest
        {
            Id = ManifestId.Create("1.10.communityoutpost.addon.hlenenglish"),
            Name = "Hotkeys Indicators (Leikeze/Legionnaire)",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
            Metadata = new ContentMetadata { Tags = ["contentCode:hlen"] },
        };
        var installation = new ContentManifest
        {
            Id = ManifestId.Create(installationId),
            Name = "Zero Hour Installation",
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.ZeroHour,
        };
        var client = new ContentManifest
        {
            Id = ManifestId.Create(clientId),
            Name = "Community Patch",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
        };
        var profile = new GameProfile
        {
            Id = "profile-id",
            Name = "Community Patch",
            GameClient = new GameClient { Id = clientId, GameType = GameType.ZeroHour },
            EnabledContentIds = [installationId, clientId],
        };
        var manifests = new Dictionary<string, ContentManifest>(StringComparer.OrdinalIgnoreCase)
        {
            [hotkeysId] = hotkeys,
            [installationId] = installation,
            [clientId] = client,
        };

        var profileManager = new Mock<IGameProfileManager>();
        var manifestPool = new Mock<IContentManifestPool>();
        var installationService = new Mock<IGameInstallationService>();
        var contentOrchestrator = new Mock<IContentOrchestrator>();
        var notifications = new Mock<INotificationService>();
        ContentSearchResult? acquiredSearchResult = null;

        profileManager
            .Setup(manager => manager.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        profileManager
            .Setup(manager => manager.UpdateProfileAsync(profile.Id, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, UpdateProfileRequest request, CancellationToken _) => ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        manifestPool
            .Setup(pool => pool.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManifestId manifestId, CancellationToken _) =>
                manifests.TryGetValue(manifestId.Value, out var manifest)
                    ? OperationResult<ContentManifest?>.CreateSuccess(manifest)
                    : OperationResult<ContentManifest?>.CreateSuccess(null));
        manifestPool
            .Setup(pool => pool.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(manifests.Values));

        contentOrchestrator
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentSearchResult searchResult, IProgress<ContentAcquisitionProgress>? _, CancellationToken _) =>
            {
                acquiredSearchResult = searchResult;
                manifests[hlenAcquired.Id.Value] = hlenAcquired;
                return OperationResult<ContentManifest>.CreateSuccess(hlenAcquired);
            });

        var service = new ProfileContentService(
            profileManager.Object,
            manifestPool.Object,
            new DependencyResolver(
                manifestPool.Object,
                NullLogger<DependencyResolver>.Instance),
            installationService.Object,
            contentOrchestrator.Object,
            notifications.Object,
            NullLogger<ProfileContentService>.Instance);

        // Act
        var result = await service.AddContentToProfileAsync(profile.Id, hotkeysId);

        // Assert
        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(acquiredSearchResult);
        Assert.Equal("https://legi.cc/gp2/f/hlen.dat", acquiredSearchResult.SourceUrl);
    }
}
