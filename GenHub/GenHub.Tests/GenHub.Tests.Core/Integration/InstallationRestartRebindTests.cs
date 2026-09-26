using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Workspace;
using GenHub.Features.GameInstallations;
using GenHub.Features.GameProfiles.Infrastructure;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.Launching;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Integration;

/// <summary>
/// Simulates an app restart by building fresh installation and profile services over the same
/// persisted settings and profile directory, then checks that profiles bound to a custom
/// installation still resolve it.
/// </summary>
public sealed class InstallationRestartRebindTests : IDisposable
{
    private const string ProfileId = "restart-profile";
    private const string LocalClientId = "1.0.genhublocal.gameclient.localbuild";
    private const string InstallationClientId = "1.104.genhublocal.gameclient.zerohour";

    private readonly string _dataRoot;
    private readonly string _installDir;
    private readonly string _profilesDir;
    private readonly UserSettings _persistedSettings = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="InstallationRestartRebindTests"/> class.
    /// </summary>
    public InstallationRestartRebindTests()
    {
        _dataRoot = Directory.CreateTempSubdirectory("GenHub.RestartRebind.").FullName;
        _installDir = Path.Combine(_dataRoot, "CustomInstall");
        _profilesDir = Path.Combine(_dataRoot, "Profiles");
        Directory.CreateDirectory(_installDir);
        Directory.CreateDirectory(_profilesDir);
        File.WriteAllText(Path.Combine(_installDir, GameClientConstants.GeneralsExecutable), "dummy");
        File.WriteAllText(Path.Combine(_installDir, GameClientConstants.ZeroHourIniBig), "archive");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, true);
        }
    }

    /// <summary>
    /// A profile created against a custom installation resolves the same installation ID after restart.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CustomInstallProfile_AfterRestart_ResolvesStoredInstallationIdAsync()
    {
        string installationId;
        using (var firstRun = CreateInstallationService())
        {
            var registerResult = await firstRun.RegisterCustomInstallationAsync(_installDir);
            Assert.True(registerResult.Success, string.Join("; ", registerResult.Errors));
            installationId = registerResult.Data!.Id;
            await SaveProfileAsync(installationId, CreateLocalClient(GameType.ZeroHour, installationId));
        }

        using var secondRun = CreateInstallationService();
        var repository = CreateRepository();
        var profileManager = CreateProfileManager(repository, secondRun);
        var facade = CreateFacade(profileManager, secondRun);

        var storedProfile = await repository.LoadProfileAsync(ProfileId);
        var installationResult = await secondRun.GetInstallationAsync(storedProfile.Data!.GameInstallationId!);
        var prepareResult = await facade.PrepareWorkspaceAsync(ProfileId);

        Assert.True(installationResult.Success, string.Join("; ", installationResult.Errors));
        Assert.True(prepareResult.Success, string.Join("; ", prepareResult.Errors));
        var reloaded = await repository.LoadProfileAsync(ProfileId);
        Assert.Equal(installationId, reloaded.Data!.GameInstallationId);
    }

    /// <summary>
    /// A profile saved before installation IDs were stable still carries a random ID. Preparing
    /// its workspace rebinds it to the current installation and keeps the local game client.
    /// </summary>
    /// <param name="staleClientInstallationId">Whether the client references a different stale installation.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyRandomIdProfile_PrepareWorkspace_RebindsAndKeepsLocalClientAsync(bool staleClientInstallationId)
    {
        _persistedSettings.CustomInstallationDirectories.Add(Path.GetFullPath(_installDir));
        var legacyId = Guid.NewGuid().ToString();
        await SaveProfileAsync(legacyId, CreateLocalClient(GameType.ZeroHour, staleClientInstallationId ? Guid.NewGuid().ToString() : legacyId));

        using var installationService = CreateInstallationService();
        var repository = CreateRepository();
        var facade = CreateFacade(CreateProfileManager(repository, installationService), installationService);

        var prepareResult = await facade.PrepareWorkspaceAsync(ProfileId);

        Assert.True(prepareResult.Success, string.Join("; ", prepareResult.Errors));
        var expectedId = (await installationService.GetAllInstallationsAsync()).Data!.Single().Id;
        var reloaded = (await repository.LoadProfileAsync(ProfileId)).Data!;
        Assert.Equal(expectedId, reloaded.GameInstallationId);
        Assert.Equal(LocalClientId, reloaded.GameClient!.Id);
        Assert.Equal(expectedId, reloaded.GameClient.InstallationId);
    }

    /// <summary>
    /// When the profile manager rejects the rebind, workspace preparation fails with a clear
    /// error instead of continuing against an installation ID that no longer exists.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LegacyRandomIdProfile_RebindRejected_FailsPrepareWorkspaceAsync()
    {
        _persistedSettings.CustomInstallationDirectories.Add(Path.GetFullPath(_installDir));
        var legacyId = Guid.NewGuid().ToString();
        await SaveProfileAsync(legacyId, CreateLocalClient(GameType.ZeroHour, legacyId));

        using var installationService = CreateInstallationService();
        var repository = CreateRepository();
        var profileManager = new Mock<IGameProfileManager>();
        profileManager
            .Setup(m => m.GetProfileAsync(ProfileId, It.IsAny<CancellationToken>()))
            .Returns((string id, CancellationToken ct) => repository.LoadProfileAsync(id, ct));
        profileManager
            .Setup(m => m.UpdateProfileAsync(ProfileId, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateFailure("No compatible game client found"));
        var facade = CreateFacade(profileManager.Object, installationService);

        var prepareResult = await facade.PrepareWorkspaceAsync(ProfileId);

        Assert.False(prepareResult.Success);
        Assert.Contains("Could not rebind profile", prepareResult.FirstError);
        profileManager.Verify(
            m => m.UpdateProfileAsync(
                ProfileId,
                It.Is<UpdateProfileRequest>(r => r.GameClient != null && r.GameClient.Id == LocalClientId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static GameClient CreateLocalClient(GameType gameType, string installationId) => new()
    {
        Id = LocalClientId,
        Name = "Local build",
        Version = "localbuild",
        GameType = gameType,
        SourceType = ContentType.GameClient,
        ExecutablePath = "/elsewhere/localbuild/generalszh",
        WorkingDirectory = "/elsewhere/localbuild",
        InstallationId = installationId,
    };

    private static IGameProfileManager CreateProfileManager(GameProfileRepository repository, IGameInstallationService installationService)
    {
        var manifestPool = new Mock<IContentManifestPool>();
        return new GameProfileManager(
            repository,
            installationService,
            manifestPool.Object,
            Mock.Of<IGameSettingsService>(),
            Mock.Of<IWorkspaceManager>(),
            Mock.Of<IProfileContentLinker>(),
            NullLogger<GameProfileManager>.Instance);
    }

    private static ProfileLauncherFacade CreateFacade(IGameProfileManager profileManager, IGameInstallationService installationService)
    {
        var clientManifest = new ContentManifest
        {
            Id = ManifestId.Create(LocalClientId),
            Name = "Local build",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };
        var dependencyResolver = new Mock<IDependencyResolver>();
        dependencyResolver
            .Setup(d => d.ResolveDependenciesWithManifestsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DependencyResolutionResult.CreateSuccess([LocalClientId], [clientManifest], []));
        var configurationProvider = new Mock<IConfigurationProviderService>();
        configurationProvider.Setup(c => c.GetDefaultWorkspaceStrategy()).Returns(WorkspaceStrategy.HardLink);
        var storageLocation = new Mock<IStorageLocationService>();
        storageLocation.Setup(s => s.GetWorkspacePath(It.IsAny<GameInstallation>())).Returns("/unused/workspaces");
        var symlinkCapability = new Mock<ISymlinkCapabilityProvider>();
        symlinkCapability.Setup(s => s.CanCreateSymlinks).Returns(true);
        var workspaceManager = new Mock<IWorkspaceManager>();
        workspaceManager
            .Setup(w => w.PrepareWorkspaceAsync(
                It.IsAny<WorkspaceConfiguration>(),
                It.IsAny<IProgress<WorkspacePreparationProgress>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorkspaceInfo>.CreateSuccess(new WorkspaceInfo { Id = ProfileId, WorkspacePath = "/unused/workspaces/restart-profile" }));

        return new ProfileLauncherFacade(
            profileManager,
            Mock.Of<IGameLauncher>(),
            workspaceManager.Object,
            Mock.Of<ILaunchRegistry>(),
            Mock.Of<IContentManifestPool>(),
            installationService,
            dependencyResolver.Object,
            Mock.Of<ICasService>(),
            Mock.Of<IGameSettingsService>(),
            storageLocation.Object,
            Mock.Of<INotificationService>(),
            Mock.Of<IPublisherReconcilerRegistry>(),
            configurationProvider.Object,
            Mock.Of<IGameProcessManager>(),
            symlinkCapability.Object,
            NullLogger<ProfileLauncherFacade>.Instance,
            new DirectRunner(NullLogger<DirectRunner>.Instance),
            installationCasPoolService: null,
            localizationService: null);
    }

    private GameProfileRepository CreateRepository() =>
        new(_profilesDir, NullLogger<GameProfileRepository>.Instance);

    private async Task SaveProfileAsync(string installationId, GameClient client)
    {
        var profile = new GameProfile
        {
            Id = ProfileId,
            Name = "Restart Profile",
            GameInstallationId = installationId,
            GameClient = client,
            WorkspaceStrategy = WorkspaceStrategy.HardLink,
            EnabledContentIds = [LocalClientId],
        };
        var saveResult = await CreateRepository().SaveProfileAsync(profile);
        Assert.True(saveResult.Success, string.Join("; ", saveResult.Errors));
    }

    private GameInstallationService CreateInstallationService()
    {
        var detection = new Mock<IGameInstallationDetectionOrchestrator>();
        detection
            .Setup(d => d.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DetectionResult<GameInstallation>.CreateSuccess([], TimeSpan.Zero));

        var clientDetection = new Mock<IGameClientDetectionOrchestrator>();
        clientDetection
            .Setup(c => c.DetectGameClientsFromInstallationsAsync(It.IsAny<IEnumerable<IGameInstallation>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<IGameInstallation> installations, CancellationToken _) =>
                DetectionResult<GameClient>.CreateSuccess(
                    installations.Select(i => new GameClient
                    {
                        Id = InstallationClientId,
                        Name = "Zero Hour",
                        Version = "1.04",
                        GameType = GameType.ZeroHour,
                        SourceType = ContentType.GameInstallation,
                        InstallationId = i.Id,
                        WorkingDirectory = i.InstallationPath,
                    }).ToList(),
                    TimeSpan.Zero));

        var manifestBuilder = new Mock<IContentManifestBuilder>();
        manifestBuilder.Setup(b => b.Build()).Returns(new ContentManifest());
        var manifestGeneration = new Mock<IManifestGenerationService>();
        manifestGeneration
            .Setup(m => m.CreateGameInstallationManifestAsync(
                It.IsAny<string>(),
                It.IsAny<GameType>(),
                It.IsAny<GameInstallationType>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(manifestBuilder.Object);

        var manifestPool = new Mock<IContentManifestPool>();
        manifestPool
            .Setup(p => p.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateFailure("Not found"));
        manifestPool
            .Setup(p => p.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        manifestPool
            .Setup(p => p.SearchManifestsAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        var pathResolver = new Mock<IInstallationPathResolver>();
        pathResolver
            .Setup(r => r.ValidateInstallationPathAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var userSettings = new Mock<IUserSettingsService>();
        userSettings.Setup(s => s.Get()).Returns(() => _persistedSettings);
        userSettings
            .Setup(s => s.TryUpdateAndSaveAsync(It.IsAny<Func<UserSettings, bool>>()))
            .ReturnsAsync((Func<UserSettings, bool> updater) => updater(_persistedSettings));

        return new GameInstallationService(
            detection.Object,
            clientDetection.Object,
            NullLogger<GameInstallationService>.Instance,
            manifestGeneration.Object,
            manifestPool.Object,
            pathResolver.Object,
            userSettings.Object);
    }
}
