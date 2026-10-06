using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using GameType = GenHub.Core.Models.Enums.GameType;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Unit tests for the single live-sync pipeline in <see cref="GameProfileManager"/>.
/// Every writer that mutates a running profile's enabled content (profile settings saves,
/// add-to-profile flows, reconciliation) funnels through
/// <see cref="GameProfileManager.UpdateProfileAsync"/>, which must synchronize the live
/// user-data directory before persisting so the running game observes the change.
/// </summary>
public class GameProfileManagerLiveSyncTests
{
    private readonly Mock<IGameProfileRepository> _profileRepositoryMock = new();
    private readonly Mock<IGameInstallationService> _installationServiceMock = new();
    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();
    private readonly Mock<IGameSettingsService> _gameSettingsServiceMock = new();
    private readonly Mock<IProfileContentLinker> _linkerMock = new();
    private readonly Mock<ILaunchRegistry> _launchRegistryMock = new();
    private readonly Mock<ILogger<GameProfileManager>> _loggerMock = new();
    private readonly GameProfileManager _profileManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileManagerLiveSyncTests"/> class.
    /// </summary>
    public GameProfileManagerLiveSyncTests()
    {
        _profileManager = new GameProfileManager(
            _profileRepositoryMock.Object,
            _installationServiceMock.Object,
            _manifestPoolMock.Object,
            _gameSettingsServiceMock.Object,
            Mock.Of<IWorkspaceManager>(),
            _linkerMock.Object,
            _loggerMock.Object,
            _launchRegistryMock.Object);
    }

