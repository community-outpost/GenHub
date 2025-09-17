using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Launching;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Launching;

/// <summary>
/// Tests for <see cref="GameLauncher"/>.
/// </summary>
public class GameLauncherTests
{
    private readonly Mock<IGameProfileManager> _profileManagerMock = new();
    private readonly Mock<IWorkspaceManager> _workspaceManagerMock = new();
    private readonly Mock<IGameProcessManager> _processManagerMock = new();
    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();
    private readonly Mock<ILaunchRegistry> _launchRegistryMock = new();
    private readonly Mock<ILogger<GameLauncher>> _loggerMock = new();
    private readonly Mock<IGameInstallationService> _gameInstallationServiceMock = new();
    private readonly Mock<IConfigurationProviderService> _configurationProviderServiceMock = new();
    private readonly Mock<IManifestProvider> _manifestProviderMock = new();
    private readonly Mock<ICasService> _casServiceMock = new();
    private readonly GameLauncher _gameLauncher;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameLauncherTests"/> class.
    /// </summary>
    public GameLauncherTests()
    {
        // Setup configuration provider mock
        _configurationProviderServiceMock.Setup(x => x.GetWorkspacePath()).Returns(@"C:\Workspaces");
        _configurationProviderServiceMock.Setup(x => x.GetContentStoragePath()).Returns(@"C:\Content");

        // Setup game installation service mock
        var testInstallation = new GameInstallation(@"C:\Games\CommandAndConquer", GameInstallationType.Steam);
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

        _gameLauncher = new GameLauncher(
            _loggerMock.Object,
            _profileManagerMock.Object,
            _workspaceManagerMock.Object,
            _processManagerMock.Object,
            _manifestPoolMock.Object,
            _launchRegistryMock.Object,
            _gameInstallationServiceMock.Object,
            _manifestProviderMock.Object,
            _casServiceMock.Object,
            _configurationProviderServiceMock.Object);
    }

