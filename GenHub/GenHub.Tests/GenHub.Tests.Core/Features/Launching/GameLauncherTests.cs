using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
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
using GenHub.Features.Launching;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Launching;

/// <summary>
/// Tests for <see cref="GameLauncher"/>.
/// </summary>
public class GameLauncherTests : IDisposable
{
    private static readonly string[] TestContentIds = ["1.0.genhub.mod.test"];
    private static readonly string TestWorkspacePath = Path.Combine(Path.GetTempPath(), "test_workspace");
    private static readonly string TestExecutablePath = Path.Combine(TestWorkspacePath, "generals.exe");

    private readonly Mock<IGameProfileManager> _profileManagerMock = new();
    private readonly Mock<IWorkspaceManager> _workspaceManagerMock = new();
    private readonly Mock<IGameProcessManager> _processManagerMock = new();
    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();
    private readonly Mock<IDependencyResolver> _dependencyResolverMock = new();
    private readonly Mock<ILaunchRegistry> _launchRegistryMock = new();
    private readonly Mock<ILogger<GameLauncher>> _loggerMock = new();
    private readonly Mock<IGameInstallationService> _gameInstallationServiceMock = new();
    private readonly Mock<IConfigurationProviderService> _configurationProviderServiceMock = new();
    private readonly Mock<IManifestProvider> _manifestProviderMock = new();
    private readonly Mock<ICasService> _casServiceMock = new();
    private readonly Mock<IGameSettingsService> _gameSettingsServiceMock = new();
    private readonly Mock<IStorageLocationService> _storageLocationServiceMock = new();
    private readonly Mock<IProfileContentLinker> _profileContentLinkerMock = new();
    private readonly Mock<ISteamLauncher> _steamLauncherMock = new();
    private readonly GameLauncher _gameLauncher;
    private readonly string _retailRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameLauncherTests"/> class.
    /// </summary>
    public GameLauncherTests()
    {
        // Setup configuration provider mock
        _configurationProviderServiceMock.Setup(x => x.GetWorkspacePath()).Returns(@"C:\Workspaces");
        _configurationProviderServiceMock.Setup(x => x.GetApplicationDataPath()).Returns(@"C:\Content");
        _configurationProviderServiceMock.Setup(x => x.GetDefaultWorkspaceStrategy()).Returns(WorkspaceStrategy.HardLink);

        // A real directory holding a .big archive. The launcher validates retail archive
        // roots before spawning, because a wrong root only surfaces as a generic engine
        // abort — so a fixture pointing at a path that does not exist would be rejected,
        // exactly as a stale installation would be.
        _retailRoot = Directory.CreateTempSubdirectory("GenHub.GameLauncherTests.").FullName;
        File.WriteAllText(Path.Combine(_retailRoot, "Generals.big"), "archive");

        // Setup game installation service mock
        var testInstallation = new GameInstallation(_retailRoot, GameInstallationType.Steam);

        // Ensure Generals path is set so GameLauncher validation passes
        testInstallation.SetPaths(_retailRoot, null);

        _gameInstallationServiceMock.Setup(x => x.GetInstallationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(testInstallation));

        // Setup launch registry mock
        _launchRegistryMock.Setup(x => x.RegisterLaunchAsync(It.IsAny<GameLaunchInfo>()))
            .Returns(Task.CompletedTask);
        _launchRegistryMock.Setup(x => x.UnregisterLaunchAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Setup CAS service mock
        _casServiceMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Setup game settings service mock
        _gameSettingsServiceMock.Setup(x => x.LoadOptionsAsync(It.IsAny<GameType>()))
            .ReturnsAsync(OperationResult<IniOptions>.CreateSuccess(new IniOptions()));
        _gameSettingsServiceMock.Setup(x => x.SaveOptionsAsync(It.IsAny<GameType>(), It.IsAny<IniOptions>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        _gameSettingsServiceMock.Setup(x => x.LoadGeneralsOnlineSettingsAsync())
            .ReturnsAsync(OperationResult<GeneralsOnlineSettings>.CreateSuccess(new GeneralsOnlineSettings()));
        _gameSettingsServiceMock.Setup(x => x.SaveGeneralsOnlineSettingsAsync(It.IsAny<GeneralsOnlineSettings>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Setup storage location service mock
        _storageLocationServiceMock.Setup(x => x.GetWorkspacePath(It.IsAny<GameInstallation>()))
            .Returns(@"C:\Workspaces");
        _storageLocationServiceMock.Setup(x => x.GetCasPoolPath(It.IsAny<GameInstallation>()))
            .Returns(@"C:\CAS");

        // Setup profile content linker mock
        _profileContentLinkerMock.Setup(x => x.PrepareProfileUserDataAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<ContentManifest>>(),
                It.IsAny<GameType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        _profileContentLinkerMock.Setup(x => x.SwitchProfileUserDataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IEnumerable<ContentManifest>>(),
                It.IsAny<GameType>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        _profileContentLinkerMock.Setup(x => x.GetActiveProfileId())
            .Returns((string?)null);

        // Setup dependency resolver mock - returns resolved manifests including dependencies
        _dependencyResolverMock.Setup(x => x.ResolveDependenciesWithManifestsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) =>
            {
                var idList = ids.ToList();
                return DependencyResolutionResult.CreateSuccess(idList, [], []);
            });

        _gameLauncher = new GameLauncher(
            _loggerMock.Object,
            _profileManagerMock.Object,
            _workspaceManagerMock.Object,
            _processManagerMock.Object,
            _manifestPoolMock.Object,
            _dependencyResolverMock.Object,
            _launchRegistryMock.Object,
            _gameInstallationServiceMock.Object,
            _casServiceMock.Object,
            _storageLocationServiceMock.Object,
            _gameSettingsServiceMock.Object,
            _profileContentLinkerMock.Object,
            _steamLauncherMock.Object,
            _configurationProviderServiceMock.Object);
    }

    /// <summary>
    /// Launches a profile asynchronously and asserts success.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithValidProfile_ShouldSucceedAsync()
    {
        // Arrange
        var profile = CreateTestProfile();
        var workspaceInfo = new WorkspaceInfo
        {
            Id = profile.Id,
            WorkspacePath = TestWorkspacePath,
            ExecutablePath = TestExecutablePath,
        };
        var processInfo = new GameProcessInfo { ProcessId = 123, ProcessName = "generals.exe" };
        var manifest = new ContentManifest { Id = "1.0.genhub.mod.test", Name = "Test Content" };

        _profileManagerMock.Setup(x => x.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _manifestPoolMock.Setup(x => x.GetManifestAsync("1.0.genhub.mod.test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(manifest));

        _dependencyResolverMock.Setup(x => x.ResolveDependenciesWithManifestsAsync(
                It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(TestContentIds)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DependencyResolutionResult.CreateSuccess(
                TestContentIds,
                [manifest],
                []));

        _workspaceManagerMock.Setup(x => x.PrepareWorkspaceAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo));

        _processManagerMock.Setup(x => x.StartProcessAsync(It.IsAny<GameLaunchConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameProcessInfo>.CreateSuccess(processInfo));

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile.Id);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(processInfo.ProcessId, result.Data.ProcessInfo.ProcessId);

        // Verify RegisterLaunchAsync called twice: once for placeholder, once for final update
        _launchRegistryMock.Verify(x => x.RegisterLaunchAsync(It.Is<GameLaunchInfo>(i => i.ProfileId == profile.Id)), Times.Exactly(2));
    }

    /// <summary>
    /// Launches a profile asynchronously with a non-existent profile and asserts failure.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithProfileNotFound_ShouldFailAsync()
    {
        // Arrange
        var profileId = Guid.NewGuid().ToString();
        _profileManagerMock.Setup(x => x.GetProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateFailure("Profile not found"));

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profileId);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Profile not found", result.FirstError);
    }

    /// <summary>
    /// Launches a profile asynchronously with a missing manifest and asserts failure.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithManifestNotFound_ShouldFailAsync()
    {
        // Arrange
        var profile = CreateTestProfile();
        _profileManagerMock.Setup(x => x.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        // Mock dependency resolver to return failure when manifest is not found
        _dependencyResolverMock.Setup(x => x.ResolveDependenciesWithManifestsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DependencyResolutionResult.CreateFailure("Missing or invalid content IDs: 1.0.genhub.mod.test"));

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed to resolve content dependencies", result.FirstError!);
    }

    /// <summary>
    /// Launches a profile asynchronously with null manifest and asserts failure.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithNullManifest_ShouldFailAsync()
    {
        // Arrange
        var profile = CreateTestProfile();
        _profileManagerMock.Setup(x => x.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        // Mock dependency resolver to return failure when manifest returns null
        _dependencyResolverMock.Setup(x => x.ResolveDependenciesWithManifestsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DependencyResolutionResult.CreateFailure("Missing or invalid content IDs: 1.0.genhub.mod.test"));

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed to resolve content dependencies", result.FirstError!);
    }

    /// <summary>
    /// Launches a profile asynchronously with workspace preparation failure and asserts failure.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithWorkspaceFailure_ShouldFailAsync()
    {
        // Arrange
        var profile = CreateTestProfile();
        var manifest = new ContentManifest { Id = "1.0.genhub.mod.test", Name = "Test Content" };
        _profileManagerMock.Setup(x => x.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _manifestPoolMock.Setup(x => x.GetManifestAsync("1.0.genhub.mod.test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(manifest));
        _workspaceManagerMock.Setup(x => x.PrepareWorkspaceAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorkspaceInfo>.CreateFailure("Workspace prep failed"));

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Workspace prep failed", result.FirstError);
    }

    /// <summary>
    /// Launches a profile asynchronously with process start failure and asserts failure.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithProcessStartFailure_ShouldFailAsync()
    {
        // Arrange
        var profile = CreateTestProfile();
        var workspaceInfo = new WorkspaceInfo { Id = profile.Id, WorkspacePath = TestWorkspacePath, ExecutablePath = TestExecutablePath };
        var manifest = new ContentManifest { Id = "1.0.genhub.mod.test", Name = "Test Content" };
        _profileManagerMock.Setup(x => x.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _manifestPoolMock.Setup(x => x.GetManifestAsync("1.0.genhub.mod.test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(manifest));
        _dependencyResolverMock.Setup(x => x.ResolveDependenciesWithManifestsAsync(
                It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(TestContentIds)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DependencyResolutionResult.CreateSuccess(
                TestContentIds,
                [manifest],
                []));
        _workspaceManagerMock.Setup(x => x.PrepareWorkspaceAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo));
        _processManagerMock.Setup(x => x.StartProcessAsync(It.IsAny<GameLaunchConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameProcessInfo>.CreateFailure("Process start failed"));

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Process start failed", result.FirstError);
    }

    /// <summary>
    /// Terminates a game asynchronously with a valid launch ID and asserts success.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task TerminateGameAsync_WithValidLaunchId_ShouldSucceedAsync()
    {
        // Arrange
        var launchId = Guid.NewGuid().ToString();
        var launchInfo = new GameLaunchInfo
        {
            LaunchId = launchId,
            ProfileId = "p1",
            WorkspaceId = "workspace1",
            ProcessInfo = new GameProcessInfo { ProcessId = 123 },
            LaunchedAt = DateTime.UtcNow,
        };

        _launchRegistryMock.Setup(x => x.GetLaunchInfoAsync(launchId)).ReturnsAsync(launchInfo);
        _processManagerMock.Setup(x => x.TerminateProcessAsync(123, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _gameLauncher.TerminateGameAsync(launchId);

        // Assert
        Assert.True(result.Success);
        _launchRegistryMock.Verify(x => x.UnregisterLaunchAsync(launchId), Times.Once);
    }

    /// <summary>
    /// Terminates a game asynchronously with an invalid launch ID and asserts failure.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task TerminateGameAsync_WithInvalidLaunchId_ShouldFailAsync()
    {
        // Arrange
        var launchId = Guid.NewGuid().ToString();
        _launchRegistryMock.Setup(x => x.GetLaunchInfoAsync(launchId)).ReturnsAsync((GameLaunchInfo?)null);

        // Act
        var result = await _gameLauncher.TerminateGameAsync(launchId);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Launch ID not found", result.FirstError);
    }

    /// <summary>
    /// Launches a profile asynchronously with progress tracking and asserts progress is reported.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithProgressTracking_ShouldReportProgressAsync()
    {
        // Arrange
        var profile = CreateTestProfile();
        var workspaceInfo = new WorkspaceInfo { Id = profile.Id, WorkspacePath = TestWorkspacePath, IsPrepared = true, ExecutablePath = TestExecutablePath };
        var processInfo = new GameProcessInfo { ProcessId = 123, ProcessName = "generals.exe" };
        var manifest = new ContentManifest { Id = "1.0.genhub.mod.test", Name = "Test Content" };
        var progressReports = new ConcurrentBag<LaunchProgress>();
        var progressComplete = new TaskCompletionSource<bool>();

        _profileManagerMock.Setup(x => x.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _manifestPoolMock.Setup(x => x.GetManifestAsync("1.0.genhub.mod.test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(manifest));

        _dependencyResolverMock.Setup(x => x.ResolveDependenciesWithManifestsAsync(
                It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(TestContentIds)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DependencyResolutionResult.CreateSuccess(
                TestContentIds,
                [manifest],
                []));

        _workspaceManagerMock.Setup(x => x.PrepareWorkspaceAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<WorkspaceConfiguration, IProgress<WorkspacePreparationProgress>, bool, CancellationToken>((_, p, _, _) =>
            {
                // Simulate workspace progress reporting that will trigger launcher progress updates
                p?.Report(new WorkspacePreparationProgress { FilesProcessed = 1, TotalFiles = 4, CurrentOperation = "Copying", CurrentFile = "test.exe" });
                p?.Report(new WorkspacePreparationProgress { FilesProcessed = 2, TotalFiles = 4, CurrentOperation = "Copying", CurrentFile = "config.ini" });
                p?.Report(new WorkspacePreparationProgress { FilesProcessed = 3, TotalFiles = 4, CurrentOperation = "Linking", CurrentFile = "data.big" });
                p?.Report(new WorkspacePreparationProgress { FilesProcessed = 4, TotalFiles = 4, CurrentOperation = "Finalizing", CurrentFile = string.Empty });
            })
            .ReturnsAsync(OperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo));

        _processManagerMock.Setup(x => x.StartProcessAsync(It.IsAny<GameLaunchConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameProcessInfo>.CreateSuccess(processInfo));

        var progress = new Progress<LaunchProgress>(p =>
        {
            progressReports.Add(p);
            if (p.Phase == LaunchPhase.Running)
            {
                progressComplete.TrySetResult(true);
            }
        });

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile.Id, progress);

        // Wait for Running phase to be reported (with timeout)
        await Task.WhenAny(progressComplete.Task, Task.Delay(1000));

        // Assert
        Assert.True(result.Success);
        var reports = progressReports.ToList();

        Assert.NotEmpty(reports);

        // Verify all expected phases are present
        Assert.Contains(reports, p => p.Phase == LaunchPhase.ValidatingProfile);
        Assert.Contains(reports, p => p.Phase == LaunchPhase.ResolvingContent);
        Assert.Contains(reports, p => p.Phase == LaunchPhase.PreparingWorkspace);
        Assert.Contains(reports, p => p.Phase == LaunchPhase.Starting);
        Assert.Contains(reports, p => p.Phase == LaunchPhase.Running);

        // Verify progress percentages are reasonable
        Assert.Contains(reports, p => p.PercentComplete == 0);   // ValidatingProfile
        Assert.Contains(reports, p => p.PercentComplete == 10);  // ResolvingContent
        Assert.Contains(reports, p => p.PercentComplete >= 40 && p.PercentComplete < 90);  // PreparingWorkspace (multiple reports)
        Assert.Contains(reports, p => p.PercentComplete == 90);  // Starting
        Assert.Contains(reports, p => p.PercentComplete == 100); // Running
    }

    /// <summary>
    /// Launches a profile with cancellation token and verifies cancellation is handled.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithCancellation_ShouldRespectCancellationAsync()
    {
        // Arrange
        var profileId = "test-profile";
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var profile = CreateTestProfile();
        profile.Id = profileId;

        _profileManagerMock.Setup(x => x.GetProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            _gameLauncher.LaunchProfileAsync(profileId, cancellationToken: cts.Token));
    }

    /// <summary>
    /// Verifies Steam launch setup is serialized across profiles that share an installation.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_ConcurrentSteamProfilesSharingInstallation_SerializesSetupAsync()
    {
        // Arrange
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            $"GenHub-GameLauncherAliasTests-{Guid.NewGuid():N}");
        var physicalInstallationPath = Path.Combine(testRoot, "physical-installation");
        var installationAliasPath = Path.Combine(testRoot, "installation-alias");
        Directory.CreateDirectory(physicalInstallationPath);
        CreateDirectoryAlias(installationAliasPath, physicalInstallationPath);
        Assert.Equal(
            InstallationPathLockKey.Create(physicalInstallationPath),
            InstallationPathLockKey.Create(installationAliasPath),
            InstallationPathLockKey.Comparer);

        var firstProfile = CreateTestProfile();
        firstProfile.UseSteamLaunch = true;
        firstProfile.GameInstallationId = "physical-installation";
        var secondProfile = CreateTestProfile();
        secondProfile.UseSteamLaunch = true;
        secondProfile.GameInstallationId = "installation-alias";

        var physicalInstallation = new GameInstallation(
            physicalInstallationPath,
            GameInstallationType.Steam);
        physicalInstallation.SetPaths(physicalInstallationPath, null);
        var aliasInstallation = new GameInstallation(
            installationAliasPath,
            GameInstallationType.Steam);
        aliasInstallation.SetPaths(installationAliasPath, null);

        _gameInstallationServiceMock.Setup(x => x.GetInstallationAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string installationId, CancellationToken _) =>
                OperationResult<GameInstallation>.CreateSuccess(
                    installationId == firstProfile.GameInstallationId
                        ? physicalInstallation
                        : aliasInstallation));

        var cleanupStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupCalls = 0;

        _steamLauncherMock.Setup(x => x.CleanupGameDirectoryAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                Interlocked.Increment(ref cleanupCalls);
                cleanupStarted.TrySetResult(true);
                await releaseCleanup.Task;
                return OperationResult<bool>.CreateFailure("Injected cleanup stop.");
            });

        try
        {
            // Act
            var firstLaunch = _gameLauncher.LaunchProfileAsync(firstProfile);
            await cleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var secondLaunch = _gameLauncher.LaunchProfileAsync(secondProfile);

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250));
                Assert.Equal(1, Volatile.Read(ref cleanupCalls));
            }
            finally
            {
                releaseCleanup.TrySetResult(true);
            }

            // Assert
            var results = await Task.WhenAll(firstLaunch, secondLaunch);
            Assert.All(results, result => Assert.False(result.Success));
            Assert.Equal(2, Volatile.Read(ref cleanupCalls));
        }
        finally
        {
            releaseCleanup.TrySetResult(true);

            if (Directory.Exists(installationAliasPath))
            {
                Directory.Delete(installationAliasPath);
            }

            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Launches a profile with empty enabled content and asserts success.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithEmptyEnabledContent_ShouldSucceedAsync()
    {
        // Arrange
        var profile = CreateTestProfile();
        profile.EnabledContentIds = []; // Empty content
        var workspaceInfo = new WorkspaceInfo { Id = profile.Id, WorkspacePath = TestWorkspacePath };
        var processInfo = new GameProcessInfo { ProcessId = 123, ProcessName = "generals.exe" };

        _profileManagerMock.Setup(x => x.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _workspaceManagerMock.Setup(x => x.PrepareWorkspaceAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo));

        _processManagerMock.Setup(x => x.StartProcessAsync(It.IsAny<GameLaunchConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameProcessInfo>.CreateSuccess(processInfo));

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile.Id);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
    }

    /// <summary>
    /// Terminates a game with process termination failure and ensures launch is not unregistered.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task TerminateGameAsync_WithProcessTerminationFailure_ShouldNotUnregisterAsync()
    {
        // Arrange
        var launchId = Guid.NewGuid().ToString();
        var launchInfo = new GameLaunchInfo
        {
            LaunchId = launchId,
            ProfileId = "p1",
            WorkspaceId = "workspace1",
            ProcessInfo = new GameProcessInfo { ProcessId = 123 },
            LaunchedAt = DateTime.UtcNow,
        };

        _launchRegistryMock.Setup(x => x.GetLaunchInfoAsync(launchId)).ReturnsAsync(launchInfo);
        _processManagerMock.Setup(x => x.TerminateProcessAsync(123, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("Process termination failed"));

        // Act
        var result = await _gameLauncher.TerminateGameAsync(launchId);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Process termination failed", result.FirstError);
        _launchRegistryMock.Verify(x => x.UnregisterLaunchAsync(launchId), Times.Never);
    }

    /// <summary>
    /// Gets all active launches and verifies registry interaction.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task GetActiveGamesAsync_ShouldReturnActiveProcessesAsync()
    {
        // Arrange
        var activeProcesses = new List<GameProcessInfo>
            {
                new() { ProcessId = 123, ProcessName = "game1.exe" },
                new() { ProcessId = 456, ProcessName = "game2.exe" },
            };

        _processManagerMock.Setup(x => x.GetActiveProcessesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameProcessInfo>>.CreateSuccess(activeProcesses));

        // Act
        var result = await _gameLauncher.GetActiveGamesAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Count);
        Assert.Contains(result.Data, p => p.ProcessId == 123);
        Assert.Contains(result.Data, p => p.ProcessId == 456);
    }

    /// <summary>
    /// Gets launch registry information through the registry service.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchRegistry_ShouldTrackActiveLaunchesAsync()
    {
        // Arrange
        var activeLaunches = new List<GameLaunchInfo>
            {
                new() { LaunchId = "launch1", ProfileId = "profile1", WorkspaceId = "workspace1", ProcessInfo = new GameProcessInfo { ProcessId = 123 } },
                new() { LaunchId = "launch2", ProfileId = "profile2", WorkspaceId = "workspace2", ProcessInfo = new GameProcessInfo { ProcessId = 456 } },
            };

        _launchRegistryMock.Setup(x => x.GetAllActiveLaunchesAsync())
            .ReturnsAsync(activeLaunches);

        // Act - Test the registry directly since GameLauncher doesn't expose this method
        var result = await _launchRegistryMock.Object.GetAllActiveLaunchesAsync();

        // Assert
        Assert.Equal(2, result.Count());
        Assert.Contains(result, l => l.LaunchId == "launch1");
        Assert.Contains(result, l => l.LaunchId == "launch2");

        // Verify that the launcher can retrieve individual launch info
        _launchRegistryMock.Setup(x => x.GetLaunchInfoAsync("launch1"))
            .ReturnsAsync(activeLaunches[0]);

        var individualResult = await _launchRegistryMock.Object.GetLaunchInfoAsync("launch1");
        Assert.NotNull(individualResult);
        Assert.Equal("launch1", individualResult.LaunchId);
    }

    /// <summary>
    /// Launches a profile with multiple content manifests and verifies all are resolved.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithMultipleContentManifests_ShouldResolveAllAsync()
    {
        // Arrange
        var profile = CreateTestProfile();
        profile.EnabledContentIds = ["1.0.genhub.mod.manifest1mod", "1.0.genhub.mod.manifest2mod", "1.0.genhub.mod.manifest3mod"];
        var workspaceInfo = new WorkspaceInfo { Id = profile.Id, WorkspacePath = TestWorkspacePath };
        var processInfo = new GameProcessInfo { ProcessId = 123, ProcessName = "generals.exe" };

        _profileManagerMock.Setup(x => x.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        // Setup dependency resolver to return all manifests
        var manifest1 = new ContentManifest { Id = "1.0.genhub.mod.manifest1mod" };
        var manifest2 = new ContentManifest { Id = "1.0.genhub.mod.manifest2mod" };
        var manifest3 = new ContentManifest { Id = "1.0.genhub.mod.manifest3mod" };

        _dependencyResolverMock.Setup(x => x.ResolveDependenciesWithManifestsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DependencyResolutionResult.CreateSuccess(
                profile.EnabledContentIds,
                [manifest1, manifest2, manifest3],
                []));

        _workspaceManagerMock.Setup(x => x.PrepareWorkspaceAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo));

        _processManagerMock.Setup(x => x.StartProcessAsync(It.IsAny<GameLaunchConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameProcessInfo>.CreateSuccess(processInfo));

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile.Id);

        // Assert
        Assert.True(result.Success);
        _dependencyResolverMock.Verify(
            x => x.ResolveDependenciesWithManifestsAsync(
                It.Is<IEnumerable<string>>(ids => ids.Count() == 3),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that profile settings are written to Options.ini before launching.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithProfileSettings_ShouldWriteIniOptionsBeforeLaunchAsync()
    {
        // Arrange
        var profile = CreateTestProfile();
        profile.VideoResolutionWidth = 1920;
        profile.VideoResolutionHeight = 1080;
        profile.VideoWindowed = true;
        profile.AudioSoundVolume = 80;
        profile.AudioMusicVolume = 60;

        var workspaceInfo = new WorkspaceInfo { Id = profile.Id, WorkspacePath = TestWorkspacePath };
        var processInfo = new GameProcessInfo { ProcessId = 123, ProcessName = "generals.exe" };

        _profileManagerMock.Setup(x => x.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _manifestPoolMock.Setup(x => x.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(new ContentManifest { Id = "1.0.genhub.mod.test" }));

        _dependencyResolverMock.Setup(x => x.ResolveDependenciesWithManifestsAsync(
                It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(TestContentIds)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DependencyResolutionResult.CreateSuccess(
                TestContentIds,
                [new ContentManifest { Id = "1.0.genhub.mod.test" }],
                []));

        _workspaceManagerMock.Setup(x => x.PrepareWorkspaceAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo));

        _processManagerMock.Setup(x => x.StartProcessAsync(It.IsAny<GameLaunchConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameProcessInfo>.CreateSuccess(processInfo));

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile.Id);

        // Assert
        Assert.True(result.Success);

        // Verify that SaveOptionsAsync was called with the correct settings
        _gameSettingsServiceMock.Verify(
            x => x.SaveOptionsAsync(
                It.IsAny<GameType>(),
                It.Is<IniOptions>(o =>
                    o.Video.ResolutionWidth == 1920 &&
                    o.Video.ResolutionHeight == 1080 &&
                    o.Video.Windowed == true &&
                    o.Audio.SFXVolume == 80 &&
                    o.Audio.MusicVolume == 60)),
            Times.Once);

        _processManagerMock.Verify(
            x => x.StartProcessAsync(
                It.Is<GameLaunchConfiguration>(c => HasArgument(c, "-win")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that windowed mode adds -win argument to launch.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithWindowedMode_ShouldAddWinArgumentAsync()
    {
        // Arrange
        var profile = CreateTestProfile();
        profile.VideoWindowed = true;

        var workspaceInfo = new WorkspaceInfo { Id = profile.Id, WorkspacePath = TestWorkspacePath };
        var processInfo = new GameProcessInfo { ProcessId = 123, ProcessName = "generals.exe" };

        _profileManagerMock.Setup(x => x.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _manifestPoolMock.Setup(x => x.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(new ContentManifest { Id = "1.0.genhub.mod.test" }));

        _dependencyResolverMock.Setup(x => x.ResolveDependenciesWithManifestsAsync(
                It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(TestContentIds)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DependencyResolutionResult.CreateSuccess(
                TestContentIds,
                [new ContentManifest { Id = "1.0.genhub.mod.test" }],
                []));

        _workspaceManagerMock.Setup(x => x.PrepareWorkspaceAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo));

        _processManagerMock.Setup(x => x.StartProcessAsync(It.IsAny<GameLaunchConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameProcessInfo>.CreateSuccess(processInfo));

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile.Id);

        // Assert
        Assert.True(result.Success);

        // Verify that -win argument was added
        _processManagerMock.Verify(
            x => x.StartProcessAsync(
                It.Is<GameLaunchConfiguration>(c => HasArgument(c, "-win", string.Empty)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that launching a profile without custom settings still saves Options.ini
    /// to preserve TheSuperHackers/GeneralsOnline settings.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithoutProfileSettings_ShouldStillSaveOptionsIniAsync()
    {
        // Arrange
        var profile = CreateTestProfile();

        var workspaceInfo = new WorkspaceInfo { Id = profile.Id, WorkspacePath = TestWorkspacePath };
        var processInfo = new GameProcessInfo { ProcessId = 123, ProcessName = "generals.exe" };

        _profileManagerMock.Setup(x => x.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _manifestPoolMock.Setup(x => x.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(new ContentManifest { Id = "1.0.genhub.mod.test" }));

        _workspaceManagerMock.Setup(x => x.PrepareWorkspaceAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo));

        _processManagerMock.Setup(x => x.StartProcessAsync(It.IsAny<GameLaunchConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameProcessInfo>.CreateSuccess(processInfo));

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile.Id);

        // Assert
        Assert.True(result.Success);

        // Should still save Options.ini to preserve TheSuperHackers/GeneralsOnline settings
        _gameSettingsServiceMock.Verify(
            x => x.SaveOptionsAsync(It.IsAny<GameType>(), It.IsAny<IniOptions>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that a Zero Hour profile running some other client leaves the GeneralsOnline
    /// client's settings.json alone, even when its name would match the heuristic that
    /// identifies profiles with no recorded publisher.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithNonGeneralsOnlineZeroHourProfile_ShouldNotWriteGeneralsOnlineSettingsAsync()
    {
        // Arrange
        var profile = CreateZeroHourProfile(PublisherTypeConstants.TheSuperHackers, "GeneralsOnline-compatible TheSuperHackers");
        ArrangeSuccessfulLaunch(profile);

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile.Id);

        // Assert
        Assert.True(result.Success, result.FirstError);
        _gameSettingsServiceMock.Verify(
            x => x.SaveGeneralsOnlineSettingsAsync(It.IsAny<GeneralsOnlineSettings>()),
            Times.Never);
    }

    /// <summary>
    /// Tests that a GeneralsOnline profile does write its client settings.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithGeneralsOnlineProfile_ShouldWriteGeneralsOnlineSettingsAsync()
    {
        // Arrange
        var profile = CreateZeroHourProfile(PublisherTypeConstants.GeneralsOnline, "GeneralsOnline");
        profile.GoShowFps = true;
        ArrangeSuccessfulLaunch(profile);

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile.Id);

        // Assert
        Assert.True(result.Success, result.FirstError);
        _gameSettingsServiceMock.Verify(
            x => x.SaveGeneralsOnlineSettingsAsync(It.Is<GeneralsOnlineSettings>(s => s.ShowFps)),
            Times.Once);
    }

    /// <summary>
    /// Tests that settings.json is left alone when it could not be read. A missing file reads as
    /// defaults and reports success, so a failed read means the client's own file exists and is
    /// unreadable, and rewriting it from defaults would discard everything the client owns.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithUnreadableGeneralsOnlineSettings_ShouldNotRewriteThemAsync()
    {
        // Arrange
        _gameSettingsServiceMock.Setup(x => x.LoadGeneralsOnlineSettingsAsync())
            .ReturnsAsync(OperationResult<GeneralsOnlineSettings>.CreateFailure("settings.json is locked"));

        var profile = CreateZeroHourProfile(PublisherTypeConstants.GeneralsOnline, "GeneralsOnline");
        profile.GoShowFps = true;
        ArrangeSuccessfulLaunch(profile);

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile.Id);

        // Assert
        Assert.True(result.Success, result.FirstError);
        _gameSettingsServiceMock.Verify(
            x => x.SaveGeneralsOnlineSettingsAsync(It.IsAny<GeneralsOnlineSettings>()),
            Times.Never);
    }

    /// <summary>
    /// Tests that a settings.json spelling a nested section as an explicit null, which is valid
    /// JSON, does not break the merge the launch performs.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithNullGeneralsOnlineSection_ShouldStillWriteSettingsAsync()
    {
        // Arrange
        var existing = new GeneralsOnlineSettings { Camera = null! };

        _gameSettingsServiceMock.Setup(x => x.LoadGeneralsOnlineSettingsAsync())
            .ReturnsAsync(OperationResult<GeneralsOnlineSettings>.CreateSuccess(existing));

        var profile = CreateZeroHourProfile(PublisherTypeConstants.GeneralsOnline, "GeneralsOnline");
        profile.GoCameraMinHeight = 200.0f;
        ArrangeSuccessfulLaunch(profile);

        GeneralsOnlineSettings? saved = null;
        _gameSettingsServiceMock.Setup(x => x.SaveGeneralsOnlineSettingsAsync(It.IsAny<GeneralsOnlineSettings>()))
            .Callback<GeneralsOnlineSettings>(s => saved = s)
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile.Id);

        // Assert
        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(saved);
        Assert.Equal(200.0f, saved.Camera.MinHeight);
    }

    /// <summary>
    /// Tests that the values a user configured inside the GeneralsOnline client survive a launch
    /// of a profile that says nothing about them.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithGeneralsOnlineProfile_ShouldPreserveSettingsTheProfileDoesNotSpecifyAsync()
    {
        // Arrange - every seeded value is the opposite of the GenHub default
        var existing = new GeneralsOnlineSettings
        {
            ShowPing = false,
            ChatFontSize = 24,
            RememberUsername = false,
        };
        existing.Render.FpsLimit = 60;
        existing.AdditionalSettings["auth_token"] = JsonSerializer.Deserialize<JsonElement>("\"preserve-me\"");

        _gameSettingsServiceMock.Setup(x => x.LoadGeneralsOnlineSettingsAsync())
            .ReturnsAsync(OperationResult<GeneralsOnlineSettings>.CreateSuccess(existing));

        var profile = CreateZeroHourProfile(PublisherTypeConstants.GeneralsOnline, "GeneralsOnline");
        profile.GoShowFps = true;
        ArrangeSuccessfulLaunch(profile);

        GeneralsOnlineSettings? saved = null;
        _gameSettingsServiceMock.Setup(x => x.SaveGeneralsOnlineSettingsAsync(It.IsAny<GeneralsOnlineSettings>()))
            .Callback<GeneralsOnlineSettings>(s => saved = s)
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile.Id);

        // Assert
        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(saved);
        Assert.True(saved.ShowFps);
        Assert.False(saved.ShowPing);
        Assert.Equal(24, saved.ChatFontSize);
        Assert.False(saved.RememberUsername);
        Assert.Equal(60, saved.Render.FpsLimit);
        Assert.True(saved.AdditionalSettings.ContainsKey("auth_token"), "client-owned key was dropped");
        Assert.Equal("preserve-me", saved.AdditionalSettings["auth_token"].GetString());
    }

    /// <summary>
    /// Removes the temporary retail root.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_retailRoot, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; a leftover temp directory is not worth failing the run over.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Creates a test <see cref="GameProfile"/> with required members set.
    /// </summary>
    /// <returns>A valid <see cref="GameProfile"/>.</returns>
    private static GameProfile CreateTestProfile()
    {
        return new GameProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test Profile",
            GameInstallationId = "install-1",
            GameClient = new GameClient
            {
                Id = "version-1",
                ExecutablePath = TestExecutablePath,
                WorkingDirectory = TestWorkspacePath,
                GameType = GameType.Generals,
            },
            EnabledContentIds = ["1.0.genhub.mod.test"],
        };
    }

    /// <summary>
    /// Creates a Zero Hour <see cref="GameProfile"/> attributed to a specific publisher.
    /// </summary>
    /// <param name="publisherType">The publisher the profile's client belongs to.</param>
    /// <param name="clientName">The client name, which is also consulted when identifying the publisher.</param>
    /// <returns>A valid Zero Hour <see cref="GameProfile"/>.</returns>
    private static GameProfile CreateZeroHourProfile(string publisherType, string clientName)
    {
        return new GameProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test Profile",
            GameInstallationId = "install-1",
            GameClient = new GameClient
            {
                Id = "version-1",
                Name = clientName,
                ExecutablePath = @"C:\Games\generals.exe",
                GameType = GameType.ZeroHour,
                PublisherType = publisherType,
            },
            EnabledContentIds = ["1.0.genhub.mod.test"],
        };
    }

    private static bool HasArgument(GameLaunchConfiguration? config, string key)
    {
        return config?.Arguments is not null && config.Arguments.ContainsKey(key);
    }

    private static bool HasArgument(GameLaunchConfiguration? config, string key, string expectedValue)
    {
        return config?.Arguments is not null && config.Arguments.TryGetValue(key, out var val) && val == expectedValue;
    }

    private static void CreateDirectoryAlias(string aliasPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(aliasPath, targetPath);
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(aliasPath);
        startInfo.ArgumentList.Add(targetPath);

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Failed to start junction creation process.");
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private static bool HasWinArgument(GameLaunchConfiguration? config) =>
        config?.Arguments?.ContainsKey("-win") == true;

    private static bool HasEmptyWinArgument(GameLaunchConfiguration? config) =>
        config?.Arguments != null &&
        config.Arguments.TryGetValue("-win", out var value) &&
        value == string.Empty;

    /// <summary>
    /// Wires the mocks a launch needs to reach the settings-writing step and succeed.
    /// </summary>
    /// <param name="profile">The profile being launched.</param>
    private void ArrangeSuccessfulLaunch(GameProfile profile)
    {
        var manifest = new ContentManifest { Id = "1.0.genhub.mod.test", Name = "Test Content" };
        var workspaceInfo = new WorkspaceInfo { Id = profile.Id, WorkspacePath = @"C:\workspace" };
        var processInfo = new GameProcessInfo { ProcessId = 123, ProcessName = "generals.exe" };

        // Zero Hour launches resolve their own installation path, so both roots are declared.
        var installation = new GameInstallation(_retailRoot, GameInstallationType.Steam);
        installation.SetPaths(_retailRoot, _retailRoot);
        _gameInstallationServiceMock.Setup(x => x.GetInstallationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(installation));

        _profileManagerMock.Setup(x => x.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _manifestPoolMock.Setup(x => x.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(manifest));

        _dependencyResolverMock.Setup(x => x.ResolveDependenciesWithManifestsAsync(
                It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(TestContentIds)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DependencyResolutionResult.CreateSuccess(TestContentIds, [manifest], []));

        _workspaceManagerMock.Setup(x => x.PrepareWorkspaceAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo));

        _processManagerMock.Setup(x => x.StartProcessAsync(It.IsAny<GameLaunchConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameProcessInfo>.CreateSuccess(processInfo));
    }
}
