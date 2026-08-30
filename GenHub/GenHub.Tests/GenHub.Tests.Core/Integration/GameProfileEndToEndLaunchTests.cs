using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launcher;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameSettings;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Workspace;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.Launching;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Integration;

/// <summary>
/// End-to-end integration tests simulating profile creation through to launcher dependency resolution and launch validation.
/// </summary>
public class GameProfileEndToEndLaunchTests : IDisposable
{
    private readonly string _testTempDir;
    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();
    private readonly Mock<IGameProfileRepository> _profileRepositoryMock = new();
    private readonly Mock<IGameInstallationService> _installationServiceMock = new();
    private readonly Mock<IGameSettingsService> _gameSettingsServiceMock = new();
    private readonly Mock<ICasService> _casServiceMock = new();
    private readonly Mock<IStorageLocationService> _storageLocationServiceMock = new();
    private readonly Mock<IGameProcessManager> _processManagerMock = new();
    private readonly Mock<IWorkspaceManager> _workspaceManagerMock = new();
    private readonly Mock<ILaunchRegistry> _launchRegistryMock = new();
    private readonly Mock<IProfileContentLinker> _profileContentLinkerMock = new();
    private readonly Mock<ISteamLauncher> _steamLauncherMock = new();
    private readonly Mock<IConfigurationProviderService> _configurationProviderServiceMock = new();
    private readonly Mock<ILogger<DependencyResolver>> _depLoggerMock = new();
    private readonly Mock<ILogger<GameProfileManager>> _profileManagerLoggerMock = new();
    private readonly Mock<ILogger<GameLauncher>> _launcherLoggerMock = new();

