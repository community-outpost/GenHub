using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Unit tests for ProfileLauncherFacade launch dependency validation.
/// </summary>
public sealed class ProfileLauncherFacadeDependencyValidationTests
{
    private readonly Mock<IGameProfileManager> _profileManagerMock = new();
    private readonly Mock<IGameLauncher> _gameLauncherMock = new();
    private readonly Mock<IWorkspaceManager> _workspaceManagerMock = new();
    private readonly Mock<ILaunchRegistry> _launchRegistryMock = new();
    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();
    private readonly Mock<IGameInstallationService> _installationServiceMock = new();
    private readonly Mock<IDependencyResolver> _dependencyResolverMock = new();
    private readonly Mock<ICasService> _casServiceMock = new();
    private readonly Mock<IGameSettingsService> _gameSettingsServiceMock = new();
    private readonly Mock<IStorageLocationService> _storageLocationServiceMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly Mock<IPublisherReconcilerRegistry> _reconcilerRegistryMock = new();
    private readonly Mock<IConfigurationProviderService> _configurationProviderMock = new();
    private readonly Mock<IGameProcessManager> _gameProcessManagerMock = new();
    private readonly Mock<ISymlinkCapabilityProvider> _symlinkCapabilityMock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileLauncherFacadeDependencyValidationTests"/> class.
    /// </summary>
    public ProfileLauncherFacadeDependencyValidationTests()
    {
        _casServiceMock
            .Setup(c => c.GetStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CasStats { ObjectCount = 10, TotalSize = 1024, SpaceSaved = 1024 });
    }