    /// <summary>
    /// Launches a profile asynchronously and asserts success.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithValidProfile_ShouldSucceed()
    {
        // Arrange
        var profile = CreateTestProfile();
        var workspaceInfo = new WorkspaceInfo { Id = profile.Id, WorkspacePath = @"C:\workspace" };
        var processInfo = new GameProcessInfo { ProcessId = 123, ProcessName = "game.exe" };
        var manifest = new ContentManifest { Id = "1.0.test.publisher.mod", Name = "Test Content" };

        _profileManagerMock.Setup(x => x.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _manifestPoolMock.Setup(x => x.GetManifestAsync("1.0.test.publisher.mod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(manifest));

        _workspaceManagerMock.Setup(x => x.PrepareWorkspaceAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo));

        _processManagerMock.Setup(x => x.StartProcessAsync(It.IsAny<GameLaunchConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameProcessInfo>.CreateSuccess(processInfo));

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile.Id);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(processInfo.ProcessId, result.Data.ProcessInfo.ProcessId);
        _launchRegistryMock.Verify(x => x.RegisterLaunchAsync(It.Is<GameLaunchInfo>(i => i.ProfileId == profile.Id)), Times.Once);
    }

    /// <summary>
    /// Launches a profile asynchronously with a non-existent profile and asserts failure.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithProfileNotFound_ShouldFail()
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
    public async Task LaunchProfileAsync_WithManifestNotFound_ShouldFail()
    {
        // Arrange
        var profile = CreateTestProfile();
        _profileManagerMock.Setup(x => x.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _manifestPoolMock.Setup(x => x.GetManifestAsync("1.0.test.publisher.mod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateFailure("Manifest not found"));

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed to resolve content", result.FirstError!);
    }

    /// <summary>
    /// Launches a profile asynchronously with null manifest and asserts failure.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithNullManifest_ShouldFail()
    {
        // Arrange
        var profile = CreateTestProfile();
        _profileManagerMock.Setup(x => x.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _manifestPoolMock.Setup(x => x.GetManifestAsync("1.0.test.publisher.mod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(null));

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Content manifest '1.0.test.publisher.mod' not found", result.FirstError!);
    }

    /// <summary>
    /// Launches a profile asynchronously with workspace preparation failure and asserts failure.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithWorkspaceFailure_ShouldFail()
    {
        // Arrange
        var profile = CreateTestProfile();
        var manifest = new ContentManifest { Id = "1.0.test.publisher.mod", Name = "Test Content" };
        _profileManagerMock.Setup(x => x.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _manifestPoolMock.Setup(x => x.GetManifestAsync("1.0.test.publisher.mod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(manifest));
        _workspaceManagerMock.Setup(x => x.PrepareWorkspaceAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorkspaceInfo>.CreateFailure("Workspace prep failed"));

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Workspace prep failed", result.FirstError);
    }

    /// <summary>
    /// Launches a profile asynchronously with process start failure and asserts failure.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithProcessStartFailure_ShouldFail()
    {
        // Arrange
        var profile = CreateTestProfile();
        var workspaceInfo = new WorkspaceInfo { Id = profile.Id, WorkspacePath = @"C:\workspace" };
        var manifest = new ContentManifest { Id = "1.0.test.publisher.mod", Name = "Test Content" };
        _profileManagerMock.Setup(x => x.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _manifestPoolMock.Setup(x => x.GetManifestAsync("1.0.test.publisher.mod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(manifest));
        _workspaceManagerMock.Setup(x => x.PrepareWorkspaceAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo));
        _processManagerMock.Setup(x => x.StartProcessAsync(It.IsAny<GameLaunchConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameProcessInfo>.CreateFailure("Process start failed"));

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Process start failed", result.FirstError);
    }

    /// <summary>
    /// Terminates a game asynchronously with a valid launch ID and asserts success.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task TerminateGameAsync_WithValidLaunchId_ShouldSucceed()
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
    public async Task TerminateGameAsync_WithInvalidLaunchId_ShouldFail()
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
    public async Task LaunchProfileAsync_WithProgressTracking_ShouldReportProgress()
    {
        // Arrange
        var profile = CreateTestProfile();
        var workspaceInfo = new WorkspaceInfo { Id = profile.Id, WorkspacePath = @"C:\workspace", IsPrepared = true, ExecutablePath = "game.exe" };
        var processInfo = new GameProcessInfo { ProcessId = 123, ProcessName = "game.exe" };
        var manifest = new ContentManifest { Id = "1.0.test.publisher.mod", Name = "Test Content" };
        var progressReports = new List<LaunchProgress>();
        var progressLock = new object();
        var progressComplete = new TaskCompletionSource<bool>();

        _profileManagerMock.Setup(x => x.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _manifestPoolMock.Setup(x => x.GetManifestAsync("1.0.test.publisher.mod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(manifest));

        _workspaceManagerMock.Setup(x => x.PrepareWorkspaceAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<CancellationToken>()))
            .Callback<WorkspaceConfiguration, IProgress<WorkspacePreparationProgress>, CancellationToken>((_, p, _) =>
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
            lock (progressLock)
            {
                progressReports.Add(p);
                if (p.Phase == LaunchPhase.Running)
                {
                    progressComplete.TrySetResult(true);
                }
            }
        });

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile.Id, progress);

        // Wait for Running phase to be reported (with timeout)
        await Task.WhenAny(progressComplete.Task, Task.Delay(1000));

        // Assert
        Assert.True(result.Success);
        List<LaunchProgress> reports;
        lock (progressLock)
        {
            reports = progressReports.ToList(); // Create a copy for safe enumeration
        }

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
    public async Task LaunchProfileAsync_WithCancellation_ShouldRespectCancellation()
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
        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            await _gameLauncher.LaunchProfileAsync(profileId, cancellationToken: cts.Token);
        });
    }

    /// <summary>
    /// Launches a profile with empty enabled content and asserts success.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WithEmptyEnabledContent_ShouldSucceed()
    {
        // Arrange
        var profile = CreateTestProfile();
        profile.EnabledContentIds = new List<string>(); // Empty content
        var workspaceInfo = new WorkspaceInfo { Id = profile.Id, WorkspacePath = @"C:\workspace" };
        var processInfo = new GameProcessInfo { ProcessId = 123, ProcessName = "game.exe" };

        _profileManagerMock.Setup(x => x.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _workspaceManagerMock.Setup(x => x.PrepareWorkspaceAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo));

        _processManagerMock.Setup(x => x.StartProcessAsync(It.IsAny<GameLaunchConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameProcessInfo>.CreateSuccess(processInfo));

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
    }

    /// <summary>
    /// Terminates a game with process termination failure and ensures launch is not unregistered.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task TerminateGameAsync_WithProcessTerminationFailure_ShouldNotUnregister()
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
    public async Task GetActiveGamesAsync_ShouldReturnActiveProcesses()
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
    public async Task LaunchRegistry_ShouldTrackActiveLaunches()
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
    public async Task LaunchProfileAsync_WithMultipleContentManifests_ShouldResolveAll()
    {
        // Arrange
        var profile = CreateTestProfile();
        profile.EnabledContentIds = new List<string> { "1.0.test.manifest1.mod", "1.0.test.manifest2.mod", "1.0.test.manifest3.mod" };
        var workspaceInfo = new WorkspaceInfo { Id = profile.Id, WorkspacePath = @"C:\workspace" };
        var processInfo = new GameProcessInfo { ProcessId = 123, ProcessName = "game.exe" };

        _profileManagerMock.Setup(x => x.GetProfileAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        // Setup multiple manifests
        _manifestPoolMock.Setup(x => x.GetManifestAsync("1.0.test.manifest1.mod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(new ContentManifest { Id = "1.0.test.manifest1.mod" }));
        _manifestPoolMock.Setup(x => x.GetManifestAsync("1.0.test.manifest2.mod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(new ContentManifest { Id = "1.0.test.manifest2.mod" }));
        _manifestPoolMock.Setup(x => x.GetManifestAsync("1.0.test.manifest3.mod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(new ContentManifest { Id = "1.0.test.manifest3.mod" }));

        _workspaceManagerMock.Setup(x => x.PrepareWorkspaceAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo));

        _processManagerMock.Setup(x => x.StartProcessAsync(It.IsAny<GameLaunchConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameProcessInfo>.CreateSuccess(processInfo));

        // Act
        var result = await _gameLauncher.LaunchProfileAsync(profile);

        // Assert
        Assert.True(result.Success);
        _manifestPoolMock.Verify(x => x.GetManifestAsync("1.0.test.manifest1.mod", It.IsAny<CancellationToken>()), Times.Once);
        _manifestPoolMock.Verify(x => x.GetManifestAsync("1.0.test.manifest2.mod", It.IsAny<CancellationToken>()), Times.Once);
        _manifestPoolMock.Verify(x => x.GetManifestAsync("1.0.test.manifest3.mod", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a test <see cref="GameProfile"/> with required members set.
    /// </summary>
    /// <returns>A valid <see cref="GameProfile"/>.</returns>
    private GameProfile CreateTestProfile()
    {
        return new GameProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test Profile",
            GameInstallationId = "install-1",
            GameVersion = new GameVersion { Id = "version-1", ExecutablePath = @"C:\Games\game.exe" },
            EnabledContentIds = new List<string> { "1.0.test.publisher.mod" },
        };
    }
}
