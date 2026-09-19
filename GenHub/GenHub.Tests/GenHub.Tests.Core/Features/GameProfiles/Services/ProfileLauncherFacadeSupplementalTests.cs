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
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Workspace;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.Launching;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Unit tests for supplemental archive root wiring in <see cref="ProfileLauncherFacade"/>.
/// </summary>
public sealed class ProfileLauncherFacadeSupplementalTests
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
    /// Verifies that preparing a Zero Hour workspace forwards the installation's effective
    /// Generals root as the supplemental archive root, so pre-prepared workspaces carry base
    /// archives without waiting for a launch to repair them.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PrepareWorkspaceAsync_ZeroHourProfile_SetsSupplementalArchiveRootAsync()
    {
        // Arrange
        const string generalsPath = "/retail/generals";
        var capturedConfig = await PrepareWorkspaceWithGeneralsRootAsync(GameType.ZeroHour, generalsPath);

        // Assert
        var expected = OperatingSystem.IsWindows() ? null : generalsPath;
        Assert.Equal(expected, capturedConfig.SupplementalArchiveRoot);
    }

    /// <summary>
    /// Verifies that preparing a Generals workspace leaves the supplemental archive root unset.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PrepareWorkspaceAsync_GeneralsProfile_LeavesSupplementalArchiveRootUnsetAsync()
    {
        // Arrange
        var capturedConfig = await PrepareWorkspaceWithGeneralsRootAsync(GameType.Generals, "/retail/generals");

        // Assert
        Assert.Null(capturedConfig.SupplementalArchiveRoot);
    }

    private async Task<WorkspaceConfiguration> PrepareWorkspaceWithGeneralsRootAsync(GameType gameType, string generalsPath)
    {
        var gameClient = new GameClient
        {
            Id = "client-test",
            Name = "Test Client",
            GameType = gameType,
            ExecutablePath = "generals.exe",
            WorkingDirectory = "/retail/work",
        };

        var profile = new GameProfile
        {
            Id = "test-profile",
            Name = "Test Profile",
            GameClient = gameClient,
            GameInstallationId = "inst-1",
            WorkspaceStrategy = WorkspaceStrategy.HardLink,
            EnabledContentIds = ["1.0.test.gameclient.test", "1.0.test.gameinstall.test"],
        };

        var installation = new GameInstallation("/retail", GameInstallationType.Retail)
        {
            Id = "inst-1",
        };
        installation.SetPaths(generalsPath, "/retail/zerohour");

        var clientManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.gameclient.test"),
            Name = "Test Client",
            ContentType = ContentType.GameClient,
            TargetGame = gameType,
        };
        var installManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.gameinstall.test"),
            Name = "Test Installation",
            ContentType = ContentType.GameInstallation,
            TargetGame = gameType,
        };

        _profileManagerMock
            .Setup(m => m.GetProfileAsync("test-profile", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _profileManagerMock
            .Setup(m => m.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _installationServiceMock
            .Setup(s => s.GetInstallationAsync("inst-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(installation));
        _installationServiceMock
            .Setup(s => s.CreateAndRegisterInstallationManifestsAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _dependencyResolverMock
            .Setup(d => d.ResolveDependenciesWithManifestsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DependencyResolutionResult.CreateSuccess(
                ["1.0.test.gameclient.test", "1.0.test.gameinstall.test"],
                [clientManifest, installManifest],
                []));
        _configurationProviderMock
            .Setup(c => c.GetDefaultWorkspaceStrategy())
            .Returns(WorkspaceStrategy.HardLink);
        _storageLocationServiceMock
            .Setup(s => s.GetWorkspacePath(It.IsAny<GameInstallation>()))
            .Returns("/retail/.genhub-workspace");
        _symlinkCapabilityMock
            .Setup(s => s.CanCreateSymlinks)
            .Returns(true);

        WorkspaceConfiguration? capturedConfig = null;
        _workspaceManagerMock
            .Setup(w => w.PrepareWorkspaceAsync(
                It.IsAny<WorkspaceConfiguration>(),
                It.IsAny<IProgress<WorkspacePreparationProgress>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Callback<WorkspaceConfiguration, IProgress<WorkspacePreparationProgress>?, bool, CancellationToken>(
                (config, _, _, _) => capturedConfig = config)
            .ReturnsAsync(OperationResult<WorkspaceInfo>.CreateSuccess(new WorkspaceInfo
            {
                Id = "test-profile",
                WorkspacePath = Path.Combine("/retail/.genhub-workspace", "test-profile"),
            }));

        var facade = CreateFacade();

        // Act
        var result = await facade.PrepareWorkspaceAsync("test-profile");

        // Assert
        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.NotNull(capturedConfig);
        return capturedConfig;
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
