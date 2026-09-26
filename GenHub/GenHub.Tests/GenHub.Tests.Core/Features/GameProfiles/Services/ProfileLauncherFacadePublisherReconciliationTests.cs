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
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameSettings;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Core.Models.Storage;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.Launching;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Unit tests for publisher reconciliation handling during profile launch in <see cref="ProfileLauncherFacade"/>.
/// </summary>
public sealed class ProfileLauncherFacadePublisherReconciliationTests
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

    private readonly GameInstallation _installation;
    private readonly ContentManifest _clientManifest;
    private readonly ContentManifest _installManifest;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileLauncherFacadePublisherReconciliationTests"/> class.
    /// </summary>
    public ProfileLauncherFacadePublisherReconciliationTests()
    {
        const string clientManifestId = "1.0.thesuperhackers.gameclient.zh";
        _clientManifest = new ContentManifest
        {
            Id = ManifestId.Create(clientManifestId),
            Name = "SuperHackers Client",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };

        const string installManifestId = "1.0.ea.gameinstallation.zh";
        _installManifest = new ContentManifest
        {
            Id = ManifestId.Create(installManifestId),
            Name = "Zero Hour Installation",
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.ZeroHour,
        };

        _installation = new GameInstallation(@"C:\Games\ZeroHour", GameInstallationType.Retail)
        {
            Id = "inst-1",
        };

        _installationServiceMock
            .Setup(s => s.GetInstallationAsync("inst-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(_installation));

        _manifestPoolMock
            .Setup(m => m.GetManifestAsync(ManifestId.Create(clientManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(_clientManifest));

        _manifestPoolMock
            .Setup(m => m.GetManifestAsync(ManifestId.Create(installManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(_installManifest));

        _casServiceMock
            .Setup(c => c.GetStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CasStats { ObjectCount = 10, TotalSize = 1024, SpaceSaved = 1024 });

        _configurationProviderMock
            .Setup(c => c.GetDefaultWorkspaceStrategy())
            .Returns(WorkspaceStrategy.HardLink);

        _storageLocationServiceMock
            .Setup(s => s.GetCasPoolPath(It.IsAny<GameInstallation>()))
            .Returns(@"C:\Games\ZeroHour\.genhub-cas");
        _storageLocationServiceMock
            .Setup(s => s.GetWorkspacePath(It.IsAny<GameInstallation>()))
            .Returns(@"C:\Games\ZeroHour\.genhub-workspace");

        _symlinkCapabilityMock
            .Setup(s => s.CanCreateSymlinks)
            .Returns(true);

        _gameSettingsServiceMock
            .Setup(s => s.LoadOptionsAsync(It.IsAny<GameType>()))
            .ReturnsAsync(OperationResult<IniOptions>.CreateSuccess(new IniOptions()));

        _launchRegistryMock
            .Setup(r => r.RegisterLaunchAsync(It.IsAny<GameLaunchInfo>()))
            .Returns(Task.CompletedTask);
    }

    /// <summary>
    /// Verifies that when publisher reconciliation creates a new profile,
    /// the new profile is resolved, validated, and launched instead of the triggering profile.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WhenReconcilerCreatesNewProfile_LaunchesNewProfileInsteadOfOriginalAsync()
    {
        // Arrange
        const string originalProfileId = "original-profile";
        const string newProfileId = "cloned-new-profile";

        var gameClient = new GameClient
        {
            Id = "client-zh",
            Name = "SuperHackers Client",
            GameType = GameType.ZeroHour,
            PublisherType = PublisherTypeConstants.TheSuperHackers,
            ExecutablePath = "generals.exe",
        };

        var originalProfile = new GameProfile
        {
            Id = originalProfileId,
            Name = "Original Profile",
            GameClient = gameClient,
            GameInstallationId = "inst-1",
            WorkspaceStrategy = WorkspaceStrategy.HardLink,
            EnabledContentIds = [_clientManifest.Id.Value, _installManifest.Id.Value],
        };

        var newProfile = new GameProfile
        {
            Id = newProfileId,
            Name = "Original Profile (v2)",
            GameClient = gameClient,
            GameInstallationId = "inst-1",
            WorkspaceStrategy = WorkspaceStrategy.HardLink,
            EnabledContentIds = [_clientManifest.Id.Value, _installManifest.Id.Value],
        };

        _profileManagerMock
            .Setup(m => m.GetProfileAsync(originalProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(originalProfile));

        _profileManagerMock
            .Setup(m => m.GetProfileAsync(newProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(newProfile));

        _profileManagerMock
            .Setup(m => m.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(newProfile));

        var reconcilerMock = new Mock<IPublisherReconciler>();
        reconcilerMock
            .Setup(r => r.PublisherType)
            .Returns(PublisherTypeConstants.TheSuperHackers);
        reconcilerMock
            .Setup(r => r.CheckAndReconcileIfNeededAsync(originalProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherReconciliationResult>.CreateSuccess(
                PublisherReconciliationResult.Success(UpdateStrategy.CreateNewProfile, newProfileId, 1)));

        _reconcilerRegistryMock
            .Setup(r => r.GetReconciler(PublisherTypeConstants.TheSuperHackers))
            .Returns(reconcilerMock.Object);

        GameProfile? launchedProfile = null;
        var launchInfo = new GameLaunchInfo
        {
            LaunchId = "launch-1",
            ProfileId = newProfileId,
            WorkspaceId = newProfileId,
            ProcessInfo = new GameProcessInfo { ProcessId = 1234, ProcessName = "generals" },
        };

        _gameLauncherMock
            .Setup(g => g.LaunchProfileAsync(
                It.IsAny<GameProfile>(),
                It.IsAny<IProgress<LaunchProgress>>(),
                It.IsAny<bool>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<GameProfile, IProgress<LaunchProgress>?, bool, IReadOnlyDictionary<string, string>?, CancellationToken>((p, _, _, _, _) =>
            {
                launchedProfile = p;
            })
            .ReturnsAsync(LaunchOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo, profileId: newProfileId));

        var facade = CreateFacade();

        // Act
        var result = await facade.LaunchProfileAsync(originalProfileId);

        // Assert
        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.NotNull(launchedProfile);
        Assert.Equal(newProfileId, launchedProfile.Id);
        Assert.Equal(newProfileId, result.Data?.ProfileId);

        _gameLauncherMock.Verify(
            g => g.LaunchProfileAsync(
                It.Is<GameProfile>(p => p.Id == newProfileId),
                It.IsAny<IProgress<LaunchProgress>>(),
                It.IsAny<bool>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _gameLauncherMock.Verify(
            g => g.LaunchProfileAsync(
                It.Is<GameProfile>(p => p.Id == originalProfileId),
                It.IsAny<IProgress<LaunchProgress>>(),
                It.IsAny<bool>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private ProfileLauncherFacade CreateFacade() => new(
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
        new DirectRunner(NullLogger<DirectRunner>.Instance),
        installationCasPoolService: null,
        localizationService: null);
}
