using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using WorkspaceStrategy = GenHub.Core.Models.Enums.WorkspaceStrategy;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Unit tests for runtime content hot-swapping validation in <see cref="GameProfileManager"/>.
/// </summary>
public class GameProfileManagerHotswapTests
{
    private readonly Mock<IGameProfileRepository> _profileRepositoryMock = new();
    private readonly Mock<IGameInstallationService> _installationServiceMock = new();
    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();
    private readonly Mock<IGameSettingsService> _gameSettingsServiceMock = new();
    private readonly Mock<ILaunchRegistry> _launchRegistryMock = new();
    private readonly Mock<ILogger<GameProfileManager>> _loggerMock = new();
    private readonly GameProfileManager _profileManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileManagerHotswapTests"/> class.
    /// </summary>
    public GameProfileManagerHotswapTests()
    {
        _profileManager = new GameProfileManager(
            _profileRepositoryMock.Object,
            _installationServiceMock.Object,
            _manifestPoolMock.Object,
            _gameSettingsServiceMock.Object,
            _loggerMock.Object,
            _launchRegistryMock.Object);
    }

    /// <summary>
    /// Verifies that updating a non-running profile clears the ActiveWorkspaceId when content changes.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_WhenProfileNotRunning_ClearsActiveWorkspaceIdOnContentChangeAsync()
    {
        // Arrange
        const string profileId = "profile-1";
        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Existing Profile",
            ActiveWorkspaceId = "workspace-abc",
            EnabledContentIds = ["1.0.0.mod.first"],
        };