    /// <summary>
    /// Verifies that adding hotswappable content to a running profile live-syncs the new
    /// manifests before persisting and preserves the active workspace.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_WhenRunningAndMapAdded_LiveSyncsBeforeSavingAsync()
    {
        // Arrange
        const string profileId = "profile-live-add";
        const string installId = "1.104.steam.gameinstallation.zerohour";
        const string mapId = "1.0.0.map.desert";

        var profile = CreateRunningProfile(profileId, [installId]);
        SetupManifest(CreateManifest(installId, "Zero Hour", ContentType.GameInstallation));
        SetupManifest(CreateManifest(mapId, "Tournament Desert", ContentType.Map));
        SetupLinkerSuccess();

        var request = new UpdateProfileRequest
        {
            EnabledContentIds = [installId, mapId],
        };

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.WasAppliedLive);
        Assert.Equal("workspace-live-123", profile.ActiveWorkspaceId);
        _linkerMock.Verify(
            l => l.UpdateProfileUserDataAsync(
                profileId,
                It.Is<IEnumerable<ContentManifest>>(m => m.Count() == 2 && m.Any(x => x.Id.Value == mapId)),
                GameType.ZeroHour,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _profileRepositoryMock.Verify(
            r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that a live-sync failure leaves the persisted profile untouched.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_WhenRunningAndLiveSyncFails_DoesNotSaveProfileAsync()
    {
        // Arrange
        const string profileId = "profile-live-sync-fail";
        const string installId = "1.104.steam.gameinstallation.zerohour";
        const string mapId = "1.0.0.map.desert";

        CreateRunningProfile(profileId, [installId]);
        SetupManifest(CreateManifest(installId, "Zero Hour", ContentType.GameInstallation));
        SetupManifest(CreateManifest(mapId, "Tournament Desert", ContentType.Map));
        _linkerMock.Setup(l => l.UpdateProfileUserDataAsync(
                profileId,
                It.IsAny<IEnumerable<ContentManifest>>(),
                It.IsAny<GameType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("Live file locked by process"));

        var request = new UpdateProfileRequest
        {
            EnabledContentIds = [installId, mapId],
        };

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Live content synchronization failed", result.FirstError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("were not saved", result.FirstError, StringComparison.OrdinalIgnoreCase);
        _profileRepositoryMock.Verify(
            r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that a persistence failure after a successful live sync rolls the live
    /// user data back to the previously enabled manifests.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_WhenRunningAndSaveFailsAfterLiveSync_RollsBackLiveUserDataAsync()
    {
        // Arrange
        const string profileId = "profile-live-save-fail";
        const string installId = "1.104.steam.gameinstallation.zerohour";
        const string originalMapId = "1.0.0.map.desert";

        CreateRunningProfile(profileId, [installId, originalMapId]);
        SetupManifest(CreateManifest(installId, "Zero Hour", ContentType.GameInstallation));
        SetupManifest(CreateManifest(originalMapId, "Tournament Desert", ContentType.Map));
        SetupLinkerSuccess();
        _profileRepositoryMock.Setup(r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateFailure("Disk write failure"));

        var request = new UpdateProfileRequest
        {
            EnabledContentIds = [installId],
        };

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Disk write failure", result.FirstError, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.LiveRollbackAttempted);
        Assert.True(result.LiveRollbackSucceeded);

        // First call forwards the desired state (map removed), second call restores it.
        _linkerMock.Verify(
            l => l.UpdateProfileUserDataAsync(
                profileId,
                It.Is<IEnumerable<ContentManifest>>(m => m.Count() == 1 && m.All(x => x.Id.Value == installId)),
                It.IsAny<GameType>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _linkerMock.Verify(
            l => l.UpdateProfileUserDataAsync(
                profileId,
                It.Is<IEnumerable<ContentManifest>>(m => m.Count() == 2 && m.Any(x => x.Id.Value == originalMapId)),
                It.IsAny<GameType>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that if profile save throws an exception after a successful live sync,
    /// live rollback is executed with CancellationToken.None and failure is returned.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_WhenSaveThrowsExceptionAfterLiveSync_ExecutesRollbackWithCancellationTokenNoneAndReturnsFailureAsync()
    {
        // Arrange
        const string profileId = "profile-live-save-throw";
        const string installId = "1.104.steam.gameinstallation.zerohour";
        const string originalMapId = "1.0.0.map.desert";

        CreateRunningProfile(profileId, [installId, originalMapId]);
        SetupManifest(CreateManifest(installId, "Zero Hour", ContentType.GameInstallation));
        SetupManifest(CreateManifest(originalMapId, "Tournament Desert", ContentType.Map));
        SetupLinkerSuccess();
        _profileRepositoryMock.Setup(r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Fatal disk error"));

        var request = new UpdateProfileRequest
        {
            EnabledContentIds = [installId],
        };

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.LiveRollbackAttempted);
        Assert.True(result.LiveRollbackSucceeded);

        _linkerMock.Verify(
            l => l.UpdateProfileUserDataAsync(
                profileId,
                It.Is<IEnumerable<ContentManifest>>(m => m.Count() == 2 && m.Any(x => x.Id.Value == originalMapId)),
                GameType.ZeroHour,
                CancellationToken.None),
            Times.Once);
    }

    /// <summary>
    /// Verifies that if profile save is canceled after a successful live sync,
    /// live rollback is executed with CancellationToken.None and OperationCanceledException is rethrown.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_WhenSaveIsCanceledAfterLiveSync_ExecutesRollbackWithCancellationTokenNoneAndRethrowsAsync()
    {
        // Arrange
        const string profileId = "profile-live-save-cancel";
        const string installId = "1.104.steam.gameinstallation.zerohour";
        const string originalMapId = "1.0.0.map.desert";

        CreateRunningProfile(profileId, [installId, originalMapId]);
        SetupManifest(CreateManifest(installId, "Zero Hour", ContentType.GameInstallation));
        SetupManifest(CreateManifest(originalMapId, "Tournament Desert", ContentType.Map));
        SetupLinkerSuccess();
        _profileRepositoryMock.Setup(r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var request = new UpdateProfileRequest
        {
            EnabledContentIds = [installId],
        };

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => _profileManager.UpdateProfileAsync(profileId, request));

        _linkerMock.Verify(
            l => l.UpdateProfileUserDataAsync(
                profileId,
                It.Is<IEnumerable<ContentManifest>>(m => m.Count() == 2 && m.Any(x => x.Id.Value == originalMapId)),
                GameType.ZeroHour,
                CancellationToken.None),
            Times.Once);
    }

    /// <summary>
    /// Verifies that when save fails and rollback fails, the result reflects rollback failure details.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_WhenSaveFailsAndRollbackFails_ReturnsFailureWithRollbackErrorInfoAsync()
    {
        // Arrange
        const string profileId = "profile-live-both-fail";
        const string installId = "1.104.steam.gameinstallation.zerohour";
        const string originalMapId = "1.0.0.map.desert";

        CreateRunningProfile(profileId, [installId, originalMapId]);
        SetupManifest(CreateManifest(installId, "Zero Hour", ContentType.GameInstallation));
        SetupManifest(CreateManifest(originalMapId, "Tournament Desert", ContentType.Map));

        _linkerMock.SetupSequence(l => l.UpdateProfileUserDataAsync(
                profileId,
                It.IsAny<IEnumerable<ContentManifest>>(),
                It.IsAny<GameType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("Rollback file locked"));

        _profileRepositoryMock.Setup(r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateFailure("Disk write failure"));

        var request = new UpdateProfileRequest
        {
            EnabledContentIds = [installId],
        };

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.LiveRollbackAttempted);
        Assert.False(result.LiveRollbackSucceeded);
        Assert.Contains("Rollback file locked", result.LiveRollbackError);
    }

    /// <summary>
    /// Verifies that non-hotswappable content is rejected for running profiles without
    /// touching live user data or storage.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_WhenRunningAndModAdded_RejectsWithoutLiveSyncAsync()
    {
        // Arrange
        const string profileId = "profile-live-mod";
        const string installId = "1.104.steam.gameinstallation.zerohour";
        const string modId = "1.0.0.mod.shockwave";

        CreateRunningProfile(profileId, [installId]);
        SetupManifest(CreateManifest(installId, "Zero Hour", ContentType.GameInstallation));
        SetupManifest(CreateManifest(modId, "ShockWave", ContentType.Mod));

        var request = new UpdateProfileRequest
        {
            EnabledContentIds = [installId, modId],
        };

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.False(result.Success);
        _linkerMock.Verify(
            l => l.UpdateProfileUserDataAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<ContentManifest>>(),
                It.IsAny<GameType>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _profileRepositoryMock.Verify(
            r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that content changes for an idle profile persist without live sync.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_WhenIdleAndMapAdded_SavesWithoutLiveSyncAsync()
    {
        // Arrange
        const string profileId = "profile-idle-add";
        const string installId = "1.104.steam.gameinstallation.zerohour";
        const string mapId = "1.0.0.map.desert";

        var profile = new GameProfile
        {
            Id = profileId,
            Name = "Idle Profile",
            ActiveWorkspaceId = "workspace-old",
            EnabledContentIds = [installId],
            GameClient = new GameClient { Id = "client-zh", GameType = GameType.ZeroHour },
        };
        _profileRepositoryMock.Setup(r => r.LoadProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _profileRepositoryMock.Setup(r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync())
            .ReturnsAsync([]);

        var request = new UpdateProfileRequest
        {
            EnabledContentIds = [installId, mapId],
        };

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.True(result.Success);
        _linkerMock.Verify(
            l => l.UpdateProfileUserDataAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<ContentManifest>>(),
                It.IsAny<GameType>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that saving a running profile without content changes skips live sync.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_WhenRunningAndContentUnchanged_SavesWithoutLiveSyncAsync()
    {
        // Arrange
        const string profileId = "profile-live-rename";
        const string installId = "1.104.steam.gameinstallation.zerohour";
        const string mapId = "1.0.0.map.desert";

        CreateRunningProfile(profileId, [installId, mapId]);

        var request = new UpdateProfileRequest
        {
            Name = "Renamed While Running",
            EnabledContentIds = [installId, mapId],
        };

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.True(result.Success);
        _linkerMock.Verify(
            l => l.UpdateProfileUserDataAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<ContentManifest>>(),
                It.IsAny<GameType>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _profileRepositoryMock.Verify(
            r => r.SaveProfileAsync(It.Is<GameProfile>(p => p.Name == "Renamed While Running"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that rollback restores on a running profile persist without live sync so
    /// callers can compensate live user data on their own schedule.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_WhenRunningRollback_SavesWithoutLiveSyncAsync()
    {
        // Arrange
        const string profileId = "profile-live-rollback";
        const string installId = "1.104.steam.gameinstallation.zerohour";
        const string mapId = "1.0.0.map.desert";

        CreateRunningProfile(profileId, [installId]);

        var request = new UpdateProfileRequest
        {
            EnabledContentIds = [installId, mapId],
            IsRollback = true,
        };

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.True(result.Success);
        _linkerMock.Verify(
            l => l.UpdateProfileUserDataAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<ContentManifest>>(),
                It.IsAny<GameType>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that an unresolvable manifest in the desired state fails a running content
    /// update before anything is persisted or synchronized, even when the changed content
    /// itself is valid. A partial manifest list must never reach the linker.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_WhenRunningAndManifestUnresolvable_FailsWithoutSavingAsync()
    {
        // Arrange
        const string profileId = "profile-live-missing";
        const string installId = "1.104.steam.gameinstallation.zerohour";
        const string mapId = "1.0.0.map.desert";

        CreateRunningProfile(profileId, [installId]);
        SetupManifest(CreateManifest(mapId, "Tournament Desert", ContentType.Map));
        _manifestPoolMock.Setup(m => m.GetManifestAsync(ManifestId.Create(installId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateFailure("Manifest not found in pool"));

        var request = new UpdateProfileRequest
        {
            EnabledContentIds = [installId, mapId],
        };

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.False(result.Success);
        _linkerMock.Verify(
            l => l.UpdateProfileUserDataAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<ContentManifest>>(),
                It.IsAny<GameType>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _profileRepositoryMock.Verify(
            r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static ContentManifest CreateManifest(string id, string name, ContentType contentType)
    {
        return new ContentManifest
        {
            Id = ManifestId.Create(id),
            Name = name,
            ContentType = contentType,
            TargetGame = GameType.ZeroHour,
        };
    }

    private static GameLaunchInfo CreateActiveLaunch(string profileId) => new()
    {
        LaunchId = "launch-1",
        ProfileId = profileId,
        WorkspaceId = "ws-1",
        ProcessInfo = new GameProcessInfo
        {
            ProcessId = 1234,
            ProcessName = "generals.exe",
            StartTime = DateTime.UtcNow,
        },
    };

    private GameProfile CreateRunningProfile(string profileId, List<string> enabledContentIds)
    {
        var profile = new GameProfile
        {
            Id = profileId,
            Name = "Running Profile",
            ActiveWorkspaceId = "workspace-live-123",
            EnabledContentIds = enabledContentIds,
            GameClient = new GameClient
            {
                Id = "client-zh",
                Name = "Zero Hour",
                GameType = GameType.ZeroHour,
            },
        };
        _profileRepositoryMock.Setup(r => r.LoadProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _profileRepositoryMock.Setup(r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync())
            .ReturnsAsync([CreateActiveLaunch(profileId)]);
        return profile;
    }

    private void SetupManifest(ContentManifest manifest)
    {
        _manifestPoolMock.Setup(m => m.GetManifestAsync(manifest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(manifest));
    }

    private void SetupLinkerSuccess()
    {
        _linkerMock.Setup(l => l.UpdateProfileUserDataAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<ContentManifest>>(),
                It.IsAny<GameType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
    }
}