    /// <summary>
    /// Verifies that a profile configured with all required dependencies passes launch validation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateLaunchAsync_CommunityOutpostBundleProfile_PassesValidationAsync()
    {
        // Arrange
        const string profileId = "profile-community-outpost-stack";
        const string installationManifestId = "1.104.steam.gameinstallation.zerohour";
        const string clientManifestId = "1.0.communityoutpost.gameclient.communityoutpostgameclientcommunitypatch";
        const string gentoolManifestId = "1.0.communityoutpost.addon.gentool89suite";
        const string indicatorsManifestId = "1.10.communityoutpost.addon.hlenenglish";
        const string hotkeysManifestId = "1.0.communityoutpost.addon.legionnaireshotkeys";

        var installationManifest = new ContentManifest
        {
            Id = ManifestId.Create(installationManifestId),
            Name = "Steam Zero Hour",
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.ZeroHour,
        };

        var clientManifest = new ContentManifest
        {
            Id = ManifestId.Create(clientManifestId),
            Name = "Community Outpost Game Client (Community Patch)",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create("1.104.any.gameinstallation.zerohour"),
                    Name = "Zero Hour",
                    DependencyType = ContentType.GameInstallation,
                    InstallBehavior = DependencyInstallBehavior.RequireExisting,
                    IsOptional = false,
                },
            ],
        };

        var gentoolManifest = new ContentManifest
        {
            Id = ManifestId.Create(gentoolManifestId),
            Name = "GenTool 8.9 Suite",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
            Metadata = new ContentMetadata { Tags = ["contentCode:gent"] },
        };

        var indicatorsManifest = new ContentManifest
        {
            Id = ManifestId.Create(indicatorsManifestId),
            Name = "Leikeze/Legionnaire Hotkeys Indicators (English)",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
            Metadata = new ContentMetadata { Tags = ["contentCode:hlen"] },
        };

        var hotkeysManifest = new ContentManifest
        {
            Id = ManifestId.Create(hotkeysManifestId),
            Name = "Legionnaire's Hotkeys",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
            Metadata = new ContentMetadata { Tags = ["contentCode:hleg"] },
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create("1.1.communityoutpost.addon.hlen"),
                    Name = "Leikeze/Legionnaire Hotkeys Indicators (provides visual overlay icons)",
                    DependencyType = ContentType.Addon,
                    InstallBehavior = DependencyInstallBehavior.AutoInstall,
                    IsOptional = false,
                },
                new ContentDependency
                {
                    Id = ManifestId.Create("1.1.communityoutpost.addon.gent"),
                    Name = "GenTool (required for Legionnaire's Hotkeys)",
                    DependencyType = ContentType.Addon,
                    InstallBehavior = DependencyInstallBehavior.AutoInstall,
                    IsOptional = false,
                },
            ],
        };

        var profile = new GameProfile
        {
            Id = profileId,
            Name = "Community Outpost Stack",
            GameInstallationId = "steam-zh-install-id",
            GameClient = new GameClient
            {
                Id = clientManifestId,
                Name = clientManifest.Name,
                GameType = GameType.ZeroHour,
                InstallationId = "steam-zh-install-id",
            },
            EnabledContentIds =
            [
                installationManifestId,
                clientManifestId,
                gentoolManifestId,
                indicatorsManifestId,
                hotkeysManifestId,
            ],
        };

        _profileManagerMock
            .Setup(p => p.GetProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _manifestPoolMock
            .Setup(m => m.GetManifestAsync(ManifestId.Create(installationManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(installationManifest));
        _manifestPoolMock
            .Setup(m => m.GetManifestAsync(ManifestId.Create(clientManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(clientManifest));
        _manifestPoolMock
            .Setup(m => m.GetManifestAsync(ManifestId.Create(gentoolManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(gentoolManifest));
        _manifestPoolMock
            .Setup(m => m.GetManifestAsync(ManifestId.Create(indicatorsManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(indicatorsManifest));
        _manifestPoolMock
            .Setup(m => m.GetManifestAsync(ManifestId.Create(hotkeysManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(hotkeysManifest));

        var facade = CreateFacade();

        // Act
        var result = await facade.ValidateLaunchAsync(profileId);

        // Assert
        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    /// <summary>
    /// Verifies that a profile missing required dependencies fails launch validation with descriptive error messages.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateLaunchAsync_MissingRequiredCommunityOutpostDependency_FailsValidationAsync()
    {
        // Arrange
        const string profileId = "profile-missing-dependencies";
        const string installationManifestId = "1.104.steam.gameinstallation.zerohour";
        const string clientManifestId = "1.0.communityoutpost.gameclient.communityoutpostgameclientcommunitypatch";
        const string hotkeysManifestId = "1.0.communityoutpost.addon.legionnaireshotkeys";

        var installationManifest = new ContentManifest
        {
            Id = ManifestId.Create(installationManifestId),
            Name = "Steam Zero Hour",
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.ZeroHour,
        };

        var clientManifest = new ContentManifest
        {
            Id = ManifestId.Create(clientManifestId),
            Name = "Community Patch",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
        };

        var hotkeysManifest = new ContentManifest
        {
            Id = ManifestId.Create(hotkeysManifestId),
            Name = "Legionnaire's Hotkeys",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
            Metadata = new ContentMetadata { Tags = ["contentCode:hleg"] },
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create("1.1.communityoutpost.addon.hlen"),
                    Name = "Leikeze/Legionnaire Hotkeys Indicators (provides visual overlay icons)",
                    DependencyType = ContentType.Addon,
                    InstallBehavior = DependencyInstallBehavior.AutoInstall,
                    IsOptional = false,
                },
                new ContentDependency
                {
                    Id = ManifestId.Create("1.1.communityoutpost.addon.gent"),
                    Name = "GenTool (required for Legionnaire's Hotkeys)",
                    DependencyType = ContentType.Addon,
                    InstallBehavior = DependencyInstallBehavior.AutoInstall,
                    IsOptional = false,
                },
            ],
        };

        var profile = new GameProfile
        {
            Id = profileId,
            Name = "Missing Deps Profile",
            GameInstallationId = "steam-zh-install-id",
            GameClient = new GameClient
            {
                Id = clientManifestId,
                Name = clientManifest.Name,
                GameType = GameType.ZeroHour,
                InstallationId = "steam-zh-install-id",
            },
            EnabledContentIds =
            [
                installationManifestId,
                clientManifestId,
                hotkeysManifestId,
            ],
        };

        _profileManagerMock
            .Setup(p => p.GetProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _manifestPoolMock
            .Setup(m => m.GetManifestAsync(ManifestId.Create(installationManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(installationManifest));
        _manifestPoolMock
            .Setup(m => m.GetManifestAsync(ManifestId.Create(clientManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(clientManifest));
        _manifestPoolMock
            .Setup(m => m.GetManifestAsync(ManifestId.Create(hotkeysManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(hotkeysManifest));

        var facade = CreateFacade();

        // Act
        var result = await facade.ValidateLaunchAsync(profileId);

        // Assert
        Assert.False(result.Success);
        Assert.Contains(result.Errors, err => err.Contains("Leikeze/Legionnaire Hotkeys Indicators"));
        Assert.Contains(result.Errors, err => err.Contains("GenTool"));
    }

    /// <summary>
    /// Verifies that when a profile has GameClient configured in its GameClient property but omitted from EnabledContentIds, launch validation resolves it and passes.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateLaunchAsync_WhenGameClientOnlyInProfileGameClientProperty_PassesValidationAsync()
    {
        // Arrange
        const string profileId = "profile-gameclient-prop-test";
        const string installationManifestId = "1.104.steam.gameinstallation.zerohour";
        const string clientManifestId = "1.104.steam.gameclient.zerohour";

        var installationManifest = new ContentManifest
        {
            Id = ManifestId.Create(installationManifestId),
            Name = "Steam Zero Hour",
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.ZeroHour,
        };

        var clientManifest = new ContentManifest
        {
            Id = ManifestId.Create(clientManifestId),
            Name = "Steam Zero Hour Client",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };

        var profile = new GameProfile
        {
            Id = profileId,
            Name = "ZH Profile",
            GameInstallationId = "steam-zh-install-id",
            GameClient = new GameClient
            {
                Id = clientManifestId,
                Name = clientManifest.Name,
                GameType = GameType.ZeroHour,
                InstallationId = "steam-zh-install-id",
            },
            EnabledContentIds =
            [
                installationManifestId,
            ],
        };

        _profileManagerMock
            .Setup(p => p.GetProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _manifestPoolMock
            .Setup(m => m.GetManifestAsync(ManifestId.Create(installationManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(installationManifest));
        _manifestPoolMock
            .Setup(m => m.GetManifestAsync(ManifestId.Create(clientManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(clientManifest));

        var facade = CreateFacade();

        // Act
        var result = await facade.ValidateLaunchAsync(profileId);

        // Assert: Validation succeeds because GameClient is resolved from profile.GameClient
        Assert.True(result.Success);
    }

    private ProfileLauncherFacade CreateFacade()
    {
        return new ProfileLauncherFacade(
            _profileManagerMock.Object,
            _gameLauncherMock.Object,
            _workspaceManagerMock.Object,
            _launchRegistryMock.Object,
            _manifestPoolMock.Object,
            _installationServiceMock.Object,
            _dependencyResolverMock.Object,
            _casServiceMock.Object,
            _gameSettingsServiceMock.Object,
            _storageLocationServiceMock.Object,
            _notificationServiceMock.Object,
            _reconcilerRegistryMock.Object,
            _configurationProviderMock.Object,
            _gameProcessManagerMock.Object,
            _symlinkCapabilityMock.Object,
            NullLogger<ProfileLauncherFacade>.Instance);
    }
}