        _profileRepositoryMock.Setup(r => r.LoadProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));
        _profileRepositoryMock.Setup(r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));
        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync())
            .ReturnsAsync(new List<GameLaunchInfo>());

        var request = new UpdateProfileRequest
        {
            EnabledContentIds = ["1.0.0.mod.second"],
        };

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(existingProfile.ActiveWorkspaceId);
    }

    /// <summary>
    /// Verifies that updating a running profile with map changes succeeds and preserves ActiveWorkspaceId.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_WhenProfileRunning_WithMapChanges_SucceedsAndPreservesActiveWorkspaceIdAsync()
    {
        // Arrange
        const string profileId = "profile-running-1";
        const string oldMapId = "1.0.0.map.oldmap";
        const string newMapId = "1.0.0.mappack.newpack";

        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Running Profile",
            ActiveWorkspaceId = "workspace-live-123",
            EnabledContentIds = [oldMapId],
        };

        var oldMapManifest = new ContentManifest
        {
            Id = ManifestId.Create(oldMapId),
            Name = "Old Map",
            ContentType = ContentType.Map,
        };

        var newMapManifest = new ContentManifest
        {
            Id = ManifestId.Create(newMapId),
            Name = "New Map Pack",
            ContentType = ContentType.MapPack,
        };

        _profileRepositoryMock.Setup(r => r.LoadProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));
        _profileRepositoryMock.Setup(r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));

        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync())
            .ReturnsAsync([CreateActiveLaunch(profileId)]);

        _manifestPoolMock.Setup(m => m.GetManifestAsync(ManifestId.Create(oldMapId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(oldMapManifest));
        _manifestPoolMock.Setup(m => m.GetManifestAsync(ManifestId.Create(newMapId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(newMapManifest));

        var request = new UpdateProfileRequest
        {
            EnabledContentIds = [newMapId],
        };

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("workspace-live-123", existingProfile.ActiveWorkspaceId);
        Assert.Contains(newMapId, existingProfile.EnabledContentIds);
    }

    /// <summary>
    /// Verifies that updating a running profile with locked mod changes fails with a descriptive error.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_WhenProfileRunning_WithModChanges_FailsWithDescriptiveErrorAsync()
    {
        // Arrange
        const string profileId = "profile-running-2";
        const string baseModId = "1.0.0.mod.base";
        const string addedModId = "1.0.0.mod.shockwave";

        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Running Profile",
            ActiveWorkspaceId = "workspace-live-123",
            EnabledContentIds = [baseModId],
        };

        var modManifest = new ContentManifest
        {
            Id = ManifestId.Create(addedModId),
            Name = "ShockWave Mod",
            ContentType = ContentType.Mod,
        };

        _profileRepositoryMock.Setup(r => r.LoadProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));

        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync())
            .ReturnsAsync([CreateActiveLaunch(profileId)]);

        _manifestPoolMock.Setup(m => m.GetManifestAsync(ManifestId.Create(addedModId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(modManifest));

        var request = new UpdateProfileRequest
        {
            EnabledContentIds = [baseModId, addedModId],
        };

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("ShockWave Mod", result.FirstError);
        Assert.Contains("while profile is running", result.FirstError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hot swapped", result.FirstError, StringComparison.OrdinalIgnoreCase);
        _profileRepositoryMock.Verify(r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that updating a running profile with game client changes fails with a descriptive error.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_WhenProfileRunning_WithGameClientChanges_FailsWithDescriptiveErrorAsync()
    {
        // Arrange
        const string profileId = "profile-running-3";
        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Running Profile",
            ActiveWorkspaceId = "workspace-live-123",
            GameClient = new GameClient { Id = "client-original", Name = "Client 1.04" },
        };

        _profileRepositoryMock.Setup(r => r.LoadProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));

        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync())
            .ReturnsAsync([CreateActiveLaunch(profileId)]);

        var request = new UpdateProfileRequest
        {
            GameClient = new GameClient { Id = "client-new", Name = "Client 1.06" },
        };

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("game client", result.FirstError, StringComparison.OrdinalIgnoreCase);
        _profileRepositoryMock.Verify(r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that updating immutable metadata on a running profile is rejected.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_WhenProfileRunning_WithImmutableMetadataChanges_FailsAsync()
    {
        // Arrange
        const string profileId = "profile-running-4";
        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Running Profile",
            ActiveWorkspaceId = "workspace-live-123",
            WorkspaceStrategy = WorkspaceStrategy.SymlinkOnly,
            GameInstallationId = "install-1",
            CustomExecutablePath = "C:\\game\\generals.exe",
            WorkingDirectory = "C:\\game",
        };

        _profileRepositoryMock.Setup(r => r.LoadProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));

        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync())
            .ReturnsAsync([CreateActiveLaunch(profileId)]);

        // 1. Workspace Strategy change
        var req1 = new UpdateProfileRequest { WorkspaceStrategy = WorkspaceStrategy.HardLink };
        var res1 = await _profileManager.UpdateProfileAsync(profileId, req1);
        Assert.False(res1.Success);
        Assert.Contains("workspace strategy", res1.FirstError, StringComparison.OrdinalIgnoreCase);

        // 2. Installation change
        var req2 = new UpdateProfileRequest { GameInstallationId = "install-2" };
        var res2 = await _profileManager.UpdateProfileAsync(profileId, req2);
        Assert.False(res2.Success);
        Assert.Contains("game installation", res2.FirstError, StringComparison.OrdinalIgnoreCase);

        // 2b. Empty installation change
        var req2b = new UpdateProfileRequest { GameInstallationId = string.Empty };
        var res2b = await _profileManager.UpdateProfileAsync(profileId, req2b);
        Assert.False(res2b.Success);
        Assert.Contains("game installation", res2b.FirstError, StringComparison.OrdinalIgnoreCase);

        // 3. Custom executable path change
        var req3 = new UpdateProfileRequest { CustomExecutablePath = "C:\\game\\new_generals.exe" };
        var res3 = await _profileManager.UpdateProfileAsync(profileId, req3);
        Assert.False(res3.Success);
        Assert.Contains("custom executable path", res3.FirstError, StringComparison.OrdinalIgnoreCase);

        // 4. Working directory change
        var req4 = new UpdateProfileRequest { WorkingDirectory = "C:\\other_dir" };
        var res4 = await _profileManager.UpdateProfileAsync(profileId, req4);
        Assert.False(res4.Success);
        Assert.Contains("working directory", res4.FirstError, StringComparison.OrdinalIgnoreCase);

        // 5. Command line arguments change
        var req5 = new UpdateProfileRequest { CommandLineArguments = "-win -quickstart" };
        var res5 = await _profileManager.UpdateProfileAsync(profileId, req5);
        Assert.False(res5.Success);
        Assert.Contains("command line arguments", res5.FirstError, StringComparison.OrdinalIgnoreCase);

        // 6. Active workspace ID change
        var req6 = new UpdateProfileRequest { ActiveWorkspaceId = "workspace-new-999" };
        var res6 = await _profileManager.UpdateProfileAsync(profileId, req6);
        Assert.False(res6.Success);
        Assert.Contains("active workspace", res6.FirstError, StringComparison.OrdinalIgnoreCase);

        // 7. Game client change
        var req7 = new UpdateProfileRequest { GameClient = new GameClient { Id = "different-client-id" } };
        var res7 = await _profileManager.UpdateProfileAsync(profileId, req7);
        Assert.False(res7.Success);
        Assert.Contains("game client", res7.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that updating a running profile with content whose manifest cannot be found fails gracefully.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_WhenProfileRunning_WithManifestNotFound_ReturnsFailureAsync()
    {
        // Arrange
        const string profileId = "profile-running-missing-manifest";
        const string missingManifestId = "1.0.0.map.missing";

        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Running Profile",
            ActiveWorkspaceId = "workspace-live-123",
            EnabledContentIds = [],
        };

        _profileRepositoryMock.Setup(r => r.LoadProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));

        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync())
            .ReturnsAsync([CreateActiveLaunch(profileId)]);

        _manifestPoolMock.Setup(m => m.GetManifestAsync(ManifestId.Create(missingManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateFailure("Manifest not found"));

        var request = new UpdateProfileRequest
        {
            EnabledContentIds = [missingManifestId],
        };

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("manifest not found", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that updating a running profile with an invalid manifest ID format fails gracefully.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_WhenProfileRunning_WithInvalidManifestId_ReturnsFailureAsync()
    {
        // Arrange
        const string profileId = "profile-running-invalid-manifest";
        const string invalidManifestId = "invalid manifest id!";

        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Running Profile",
            ActiveWorkspaceId = "workspace-live-123",
            EnabledContentIds = [],
        };

        _profileRepositoryMock.Setup(r => r.LoadProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));

        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync())
            .ReturnsAsync([CreateActiveLaunch(profileId)]);

        var request = new UpdateProfileRequest
        {
            EnabledContentIds = [invalidManifestId],
        };

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("invalid manifest ID format", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that stale launch records with TerminatedAt set are ignored when checking running status.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_WhenLaunchRecordIsTerminated_TreatsProfileAsNotRunningAsync()
    {
        // Arrange
        const string profileId = "profile-terminated-1";
        var existingProfile = new GameProfile
        {
            Id = profileId,
            Name = "Terminated Profile",
            ActiveWorkspaceId = "workspace-stale",
            EnabledContentIds = ["1.0.0.mod.first"],
        };

        var terminatedLaunch = CreateActiveLaunch(profileId);
        terminatedLaunch.TerminatedAt = DateTime.UtcNow.AddMinutes(-5);

        _profileRepositoryMock.Setup(r => r.LoadProfileAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));
        _profileRepositoryMock.Setup(r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));
        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync())
            .ReturnsAsync([terminatedLaunch]);

        var request = new UpdateProfileRequest
        {
            EnabledContentIds = ["1.0.0.mod.second"],
        };

        // Act
        var result = await _profileManager.UpdateProfileAsync(profileId, request);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(existingProfile.ActiveWorkspaceId);
    }

    private static GameLaunchInfo CreateActiveLaunch(string profileId, string launchId = "launch-1", string workspaceId = "ws-1") => new()
    {
        LaunchId = launchId,
        ProfileId = profileId,
        WorkspaceId = workspaceId,
        ProcessInfo = new GameProcessInfo
        {
            ProcessId = 1234,
            ProcessName = "generals.exe",
            StartTime = DateTime.UtcNow,
        },
    };
}
