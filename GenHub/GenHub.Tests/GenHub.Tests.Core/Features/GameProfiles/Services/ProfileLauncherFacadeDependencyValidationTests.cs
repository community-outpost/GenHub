using CommunityToolkit.Mvvm.Messaging;
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
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.Launching;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>
    /// Verifies that when an installed dependency candidate matches by identity but fails version constraints, launch validation fails with a version mismatch error.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateLaunchAsync_WhenDependencyVersionIncompatible_FailsValidationAsync()
    {
        // Arrange
        const string profileId = "profile-version-incompatible";
        const string installationManifestId = "1.104.steam.gameinstallation.zerohour";
        const string clientManifestId = "1.104.steam.gameclient.zerohour";
        const string modManifestId = "1.0.ea.mod.samplemod";
        const string depManifestId = "1.0.ea.addon.sampleaddon";

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

        var modManifest = new ContentManifest
        {
            Id = ManifestId.Create(modManifestId),
            Name = "Sample Mod",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create("1.0.ea.addon.sampleaddon"),
                    Name = "Sample Addon",
                    DependencyType = ContentType.Addon,
                    MinVersion = "2.0.0",
                    IsOptional = false,
                },
            ],
        };

        var depManifest = new ContentManifest
        {
            Id = ManifestId.Create(depManifestId),
            Name = "Sample Addon",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Version = "1.0.0",
        };

        var profile = new GameProfile
        {
            Id = profileId,
            Name = "Version Incompatible Profile",
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
                modManifestId,
                depManifestId,
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
            .Setup(m => m.GetManifestAsync(ManifestId.Create(modManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(modManifest));
        _manifestPoolMock
            .Setup(m => m.GetManifestAsync(ManifestId.Create(depManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(depManifest));

        var facade = CreateFacade();

        // Act
        var result = await facade.ValidateLaunchAsync(profileId);

        // Assert: Validation must fail because installed version 1.0.0 is less than min version 2.0.0
        Assert.False(result.Success);
        Assert.Contains(result.Errors, err => err.Contains("Sample Addon") && err.Contains("(version >= 2.0.0)") && err.Contains("1.0.0"));
    }

    /// <summary>
    /// Verifies that when a profile contains content with missing required CAS objects, launch validation fails.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateLaunchAsync_WhenRequiredCasObjectMissing_FailsValidationAsync()
    {
        // Arrange
        const string profileId = "profile-missing-cas-object";
        const string installationManifestId = "1.104.steam.gameinstallation.zerohour";
        const string clientManifestId = "1.104.steam.gameclient.zerohour";
        const string modManifestId = "1.0.communityoutpost.addon.crzh";

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

        var modManifest = new ContentManifest
        {
            Id = ManifestId.Create(modManifestId),
            Name = "Camera Mod - Zero Hour",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Files =
            [
                new()
                {
                    RelativePath = "Generals.exe",
                    Hash = "missing_exe_hash_999",
                    SourceType = ContentSourceType.ContentAddressable,
                    IsRequired = true,
                },
            ],
        };

        var profile = new GameProfile
        {
            Id = profileId,
            Name = "Missing CAS Profile",
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
                modManifestId,
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
            .Setup(m => m.GetManifestAsync(ManifestId.Create(modManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(modManifest));

        _casServiceMock
            .Setup(c => c.ExistsAsync("missing_exe_hash_999", ContentType.Addon, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        _casServiceMock
            .Setup(c => c.ExistsAsync("missing_exe_hash_999", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var facade = CreateFacade();

        // Act
        var result = await facade.ValidateLaunchAsync(profileId);

        // Assert
        Assert.False(result.Success);
        Assert.Contains(result.Errors, err => err.Contains("Missing") && err.Contains("CAS objects"));
    }

    /// <summary>
    /// Verifies that when all required CAS objects are present, launch validation passes the CAS gate.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateLaunchAsync_WhenRequiredCasObjectsPresent_PassesValidationAsync()
    {
        // Arrange
        const string profileId = "profile-present-cas-object";
        const string installationManifestId = "1.104.steam.gameinstallation.zerohour";
        const string clientManifestId = "1.104.steam.gameclient.zerohour";
        const string modManifestId = "1.0.communityoutpost.addon.crzh";

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

        var modManifest = new ContentManifest
        {
            Id = ManifestId.Create(modManifestId),
            Name = "Camera Mod - Zero Hour",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Files =
            [
                new()
                {
                    RelativePath = "Generals.exe",
                    Hash = "present_exe_hash_111",
                    SourceType = ContentSourceType.ContentAddressable,
                    IsRequired = true,
                },
            ],
        };

        var profile = new GameProfile
        {
            Id = profileId,
            Name = "Present CAS Profile",
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
                modManifestId,
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
            .Setup(m => m.GetManifestAsync(ManifestId.Create(modManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(modManifest));

        _casServiceMock
            .Setup(c => c.ExistsAsync("present_exe_hash_111", ContentType.Addon, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        _casServiceMock
            .Setup(c => c.ExistsAsync("present_exe_hash_111", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var facade = CreateFacade();

        // Act
        var result = await facade.ValidateLaunchAsync(profileId);

        // Assert
        Assert.True(result.Success);
        _casServiceMock.Verify(c => c.ExistsAsync("present_exe_hash_111", ContentType.Addon, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that when an enabled content ID has no corresponding manifest in the pool, launch validation fails early.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateLaunchAsync_WhenEnabledContentManifestMissingFromPool_FailsValidationAsync()
    {
        // Arrange
        const string profileId = "profile-missing-manifest";
        const string installationManifestId = "1.104.steam.gameinstallation.zerohour";
        const string clientManifestId = "1.0.communityoutpost.gameclient.communityoutpostgameclientcommunitypatch";
        const string missingContentId = "1.1.communityoutpost.addon.crzh";

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

        var profile = new GameProfile
        {
            Id = profileId,
            Name = "Community Outpost - Zero Hour",
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
                missingContentId,
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
            .Setup(m => m.GetManifestAsync(ManifestId.Create(missingContentId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(null));

        var facade = CreateFacade();

        // Act
        var result = await facade.ValidateLaunchAsync(profileId);

        // Assert
        Assert.False(result.Success);
        Assert.Contains(result.Errors, err => err.Contains($"Missing or invalid content IDs: {missingContentId}"));
    }

    /// <summary>A stopped tool never reports success, while a live tool carries its tracked identity.</summary>
    /// <param name="exited">Whether registration drains an early exit.</param>
    /// <param name="exitCode">The observed exit code.</param>
    /// <returns>The async task.</returns>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public async Task CompleteToolLaunchAsync_UsesTrackedStateBeforeReportingSuccessAsync(bool exited, int exitCode)
    {
        using var process = Process.GetCurrentProcess();
        var identity = Guid.NewGuid();
        var info = new GameProcessInfo
        {
            ProcessId = process.Id,
            ProcessInstanceId = identity,
            IsRunning = true,
        };
        _gameProcessManagerMock.Setup(m => m.TrackProcess(process)).Returns(info);
        GameLaunchInfo? registered = null;
        _launchRegistryMock.Setup(m => m.RegisterLaunchAsync(It.IsAny<GameLaunchInfo>()))
            .Callback<GameLaunchInfo>(launch =>
            {
                registered = launch;
                if (exited)
                {
                    launch.TerminatedAt = DateTime.UtcNow;
                    launch.ExitCode = exitCode;
                    launch.ProcessInfo.IsRunning = false;
                }
            })
            .Returns(Task.CompletedTask);
        var recipient = new object();
        var messages = new List<ProfileLaunchedMessage>();
        WeakReferenceMessenger.Default.Register<ProfileLaunchedMessage>(recipient, (_, message) =>
        {
            if (message.ProfileId == "tool-profile")
            {
                messages.Add(message);
            }
        });
        try
        {
            var result = await CreateFacade().CompleteToolLaunchAsync(
                process, new GameProfile { Id = "tool-profile", Name = "Tool" }, "tool-workspace", "tool");
            Assert.Equal(!exited, result.Success);
            Assert.NotNull(registered);
            Assert.Equal(identity, registered.ProcessInfo.ProcessInstanceId);
            if (exited)
            {
                Assert.Contains($"exit code {exitCode}", result.FirstError);
                Assert.Empty(messages);
                _notificationServiceMock.Verify(m => m.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Never);
            }
            else
            {
                Assert.Equal(identity, Assert.Single(messages).ProcessInstanceId);
                Assert.Same(info, result.Data!.ProcessInfo);
            }

            _launchRegistryMock.Verify(m => m.UnregisterLaunchAsync(It.IsAny<string>()), Times.Never);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }
    }

    /// <summary>Tools that exit before tracking retain their code and never report success.</summary>
    /// <param name="exitCode">The tool exit code.</param>
    /// <returns>The async task.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task CompleteToolLaunchAsync_AlreadyExited_PreservesDiagnosticsAsync(int exitCode)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
        startInfo.ArgumentList.Add($"exit {exitCode}");
        using var process = Process.Start(startInfo)!;
        await process.WaitForExitAsync();
        _gameProcessManagerMock.Setup(m => m.TrackProcess(process)).Returns((GameProcessInfo?)null);
        GameLaunchInfo? registered = null;
        _launchRegistryMock.Setup(m => m.RegisterLaunchAsync(It.IsAny<GameLaunchInfo>()))
            .Callback<GameLaunchInfo>(launch => registered = launch)
            .Returns(Task.CompletedTask);
        var result = await CreateFacade().CompleteToolLaunchAsync(
            process, new GameProfile { Id = "exited-tool", Name = "Tool" }, "workspace", "tool");
        Assert.False(result.Success);
        Assert.NotNull(registered);
        Assert.Equal(exitCode, registered.ExitCode);
        Assert.Equal(exitCode != 0, registered.HasFailed);
        Assert.False(registered.ProcessInfo.IsRunning);
        _notificationServiceMock.Verify(m => m.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Never);
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
            NullLogger<ProfileLauncherFacade>.Instance,
            new DirectRunner(NullLogger<DirectRunner>.Instance));
    }
}