    private readonly DependencyResolver _dependencyResolver;
    private readonly GameProfileManager _profileManager;
    private readonly GameLauncher _gameLauncher;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileEndToEndLaunchTests"/> class.
    /// </summary>
    public GameProfileEndToEndLaunchTests()
    {
        _testTempDir = Directory.CreateTempSubdirectory("GenHub.E2ETests.").FullName;

        _dependencyResolver = new DependencyResolver(_manifestPoolMock.Object, _depLoggerMock.Object);
        _profileManager = new GameProfileManager(
            _profileRepositoryMock.Object,
            _installationServiceMock.Object,
            _manifestPoolMock.Object,
            _gameSettingsServiceMock.Object,
            _profileManagerLoggerMock.Object);

        _configurationProviderServiceMock.Setup(x => x.GetWorkspacePath()).Returns(_testTempDir);
        _configurationProviderServiceMock.Setup(x => x.GetApplicationDataPath()).Returns(_testTempDir);
        _configurationProviderServiceMock.Setup(x => x.GetDefaultWorkspaceStrategy()).Returns(WorkspaceStrategy.SymlinkOnly);

        _gameLauncher = new GameLauncher(
            _launcherLoggerMock.Object,
            _profileManager,
            _workspaceManagerMock.Object,
            _processManagerMock.Object,
            _manifestPoolMock.Object,
            _dependencyResolver,
            _launchRegistryMock.Object,
            _installationServiceMock.Object,
            _casServiceMock.Object,
            _storageLocationServiceMock.Object,
            _gameSettingsServiceMock.Object,
            _profileContentLinkerMock.Object,
            _steamLauncherMock.Object,
            _configurationProviderServiceMock.Object);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }
        catch
        {
            // Ignore cleanup errors in tests
        }
    }

    /// <summary>
    /// Verifies that a GeneralsOnline profile with variant naming discrepancies resolves dependencies correctly and completes launch.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task EndToEnd_GeneralsOnlineProfile_ResolvesDiscrepantManifestsAndLaunches()
    {
        // Arrange - Retail Installation
        var installationId = "install-retail-1";
        var zhDir = Path.Combine(_testTempDir, "ZeroHour");
        Directory.CreateDirectory(zhDir);
        File.WriteAllText(Path.Combine(zhDir, "generals.exe"), "dummy exe");
        File.WriteAllText(Path.Combine(zhDir, "INIZH.big"), "dummy big");

        var retailInstallation = new GameInstallation(zhDir, GameInstallationType.Retail);
        retailInstallation.SetPaths(null, zhDir);

        // Manifests in pool with new format (1.82826.*)
        var clientManifestId = "1.82826.generalsonline.gameclient.60hz";
        var gameDataManifestId = "1.82826.generalsonline.patch.gamedata";
        var mapPackManifestId = "1.82826.generalsonline.mappack.quickmatchmaps";

        var clientManifest = new ContentManifest
        {
            Id = ManifestId.Create(clientManifestId),
            Name = "GeneralsOnline 60Hz",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "generalsonline" },
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "generals.exe",
                    Hash = "a1b2c3d4e5f6",
                    IsExecutable = true,
                    SourceType = ContentSourceType.ContentAddressable,
                },
            ],
        };

        var gameDataManifest = new ContentManifest
        {
            Id = ManifestId.Create(gameDataManifestId),
            Name = "GeneralsOnline Game Data",
            ContentType = ContentType.Patch,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "generalsonline" },
        };

        var mapPackManifest = new ContentManifest
        {
            Id = ManifestId.Create(mapPackManifestId),
            Name = "GeneralsOnline QuickMatch Maps",
            ContentType = ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "generalsonline" },
        };

        var allPooledManifests = new List<ContentManifest> { clientManifest, gameDataManifest, mapPackManifest };

        _manifestPoolMock
            .Setup(p => p.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManifestId id, CancellationToken _) =>
            {
                var match = allPooledManifests.FirstOrDefault(m => m.Id.Value == id.Value);
                return match != null
                    ? OperationResult<ContentManifest?>.CreateSuccess(match)
                    : OperationResult<ContentManifest?>.CreateFailure("Not found");
            });

        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(allPooledManifests));

        _manifestPoolMock
            .Setup(p => p.GetContentDirectoryAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string?>.CreateSuccess(zhDir));

        _installationServiceMock
            .Setup(s => s.GetInstallationAsync(installationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(retailInstallation));

        _storageLocationServiceMock
            .Setup(s => s.GetWorkspacePath(It.IsAny<GameInstallation>()))
            .Returns(zhDir);

        _casServiceMock
            .Setup(c => c.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        _casServiceMock
            .Setup(c => c.ExistsAsync(It.IsAny<string>(), It.IsAny<ContentType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        _gameSettingsServiceMock
            .Setup(s => s.LoadOptionsAsync(It.IsAny<GameType>()))
            .ReturnsAsync(OperationResult<IniOptions>.CreateSuccess(new IniOptions()));

        _gameSettingsServiceMock
            .Setup(s => s.SaveOptionsAsync(It.IsAny<GameType>(), It.IsAny<IniOptions>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        _gameSettingsServiceMock
            .Setup(s => s.LoadGeneralsOnlineSettingsAsync())
            .ReturnsAsync(OperationResult<GeneralsOnlineSettings>.CreateSuccess(new GeneralsOnlineSettings()));

        _gameSettingsServiceMock
            .Setup(s => s.SaveGeneralsOnlineSettingsAsync(It.IsAny<GeneralsOnlineSettings>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        _profileContentLinkerMock
            .Setup(l => l.PrepareProfileUserDataAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<ContentManifest>>(),
                It.IsAny<GameType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        _profileContentLinkerMock
            .Setup(l => l.SwitchProfileUserDataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IEnumerable<ContentManifest>>(),
                It.IsAny<GameType>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        _profileContentLinkerMock
            .Setup(l => l.GetActiveProfileId())
            .Returns((string?)null);

        _workspaceManagerMock
            .Setup(w => w.PrepareWorkspaceAsync(
                It.IsAny<WorkspaceConfiguration>(),
                It.IsAny<IProgress<WorkspacePreparationProgress>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorkspaceInfo>.CreateSuccess(new WorkspaceInfo
            {
                Id = "ws-1",
                WorkspacePath = zhDir,
                ExecutablePath = Path.Combine(zhDir, "generals.exe"),
            }));

        _processManagerMock
            .Setup(p => p.StartProcessAsync(
                It.IsAny<GameLaunchConfiguration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameProcessInfo>.CreateSuccess(new GameProcessInfo
            {
                ProcessId = 12345,
                ProcessName = "generals.exe",
            }));

        _launchRegistryMock
            .Setup(r => r.GetAllActiveLaunchesAsync())
            .ReturnsAsync([]);

        _launchRegistryMock
            .Setup(r => r.RegisterLaunchAsync(It.IsAny<GameLaunchInfo>()))
            .Returns(Task.CompletedTask);

        // Act 1 - Create Profile with old/discrepant IDs (e.g. 1.0828261.generalsonline.gamedata.zerohour)
        var createRequest = new CreateProfileRequest
        {
            Name = "GeneralsOnline 082826_QFE1 (Replay: match_3610187_user_replay)",
            GameInstallationId = installationId,
            GameClientId = "1.0828261.generalsonline.gameclient.zerohour",
            WorkspaceStrategy = WorkspaceStrategy.SymlinkOnly,
            EnabledContentIds =
            [
                "1.0828261.generalsonline.gameclient.zerohour",
                "1.0828261.generalsonline.gamedata.zerohour",
                "1.0828261.generalsonline.mappack.quickmatchmaps",
            ],
            GameClient = new GameClient
            {
                Id = "1.0828261.generalsonline.gameclient.zerohour",
                Name = "GeneralsOnline 60Hz",
                GameType = GameType.ZeroHour,
                PublisherType = "generalsonline",
                ExecutablePath = Path.Combine(zhDir, "generals.exe"),
                WorkingDirectory = zhDir,
            },
        };

        GameProfile? savedProfile = null;
        _profileRepositoryMock
            .Setup(r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()))
            .Callback<GameProfile, CancellationToken>((p, _) => savedProfile = p)
            .ReturnsAsync((GameProfile p, CancellationToken _) => ProfileOperationResult<GameProfile>.CreateSuccess(p));

        var createResult = await _profileManager.CreateProfileAsync(createRequest);

        Assert.True(createResult.Success);
        Assert.NotNull(savedProfile);

        _profileRepositoryMock
            .Setup(r => r.LoadProfileAsync(savedProfile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(savedProfile));

        // Act 2 - Launch Profile
        var launchResult = await _gameLauncher.LaunchProfileAsync(savedProfile.Id);

        // Assert - Launch succeeds without "Manifest not found" errors
        Assert.True(launchResult.Success, $"Launch failed with error: {string.Join(", ", launchResult.Errors)}");
        Assert.NotNull(launchResult.Data);
        Assert.Equal(12345, launchResult.Data.ProcessInfo.ProcessId);
    }
}
