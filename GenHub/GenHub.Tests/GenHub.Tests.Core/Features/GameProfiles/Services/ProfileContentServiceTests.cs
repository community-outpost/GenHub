using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
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
            new ProfileContentResolutionServices(
                new DependencyResolver(
                    manifestPool.Object,
                    NullLogger<DependencyResolver>.Instance),
                installationService.Object,
                new ContentDependencyValidator(
                    manifestPool.Object,
                    NullLogger<ContentDependencyValidator>.Instance)),
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
            new ProfileContentResolutionServices(
                new DependencyResolver(
                    manifestPool.Object,
                    NullLogger<DependencyResolver>.Instance),
                installationService.Object,
                new ContentDependencyValidator(
                    manifestPool.Object,
                    NullLogger<ContentDependencyValidator>.Instance)),
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

    /// <summary>
    /// Verifies that reconciling a profile built on an installation whose client reports no version
    /// re-anchors it on the manifest id registration actually pools. Reconciliation strips the
    /// existing GameInstallation id and mints a replacement, so a raw empty version here rewrites
    /// the profile onto an id that resolves to no manifest and the profile stops launching.
    /// </summary>
    /// <returns>A task that completes when the operation finishes.</returns>
    [Fact]
    public async Task AddContentToProfileAsync_VersionLessInstallation_KeepsAResolvableInstallationIdAsync()
    {
        var gameInstallation = new GameInstallation("C:\\custom", GameInstallationType.Custom)
        {
            AvailableGameClients =
            [
                new GameClient { Id = "client-id", GameType = GameType.ZeroHour, Version = string.Empty },
            ],
        };
        var expectedInstallationId = ManifestIdGenerator.GenerateGameInstallationId(
            gameInstallation,
            GameType.ZeroHour,
            GameVersionHelper.GetDefaultManifestVersion(GameType.ZeroHour));
        var deadInstallationId = ManifestIdGenerator.GenerateGameInstallationId(
            gameInstallation, GameType.ZeroHour, "0");

        const string addonId = "1.0.communityoutpost.addon.testaddon";
        var addon = new ContentManifest
        {
            Id = ManifestId.Create(addonId),
            Name = "Test Addon",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create(expectedInstallationId),
                    Name = "Zero Hour Installation",
                    DependencyType = ContentType.GameInstallation,
                    InstallBehavior = DependencyInstallBehavior.RequireExisting,
                    CompatibleGameTypes = [GameType.ZeroHour],
                },
            ],
        };
        var installationManifest = new ContentManifest
        {
            Id = ManifestId.Create(expectedInstallationId),
            Name = "Custom Zero Hour Installation",
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.ZeroHour,
        };
        var profile = new GameProfile
        {
            Id = "profile-id",
            Name = "Custom",
            GameClient = new GameClient { Id = "client-id", GameType = GameType.ZeroHour },
            EnabledContentIds = [expectedInstallationId],
        };
        var manifests = new Dictionary<string, ContentManifest>(StringComparer.OrdinalIgnoreCase)
        {
            [addonId] = addon,
            [expectedInstallationId] = installationManifest,
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
        installationService
            .Setup(service => service.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([gameInstallation]));

        var service = new ProfileContentService(
            profileManager.Object,
            manifestPool.Object,
            new ProfileContentResolutionServices(
                new DependencyResolver(
                    manifestPool.Object,
                    NullLogger<DependencyResolver>.Instance),
                installationService.Object,
                new ContentDependencyValidator(
                    manifestPool.Object,
                    NullLogger<ContentDependencyValidator>.Instance)),
            contentOrchestrator.Object,
            notifications.Object,
            NullLogger<ProfileContentService>.Instance);

        var result = await service.AddContentToProfileAsync(profile.Id, addonId);

        Assert.True(result.Success, result.FirstError);
        var savedRequest = Assert.IsType<UpdateProfileRequest>(updateRequest);
        var savedContentIds = Assert.IsType<List<string>>(savedRequest.EnabledContentIds);
        Assert.Contains(expectedInstallationId, savedContentIds, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(deadInstallationId, savedContentIds, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reproduces issue 686: Control Bar Pro requires GenTool as RequireExisting with StrictPublisher
    /// false. Adding it from Downloads without GenTool must report GenTool by name and save nothing.
    /// </summary>
    /// <returns>A task that completes when the operation finishes.</returns>
    [Fact]
    public async Task AddContentToProfileAsync_ControlBarProWithoutGenTool_ReportsGenToolAndSavesNothingAsync()
    {
        var fixture = new ControlBarProFixture();

        var result = await fixture.Service.AddContentToProfileAsync(fixture.Profile.Id, ControlBarProFixture.ControlBarProId);

        Assert.False(result.Success);
        var gap = Assert.Single(result.MissingDependencies);
        Assert.Equal("GenTool", gap.DisplayName);
        Assert.True(gap.CanDownload);
        Assert.Contains("GenTool", result.FirstError, StringComparison.Ordinal);
        Assert.DoesNotContain("communityoutpost", result.FirstError, StringComparison.OrdinalIgnoreCase);
        fixture.ProfileManager.Verify(
            manager => manager.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Creating a profile from Downloads applies the same rule and creates nothing while GenTool is missing.
    /// </summary>
    /// <returns>A task that completes when the operation finishes.</returns>
    [Fact]
    public async Task CreateProfileWithContentAsync_ControlBarProWithoutGenTool_ReportsGenToolAndCreatesNothingAsync()
    {
        var fixture = new ControlBarProFixture();

        var result = await fixture.Service.CreateProfileWithContentAsync("Control Bar Pro Profile", ControlBarProFixture.ControlBarProId);

        Assert.False(result.Success);
        Assert.Equal("GenTool", Assert.Single(result.MissingDependencies).DisplayName);
        fixture.ProfileManager.Verify(
            manager => manager.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// A required item that is already downloaded is enabled with the content, so no dialog is needed.
    /// </summary>
    /// <returns>A task that completes when the operation finishes.</returns>
    [Fact]
    public async Task AddContentToProfileAsync_GenToolAlreadyDownloaded_EnablesItWithControlBarProAsync()
    {
        var fixture = new ControlBarProFixture();
        fixture.AddManifest(ControlBarProFixture.CreateAddon(ControlBarProFixture.GenToolId, "gent"));

        var result = await fixture.Service.AddContentToProfileAsync(fixture.Profile.Id, ControlBarProFixture.ControlBarProId);

        Assert.True(result.Success, result.FirstError);
        Assert.Empty(result.MissingDependencies);
        var saved = Assert.IsType<List<string>>(Assert.IsType<UpdateProfileRequest>(fixture.SavedRequest).EnabledContentIds);
        Assert.Contains(ControlBarProFixture.GenToolId, saved, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(ControlBarProFixture.ControlBarProId, saved, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Installing the missing items downloads GenTool, after which the same add succeeds and enables it.
    /// </summary>
    /// <returns>A task that completes when the operation finishes.</returns>
    [Fact]
    public async Task InstallMissingDependenciesAsync_DownloadSucceeds_ThenAddEnablesGenToolAsync()
    {
        var fixture = new ControlBarProFixture();
        var genTool = ControlBarProFixture.CreateAddon(ControlBarProFixture.GenToolId, "gent");
        fixture.Orchestrator
            .Setup(o => o.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
                [new ContentSearchResult { Id = ControlBarProFixture.GenToolId, Name = "GenTool" }]));
        fixture.Orchestrator
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                fixture.AddManifest(genTool);
                return OperationResult<ContentManifest>.CreateSuccess(genTool);
            });
        var firstAttempt = await fixture.Service.AddContentToProfileAsync(fixture.Profile.Id, ControlBarProFixture.ControlBarProId);

        var install = await fixture.Service.InstallMissingDependenciesAsync(firstAttempt.MissingDependencies);
        var secondAttempt = await fixture.Service.AddContentToProfileAsync(fixture.Profile.Id, ControlBarProFixture.ControlBarProId);

        Assert.True(install.Success, install.FirstError);
        Assert.Equal([ControlBarProFixture.GenToolId], install.Data);
        Assert.True(secondAttempt.Success, secondAttempt.FirstError);
        var saved = Assert.IsType<List<string>>(Assert.IsType<UpdateProfileRequest>(fixture.SavedRequest).EnabledContentIds);
        Assert.Contains(ControlBarProFixture.GenToolId, saved, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A failed download reports the item by name and leaves the profile untouched.
    /// </summary>
    /// <returns>A task that completes when the operation finishes.</returns>
    [Fact]
    public async Task InstallMissingDependenciesAsync_DownloadFails_ReportsItemByNameAsync()
    {
        var fixture = new ControlBarProFixture();
        fixture.Orchestrator
            .Setup(o => o.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure("offline"));
        fixture.Orchestrator
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateFailure("offline"));
        var attempt = await fixture.Service.AddContentToProfileAsync(fixture.Profile.Id, ControlBarProFixture.ControlBarProId);

        var install = await fixture.Service.InstallMissingDependenciesAsync(attempt.MissingDependencies);

        Assert.False(install.Success);
        Assert.Contains("GenTool", install.FirstError, StringComparison.Ordinal);
        Assert.DoesNotContain("communityoutpost", install.FirstError, StringComparison.OrdinalIgnoreCase);
        fixture.ProfileManager.Verify(
            manager => manager.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Items GenHub cannot download are skipped by the installer instead of being attempted.
    /// </summary>
    /// <returns>A task that completes when the operation finishes.</returns>
    [Fact]
    public async Task InstallMissingDependenciesAsync_ItemCannotBeDownloaded_DoesNotAttemptItAsync()
    {
        var fixture = new ControlBarProFixture();
        ContentDependencyGap installationGap = new()
        {
            DisplayName = "Zero Hour Installation (Required)",
            DependencyId = "1.104.any.gameinstallation.zerohour",
            DependencyType = ContentType.GameInstallation,
            CanDownload = false,
            RequiredByName = "Control Bar Pro (ExiLe)",
            RequiredById = ControlBarProFixture.ControlBarProId,
        };

        var install = await fixture.Service.InstallMissingDependenciesAsync([installationGap]);

        Assert.True(install.Success);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<string>>(install.Data));
        fixture.Orchestrator.Verify(
            o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// A profile saved before the fix holds Control Bar Pro without GenTool. Adding unrelated content
    /// reports that gap. After GenTool is installed, the same add succeeds and enables GenTool.
    /// </summary>
    /// <returns>A task that completes when the operation finishes.</returns>
    [Fact]
    public async Task AddContentToProfileAsync_ExistingContentMissingGenTool_InstallThenAddEnablesGenToolAsync()
    {
        var fixture = new ControlBarProFixture();
        fixture.EnableInProfile(ControlBarProFixture.ControlBarProId);
        fixture.AddManifest(ControlBarProFixture.CreateMapPack());
        var genTool = ControlBarProFixture.CreateAddon(ControlBarProFixture.GenToolId, "gent");
        fixture.Orchestrator
            .Setup(o => o.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
                [new ContentSearchResult { Id = ControlBarProFixture.GenToolId, Name = "GenTool" }]));
        fixture.Orchestrator
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                fixture.AddManifest(genTool);
                return OperationResult<ContentManifest>.CreateSuccess(genTool);
            });

        var firstAttempt = await fixture.Service.AddContentToProfileAsync(fixture.Profile.Id, ControlBarProFixture.MapPackId);
        var gap = Assert.Single(firstAttempt.MissingDependencies);
        Assert.Equal(ControlBarProFixture.ControlBarProId, gap.RequiredById);
        var install = await fixture.Service.InstallMissingDependenciesAsync(firstAttempt.MissingDependencies);
        var secondAttempt = await fixture.Service.AddContentToProfileAsync(fixture.Profile.Id, ControlBarProFixture.MapPackId);

        Assert.True(install.Success, install.FirstError);
        Assert.True(secondAttempt.Success, secondAttempt.FirstError);
        var saved = Assert.IsType<List<string>>(Assert.IsType<UpdateProfileRequest>(fixture.SavedRequest).EnabledContentIds);
        Assert.Contains(ControlBarProFixture.GenToolId, saved, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(ControlBarProFixture.MapPackId, saved, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// When content already in the profile needs an item that is downloaded but not enabled,
    /// the next add enables it without downloading anything.
    /// </summary>
    /// <returns>A task that completes when the operation finishes.</returns>
    [Fact]
    public async Task AddContentToProfileAsync_ExistingContentGapAlreadyDownloaded_EnablesItWithoutDownloadAsync()
    {
        var fixture = new ControlBarProFixture();
        fixture.EnableInProfile(ControlBarProFixture.ControlBarProId);
        fixture.AddManifest(ControlBarProFixture.CreateMapPack());
        fixture.AddManifest(ControlBarProFixture.CreateAddon(ControlBarProFixture.GenToolId, "gent"));

        var result = await fixture.Service.AddContentToProfileAsync(fixture.Profile.Id, ControlBarProFixture.MapPackId);

        Assert.True(result.Success, result.FirstError);
        var saved = Assert.IsType<List<string>>(Assert.IsType<UpdateProfileRequest>(fixture.SavedRequest).EnabledContentIds);
        Assert.Contains(ControlBarProFixture.GenToolId, saved, StringComparer.OrdinalIgnoreCase);
        fixture.Orchestrator.Verify(
            o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// A non-strict dependency is satisfied locally by another publisher's acquired manifest,
    /// the same way the validator accepts it, so the add enables it without a dialog.
    /// </summary>
    /// <returns>A task that completes when the operation finishes.</returns>
    [Fact]
    public async Task AddContentToProfileAsync_NonStrictDependencyFromOtherPublisher_EnablesLocalManifestAsync()
    {
        var fixture = new ControlBarProFixture();
        const string modId = "1.0.publishera.mod.bigmod";
        const string widgetId = "1.0.publisherb.addon.widget";
        fixture.AddManifest(new ContentManifest
        {
            Id = ManifestId.Create(modId),
            Name = "Big Mod",
            ContentType = ContentType.Mod,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create("1.0.publishera.addon.widget"),
                    Name = "Widget",
                    DependencyType = ContentType.Addon,
                    InstallBehavior = DependencyInstallBehavior.RequireExisting,
                },
            ],
        });
        fixture.AddManifest(new ContentManifest { Id = ManifestId.Create(widgetId), Name = "Widget", ContentType = ContentType.Addon });

        var result = await fixture.Service.AddContentToProfileAsync(fixture.Profile.Id, modId);

        Assert.True(result.Success, result.FirstError);
        var saved = Assert.IsType<List<string>>(Assert.IsType<UpdateProfileRequest>(fixture.SavedRequest).EnabledContentIds);
        Assert.Contains(widgetId, saved, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Installing an item also installs the items it requires, so one click is enough.
    /// </summary>
    /// <returns>A task that completes when the operation finishes.</returns>
    [Fact]
    public async Task InstallMissingDependenciesAsync_InstalledItemNeedsMore_InstallsThoseTooAsync()
    {
        var fixture = new ControlBarProFixture();
        const string indicatorsId = "1.10.communityoutpost.addon.hlenenglish";
        var genTool = ControlBarProFixture.CreateAddon(ControlBarProFixture.GenToolId, "gent");
        genTool.Dependencies =
        [
            new ContentDependency
            {
                Id = ManifestId.Create("1.1.communityoutpost.addon.hlen"),
                Name = "Hotkeys Indicators",
                DependencyType = ContentType.Addon,
                InstallBehavior = DependencyInstallBehavior.AutoInstall,
            },
        ];
        var indicators = ControlBarProFixture.CreateAddon(indicatorsId, "hlen");
        fixture.Orchestrator
            .Setup(o => o.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
                [new ContentSearchResult { Id = ControlBarProFixture.GenToolId, Name = "GenTool" }]));
        fixture.Orchestrator
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentSearchResult hit, IProgress<ContentAcquisitionProgress>? _, CancellationToken _) =>
            {
                var acquired = hit.Id!.EndsWith(".gent", StringComparison.OrdinalIgnoreCase) ? genTool : indicators;
                fixture.AddManifest(acquired);
                return OperationResult<ContentManifest>.CreateSuccess(acquired);
            });
        var attempt = await fixture.Service.AddContentToProfileAsync(fixture.Profile.Id, ControlBarProFixture.ControlBarProId);

        var install = await fixture.Service.InstallMissingDependenciesAsync(attempt.MissingDependencies);

        Assert.True(install.Success, install.FirstError);
        Assert.Contains(ControlBarProFixture.GenToolId, install.Data!);
        Assert.Contains(indicatorsId, install.Data!);
    }

    /// <summary>
    /// An acquired manifest with the exact declared ID but the wrong version is skipped in favor
    /// of a compatible acquired version of the same content.
    /// </summary>
    /// <returns>A task that completes when the operation finishes.</returns>
    [Fact]
    public async Task AddContentToProfileAsync_ExactIdWrongVersion_EnablesCompatibleVersionAsync()
    {
        var fixture = new ControlBarProFixture();
        const string modId = "1.0.examplepublisher.mod.bigmod";
        const string oldPackId = "1.0.examplepublisher.addon.basepack";
        const string newPackId = "1.5.examplepublisher.addon.basepack";
        fixture.AddManifest(new ContentManifest
        {
            Id = ManifestId.Create(modId),
            Name = "Big Mod",
            ContentType = ContentType.Mod,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create(oldPackId),
                    Name = "Base Pack",
                    DependencyType = ContentType.Addon,
                    InstallBehavior = DependencyInstallBehavior.RequireExisting,
                    MinVersion = "2.0",
                },
            ],
        });
        fixture.AddManifest(new ContentManifest { Id = ManifestId.Create(oldPackId), Name = "Base Pack", Version = "1.0", ContentType = ContentType.Addon });
        fixture.AddManifest(new ContentManifest { Id = ManifestId.Create(newPackId), Name = "Base Pack", Version = "2.5", ContentType = ContentType.Addon });

        var result = await fixture.Service.AddContentToProfileAsync(fixture.Profile.Id, modId);

        Assert.True(result.Success, result.FirstError);
        var saved = Assert.IsType<List<string>>(Assert.IsType<UpdateProfileRequest>(fixture.SavedRequest).EnabledContentIds);
        Assert.Contains(newPackId, saved, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(oldPackId, saved, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// When an item that an installed item needs cannot be downloaded, the install fails and names it.
    /// </summary>
    /// <returns>A task that completes when the operation finishes.</returns>
    [Fact]
    public async Task InstallMissingDependenciesAsync_RequiredItemOfInstalledItemFails_ReportsFailureAsync()
    {
        var fixture = new ControlBarProFixture();
        var genTool = ControlBarProFixture.CreateAddon(ControlBarProFixture.GenToolId, "gent");
        genTool.Dependencies =
        [
            new ContentDependency
            {
                Id = ManifestId.Create("1.1.communityoutpost.addon.hlen"),
                Name = "Hotkeys Indicators",
                DependencyType = ContentType.Addon,
                InstallBehavior = DependencyInstallBehavior.AutoInstall,
            },
        ];
        fixture.Orchestrator
            .Setup(o => o.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
                [new ContentSearchResult { Id = ControlBarProFixture.GenToolId, Name = "GenTool" }]));
        fixture.Orchestrator
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentSearchResult hit, IProgress<ContentAcquisitionProgress>? _, CancellationToken _) =>
            {
                if (!hit.Id!.EndsWith(".gent", StringComparison.OrdinalIgnoreCase))
                {
                    return OperationResult<ContentManifest>.CreateFailure("offline");
                }

                fixture.AddManifest(genTool);
                return OperationResult<ContentManifest>.CreateSuccess(genTool);
            });
        var attempt = await fixture.Service.AddContentToProfileAsync(fixture.Profile.Id, ControlBarProFixture.ControlBarProId);

        var install = await fixture.Service.InstallMissingDependenciesAsync(attempt.MissingDependencies);

        Assert.False(install.Success);
        Assert.Contains("Hotkeys Indicators", install.FirstError, StringComparison.Ordinal);
    }

    /// <summary>
    /// When content already in the profile is satisfied by another publisher's item, adding
    /// unrelated content does not enable a second copy of that dependency.
    /// </summary>
    /// <returns>A task that completes when the operation finishes.</returns>
    [Fact]
    public async Task AddContentToProfileAsync_ExistingDependencySatisfied_DoesNotEnableSecondCopyAsync()
    {
        var fixture = new ControlBarProFixture();
        const string modId = "1.0.publishera.mod.bigmod";
        const string exactPackId = "1.0.publishera.addon.basepack";
        const string otherPackId = "1.0.publisherb.addon.basepack";
        fixture.AddManifest(new ContentManifest
        {
            Id = ManifestId.Create(modId),
            Name = "Big Mod",
            ContentType = ContentType.Mod,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create(exactPackId),
                    Name = "Base Pack",
                    DependencyType = ContentType.Addon,
                    InstallBehavior = DependencyInstallBehavior.RequireExisting,
                },
            ],
        });
        fixture.AddManifest(new ContentManifest { Id = ManifestId.Create(exactPackId), Name = "Base Pack", ContentType = ContentType.Addon });
        fixture.AddManifest(new ContentManifest { Id = ManifestId.Create(otherPackId), Name = "Base Pack", ContentType = ContentType.Addon });
        fixture.AddManifest(ControlBarProFixture.CreateMapPack());
        fixture.EnableInProfile(modId);
        fixture.EnableInProfile(otherPackId);

        var result = await fixture.Service.AddContentToProfileAsync(fixture.Profile.Id, ControlBarProFixture.MapPackId);

        Assert.True(result.Success, result.FirstError);
        var saved = Assert.IsType<List<string>>(Assert.IsType<UpdateProfileRequest>(fixture.SavedRequest).EnabledContentIds);
        Assert.Contains(otherPackId, saved, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(exactPackId, saved, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Install honors the declared version limit. It picks the compatible acquired version over
    /// the exact-ID one, and fails when only an incompatible version is available.
    /// </summary>
    /// <param name="withCompatibleVersion">Whether a compatible version is acquired.</param>
    /// <returns>A task that completes when the operation finishes.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InstallMissingDependenciesAsync_ExactIdTooOld_HonorsVersionLimitAsync(bool withCompatibleVersion)
    {
        var fixture = new ControlBarProFixture();
        const string declaredId = "1.1.communityoutpost.addon.gent";
        const string requiringId = "1.0.communityoutpost.addon.needsnewgentool";
        fixture.AddManifest(new ContentManifest
        {
            Id = ManifestId.Create(requiringId),
            Name = "Needs New GenTool",
            ContentType = ContentType.Addon,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create(declaredId),
                    Name = "GenTool",
                    DependencyType = ContentType.Addon,
                    InstallBehavior = DependencyInstallBehavior.RequireExisting,
                    MinVersion = "2.0",
                },
            ],
        });
        var oldGenTool = ControlBarProFixture.CreateAddon(declaredId, "gent");
        oldGenTool.Version = "1.0";
        fixture.AddManifest(oldGenTool);
        if (withCompatibleVersion)
        {
            var newGenTool = ControlBarProFixture.CreateAddon(ControlBarProFixture.GenToolId, "gent");
            newGenTool.Version = "2.5";
            fixture.AddManifest(newGenTool);
        }

        var gap = new ContentDependencyGap
        {
            DisplayName = "GenTool",
            DependencyId = declaredId,
            DependencyType = ContentType.Addon,
            CanDownload = true,
            RequiredByName = "Needs New GenTool",
            RequiredById = requiringId,
        };

        var install = await fixture.Service.InstallMissingDependenciesAsync([gap]);

        Assert.Equal(withCompatibleVersion, install.Success);
        if (withCompatibleVersion)
        {
            Assert.Equal([ControlBarProFixture.GenToolId], install.Data);
        }
        else
        {
            Assert.Contains("GenTool", install.FirstError, StringComparison.Ordinal);
            Assert.Contains("not a version", install.FirstError, StringComparison.Ordinal);
            Assert.DoesNotContain("connection", install.FirstError, StringComparison.Ordinal);
        }
    }

    private sealed class ControlBarProFixture
    {
        public const string ControlBarProId = "1.10.communityoutpost.addon.cbpr";
        public const string GenToolId = "1.10.communityoutpost.addon.gent";
        public const string MapPackId = "1.0.examplepublisher.mappack.tournamentmaps";
        private const string ClientId = "1.0.communityoutpost.gameclient.communitypatch";

        private readonly Dictionary<string, ContentManifest> _manifests = new(StringComparer.OrdinalIgnoreCase);

        public ControlBarProFixture()
        {
            var installation = new GameInstallation("C:\\ZH", GameInstallationType.Steam)
            {
                AvailableGameClients =
                [
                    new GameClient { Id = ClientId, GameType = GameType.ZeroHour, Version = "1.04" },
                ],
            };
            var installationId = ManifestIdGenerator.GenerateGameInstallationId(
                installation,
                GameType.ZeroHour,
                GameVersionHelper.ResolveInstallationVersion("1.04", GameType.ZeroHour));

            AddManifest(new ContentManifest
            {
                Id = ManifestId.Create(ControlBarProId),
                Name = "Control Bar Pro (ExiLe)",
                ContentType = ContentType.Addon,
                TargetGame = GameType.ZeroHour,
                Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
                Metadata = new ContentMetadata { Tags = ["contentCode:cbpr"] },
                Dependencies = GenPatcherDependencyBuilder.GetDependencies("cbpr", GenPatcherContentRegistry.GetMetadata("cbpr")),
            });
            AddManifest(CreateAddon("1.10.communityoutpost.addon.cbpc", "cbpc"));
            AddManifest(CreateAddon("1.10.communityoutpost.addon.cben", "cben"));
            AddManifest(new ContentManifest
            {
                Id = ManifestId.Create(ClientId),
                Name = "Community Patch",
                ContentType = ContentType.GameClient,
                TargetGame = GameType.ZeroHour,
                Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
            });
            AddManifest(new ContentManifest
            {
                Id = ManifestId.Create(installationId),
                Name = "Zero Hour Installation",
                ContentType = ContentType.GameInstallation,
                TargetGame = GameType.ZeroHour,
            });

            Profile = new GameProfile
            {
                Id = "profile-id",
                Name = "TheSuperHackers Zero Hour",
                GameClient = new GameClient { Id = ClientId, GameType = GameType.ZeroHour, InstallationId = installation.Id },
                EnabledContentIds = [installationId, ClientId],
            };

            ProfileManager
                .Setup(manager => manager.GetProfileAsync(Profile.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(Profile));
            ProfileManager
                .Setup(manager => manager.UpdateProfileAsync(Profile.Id, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, UpdateProfileRequest request, CancellationToken _) =>
                {
                    SavedRequest = request;
                    return ProfileOperationResult<GameProfile>.CreateSuccess(Profile);
                });
            ProfileManager
                .Setup(manager => manager.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([Profile]));

            var manifestPool = new Mock<IContentManifestPool>();
            manifestPool
                .Setup(pool => pool.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ManifestId manifestId, CancellationToken _) =>
                    OperationResult<ContentManifest?>.CreateSuccess(_manifests.GetValueOrDefault(manifestId.Value)));
            manifestPool
                .Setup(pool => pool.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(_manifests.Values.ToList()));

            var installationService = new Mock<IGameInstallationService>();
            installationService
                .Setup(service => service.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

            Service = new ProfileContentService(
                ProfileManager.Object,
                manifestPool.Object,
                new ProfileContentResolutionServices(
                    new DependencyResolver(manifestPool.Object, NullLogger<DependencyResolver>.Instance),
                    installationService.Object,
                    new ContentDependencyValidator(manifestPool.Object, NullLogger<ContentDependencyValidator>.Instance)),
                Orchestrator.Object,
                Mock.Of<INotificationService>(),
                NullLogger<ProfileContentService>.Instance);
        }

        public Mock<IGameProfileManager> ProfileManager { get; } = new();

        public Mock<IContentOrchestrator> Orchestrator { get; } = new();

        public GameProfile Profile { get; }

        public ProfileContentService Service { get; }

        public UpdateProfileRequest? SavedRequest { get; private set; }

        public static ContentManifest CreateAddon(string id, string contentCode) => new()
        {
            Id = ManifestId.Create(id),
            Name = GenPatcherContentRegistry.GetMetadata(contentCode).DisplayName,
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
            Metadata = new ContentMetadata { Tags = [$"contentCode:{contentCode}"] },
        };

        public static ContentManifest CreateMapPack() => new()
        {
            Id = ManifestId.Create(MapPackId),
            Name = "Tournament Maps",
            ContentType = ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
        };

        public void AddManifest(ContentManifest manifest) => _manifests[manifest.Id.Value] = manifest;

        public void EnableInProfile(string manifestId) => Profile.EnabledContentIds.Add(manifestId);
    }
}
