using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.UserData;
using GenHub.Features.UserData.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.UserData;

/// <summary>
/// Contains unit tests for <see cref="ProfileContentLinkerService"/>.
/// </summary>
public sealed class ProfileContentLinkerServiceTests : IDisposable
{
    private readonly Mock<IUserDataTracker> _userDataTrackerMock = new();
    private readonly Mock<ILogger<ProfileContentLinkerService>> _loggerMock = new();
    private readonly ProfileContentLinkerService _linkerService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileContentLinkerServiceTests"/> class.
    /// </summary>
    public ProfileContentLinkerServiceTests()
    {
        ProfileContentLinkerService.ResetActiveProfilesForTesting();
        _linkerService = new ProfileContentLinkerService(
            _userDataTrackerMock.Object,
            _loggerMock.Object);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        ProfileContentLinkerService.ResetActiveProfilesForTesting();
    }

    /// <summary>
    /// Verifies that SwitchProfileUserDataAsync deactivates lingering active user data from other profiles even when oldProfileId is null (e.g. after GenHub/game crash).
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SwitchProfileUserDataAsync_WhenOldProfileIsNull_DeactivatesLingeringActiveUserDataFromOtherProfilesAsync()
    {
        // Arrange
        const string newProfileId = "profile-new";
        const string lingeringProfileId = "profile-crashed";
        const GameType gameType = GameType.ZeroHour;

        var lingeringManifest = new UserDataManifest
        {
            ManifestId = "1.0.0.patch.crashed",
            ProfileId = lingeringProfileId,
            TargetGame = gameType,
            IsActive = true,
            InstalledFiles = [new UserDataFileEntry { AbsolutePath = "C:\\path\\GameData.ini", RelativePath = "Data\\INI\\GameData.ini", InstallTarget = ContentInstallTarget.UserDataDirectory }],
        };

        _userDataTrackerMock.Setup(t => t.GetGameUserDataAsync(gameType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<UserDataManifest>>.CreateSuccess([lingeringManifest]));

        _userDataTrackerMock.Setup(t => t.DeactivateProfileUserDataAsync(lingeringProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        _userDataTrackerMock.Setup(t => t.ActivateProfileUserDataAsync(newProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var newManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.0.map.desert"),
            Name = "Desert Map",
            ContentType = ContentType.Map,
            Files = [new ManifestFile { RelativePath = "Maps\\Desert.map", InstallTarget = ContentInstallTarget.UserMapsDirectory, Hash = "hash-1" }],
        };

        _userDataTrackerMock.Setup(t => t.GetUserDataManifestAsync(newManifest.Id.Value, newProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<UserDataManifest?>.CreateSuccess(null));

        _userDataTrackerMock.Setup(t => t.InstallUserDataAsync(
            newManifest.Id.Value,
            newProfileId,
            gameType,
            It.IsAny<IEnumerable<ManifestFile>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<UserDataManifest>.CreateSuccess(new UserDataManifest { ManifestId = newManifest.Id.Value, ProfileId = newProfileId }));

        // Act
        var result = await _linkerService.SwitchProfileUserDataAsync(
            oldProfileId: null,
            newProfileId: newProfileId,
            newManifests: [newManifest],
            targetGame: gameType);

        // Assert
        Assert.True(result.Success);
        _userDataTrackerMock.Verify(t => t.DeactivateProfileUserDataAsync(lingeringProfileId, It.IsAny<CancellationToken>()), Times.Once);
        _userDataTrackerMock.Verify(t => t.ActivateProfileUserDataAsync(newProfileId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that SwitchProfileUserDataAsync returns failure when adopting a manifest from old profile fails during skipCleanup.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SwitchProfileUserDataAsync_WhenAdoptionInstallFails_ReturnsFailureAsync()
    {
        // Arrange
        const string oldProfileId = "profile-old";
        const string newProfileId = "profile-new";
        const GameType gameType = GameType.ZeroHour;

        var oldManifest = new UserDataManifest
        {
            ManifestId = "1.0.0.map.desert",
            ProfileId = oldProfileId,
            TargetGame = gameType,
            InstalledFiles = [new UserDataFileEntry { AbsolutePath = "C:\\path\\Desert.map", RelativePath = "Maps\\Desert.map", InstallTarget = ContentInstallTarget.UserMapsDirectory }],
        };

        _userDataTrackerMock.Setup(t => t.GetProfileUserDataAsync(oldProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<UserDataManifest>>.CreateSuccess([oldManifest]));

        _userDataTrackerMock.Setup(t => t.InstallUserDataAsync(
            oldManifest.ManifestId,
            newProfileId,
            gameType,
            It.IsAny<IEnumerable<ManifestFile>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<UserDataManifest>.CreateFailure("Adoption disk error"));

        // Act
        var result = await _linkerService.SwitchProfileUserDataAsync(
            oldProfileId: oldProfileId,
            newProfileId: newProfileId,
            newManifests: [],
            targetGame: gameType,
            skipCleanup: true);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Adoption disk error", result.FirstError, StringComparison.OrdinalIgnoreCase);
        _userDataTrackerMock.Verify(t => t.ActivateProfileUserDataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that SwitchProfileUserDataAsync adopts only manifests matching targetGame or GameType.Unknown during skipCleanup.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SwitchProfileUserDataAsync_WhenSkipCleanupTrue_AdoptsOnlyManifestsMatchingTargetGameOrUnknownAsync()
    {
        // Arrange
        const string oldProfileId = "profile-old";
        const string newProfileId = "profile-new";
        const GameType gameType = GameType.ZeroHour;

        var matchingManifest = new UserDataManifest
        {
            ManifestId = "1.0.0.map.zh-map",
            ProfileId = oldProfileId,
            TargetGame = GameType.ZeroHour,
            InstalledFiles = [new UserDataFileEntry { AbsolutePath = "C:\\path\\ZH.map", RelativePath = "Maps\\ZH.map", InstallTarget = ContentInstallTarget.UserMapsDirectory }],
        };

        var otherGameManifest = new UserDataManifest
        {
            ManifestId = "1.0.0.map.gen-map",
            ProfileId = oldProfileId,
            TargetGame = GameType.Generals,
            InstalledFiles = [new UserDataFileEntry { AbsolutePath = "C:\\path\\Gen.map", RelativePath = "Maps\\Gen.map", InstallTarget = ContentInstallTarget.UserMapsDirectory }],
        };

        var unknownGameManifest = new UserDataManifest
        {
            ManifestId = "1.0.0.map.unknown-map",
            ProfileId = oldProfileId,
            TargetGame = GameType.Unknown,
            InstalledFiles = [new UserDataFileEntry { AbsolutePath = "C:\\path\\Unknown.map", RelativePath = "Maps\\Unknown.map", InstallTarget = ContentInstallTarget.UserMapsDirectory }],
        };

        _userDataTrackerMock.Setup(t => t.GetProfileUserDataAsync(oldProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<UserDataManifest>>.CreateSuccess([matchingManifest, otherGameManifest, unknownGameManifest]));

        _userDataTrackerMock.Setup(t => t.InstallUserDataAsync(
            It.IsAny<string>(),
            newProfileId,
            gameType,
            It.IsAny<IEnumerable<ManifestFile>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string manifestId, string profileId, GameType game, IEnumerable<ManifestFile> _, string _, string _, CancellationToken _) =>
                OperationResult<UserDataManifest>.CreateSuccess(new UserDataManifest { ManifestId = manifestId, ProfileId = profileId, TargetGame = game }));

        _userDataTrackerMock.Setup(t => t.GetGameUserDataAsync(gameType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<UserDataManifest>>.CreateSuccess([]));

        // Act
        var result = await _linkerService.SwitchProfileUserDataAsync(
            oldProfileId: oldProfileId,
            newProfileId: newProfileId,
            newManifests: [],
            targetGame: gameType,
            skipCleanup: true);

        // Assert
        Assert.True(result.Success);
        _userDataTrackerMock.Verify(
            t => t.InstallUserDataAsync(
                "1.0.0.map.zh-map",
                newProfileId,
                gameType,
                It.IsAny<IEnumerable<ManifestFile>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _userDataTrackerMock.Verify(
            t => t.InstallUserDataAsync(
                "1.0.0.map.unknown-map",
                newProfileId,
                gameType,
                It.IsAny<IEnumerable<ManifestFile>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _userDataTrackerMock.Verify(
            t => t.InstallUserDataAsync(
                "1.0.0.map.gen-map",
                It.IsAny<string>(),
                It.IsAny<GameType>(),
                It.IsAny<IEnumerable<ManifestFile>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that PrepareProfileUserDataAsync cleans up any lingering active user data from other profiles before activating the target profile.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PrepareProfileUserDataAsync_DeactivatesLingeringActiveUserDataFromOtherProfilesAsync()
    {
        // Arrange
        const string targetProfileId = "profile-target";
        const string lingeringProfileId = "profile-lingering";
        const GameType gameType = GameType.ZeroHour;

        var lingeringManifest = new UserDataManifest
        {
            ManifestId = "1.0.0.patch.old",
            ProfileId = lingeringProfileId,
            TargetGame = gameType,
            IsActive = true,
            InstalledFiles = [new UserDataFileEntry { AbsolutePath = "C:\\path\\GameData.ini", RelativePath = "Data\\INI\\GameData.ini", InstallTarget = ContentInstallTarget.UserDataDirectory }],
        };

        _userDataTrackerMock.Setup(t => t.GetGameUserDataAsync(gameType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<UserDataManifest>>.CreateSuccess([lingeringManifest]));

        _userDataTrackerMock.Setup(t => t.DeactivateProfileUserDataAsync(lingeringProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        _userDataTrackerMock.Setup(t => t.ActivateProfileUserDataAsync(targetProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.0.map.desert"),
            Name = "Desert Map",
            ContentType = ContentType.Map,
            Files = [new ManifestFile { RelativePath = "Maps\\Desert.map", InstallTarget = ContentInstallTarget.UserMapsDirectory, Hash = "hash-1" }],
        };

        _userDataTrackerMock.Setup(t => t.GetUserDataManifestAsync(manifest.Id.Value, targetProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<UserDataManifest?>.CreateSuccess(null));

        _userDataTrackerMock.Setup(t => t.InstallUserDataAsync(
            manifest.Id.Value,
            targetProfileId,
            gameType,
            It.IsAny<IEnumerable<ManifestFile>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<UserDataManifest>.CreateSuccess(new UserDataManifest { ManifestId = manifest.Id.Value, ProfileId = targetProfileId }));

        // Act
        var result = await _linkerService.PrepareProfileUserDataAsync(targetProfileId, [manifest], gameType);

        // Assert
        Assert.True(result.Success);
        _userDataTrackerMock.Verify(t => t.DeactivateProfileUserDataAsync(lingeringProfileId, It.IsAny<CancellationToken>()), Times.Once);
        _userDataTrackerMock.Verify(t => t.ActivateProfileUserDataAsync(targetProfileId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that UpdateProfileUserDataAsync returns failure when activation fails.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task UpdateProfileUserDataAsync_WhenActivationFails_ReturnsFailureAsync()
    {
        // Arrange
        const string profileId = "profile-live";
        const GameType gameType = GameType.ZeroHour;

        _userDataTrackerMock.Setup(t => t.GetGameUserDataAsync(gameType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<UserDataManifest>>.CreateSuccess([]));

        _userDataTrackerMock.Setup(t => t.GetProfileUserDataAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<UserDataManifest>>.CreateSuccess([]));

        _userDataTrackerMock.Setup(t => t.ActivateProfileUserDataAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("Activation locked by file"));

        // Simulate that this profile is the active profile (empty manifest list sets active profile without activating user data)
        await _linkerService.PrepareProfileUserDataAsync(profileId, [], gameType);

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.0.map.desert"),
            Name = "Desert Map",
            ContentType = ContentType.Map,
            Files = [new ManifestFile { RelativePath = "Maps\\Desert.map", InstallTarget = ContentInstallTarget.UserMapsDirectory, Hash = "hash-1" }],
        };

        _userDataTrackerMock.Setup(t => t.GetUserDataManifestAsync(manifest.Id.Value, profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<UserDataManifest?>.CreateSuccess(null));

        _userDataTrackerMock.Setup(t => t.InstallUserDataAsync(
            manifest.Id.Value,
            profileId,
            gameType,
            It.IsAny<IEnumerable<ManifestFile>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<UserDataManifest>.CreateSuccess(new UserDataManifest { ManifestId = manifest.Id.Value, ProfileId = profileId }));

        _userDataTrackerMock.Setup(t => t.UninstallUserDataAsync(manifest.Id.Value, profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _linkerService.UpdateProfileUserDataAsync(profileId, [manifest], gameType);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed to activate user data", result.FirstError, StringComparison.OrdinalIgnoreCase);
        _userDataTrackerMock.Verify(t => t.UninstallUserDataAsync(manifest.Id.Value, profileId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that UpdateProfileUserDataAsync rolls back previously uninstalled manifests when a subsequent uninstall fails.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task UpdateProfileUserDataAsync_WhenSecondUninstallFails_RollsBackFirstUninstallAsync()
    {
        // Arrange
        const string profileId = "profile-uninstall-fail";
        const GameType gameType = GameType.ZeroHour;
        const string manifest1Id = "1.0.0.map.desert1";
        const string manifest2Id = "1.0.0.map.desert2";

        var existing1 = new UserDataManifest
        {
            ManifestId = manifest1Id,
            ProfileId = profileId,
            TargetGame = gameType,
            IsActive = true,
            InstalledFiles = [new UserDataFileEntry { AbsolutePath = "C:\\path\\1.map", RelativePath = "1.map", InstallTarget = ContentInstallTarget.UserMapsDirectory }],
        };
        var existing2 = new UserDataManifest
        {
            ManifestId = manifest2Id,
            ProfileId = profileId,
            TargetGame = gameType,
            IsActive = true,
            InstalledFiles = [new UserDataFileEntry { AbsolutePath = "C:\\path\\2.map", RelativePath = "2.map", InstallTarget = ContentInstallTarget.UserMapsDirectory }],
        };

        _userDataTrackerMock.Setup(t => t.GetProfileUserDataAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<UserDataManifest>>.CreateSuccess([existing1, existing2]));

        _userDataTrackerMock.Setup(t => t.UninstallUserDataAsync(manifest1Id, profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        _userDataTrackerMock.Setup(t => t.UninstallUserDataAsync(manifest2Id, profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("File locked"));

        _userDataTrackerMock.Setup(t => t.InstallUserDataAsync(
            It.IsAny<string>(),
            profileId,
            gameType,
            It.IsAny<IEnumerable<ManifestFile>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<UserDataManifest>.CreateSuccess(existing1));

        // Act - remove both manifests
        var result = await _linkerService.UpdateProfileUserDataAsync(profileId, [], gameType);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("File locked", result.FirstError, StringComparison.OrdinalIgnoreCase);

        // Verify both manifests were reinstalled as part of rollback compensation
        _userDataTrackerMock.Verify(
            t => t.InstallUserDataAsync(
                manifest1Id,
                profileId,
                gameType,
                It.IsAny<IEnumerable<ManifestFile>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _userDataTrackerMock.Verify(
            t => t.InstallUserDataAsync(
                manifest2Id,
                profileId,
                gameType,
                It.IsAny<IEnumerable<ManifestFile>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that UpdateProfileUserDataAsync rolls back previously installed manifests when a subsequent install fails.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task UpdateProfileUserDataAsync_WhenSecondInstallFails_RollsBackFirstInstallAsync()
    {
        // Arrange
        const string profileId = "profile-install-fail";
        const GameType gameType = GameType.ZeroHour;
        const string manifest1Id = "1.0.0.map.desert1";
        const string manifest2Id = "1.0.0.map.desert2";

        _userDataTrackerMock.Setup(t => t.GetProfileUserDataAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<UserDataManifest>>.CreateSuccess([]));

        var manifest1 = new ContentManifest
        {
            Id = ManifestId.Create(manifest1Id),
            Name = "Map 1",
            ContentType = ContentType.Map,
            Files = [new ManifestFile { RelativePath = "Maps\\1.map", InstallTarget = ContentInstallTarget.UserMapsDirectory, Hash = "hash-1" }],
        };
        var manifest2 = new ContentManifest
        {
            Id = ManifestId.Create(manifest2Id),
            Name = "Map 2",
            ContentType = ContentType.Map,
            Files = [new ManifestFile { RelativePath = "Maps\\2.map", InstallTarget = ContentInstallTarget.UserMapsDirectory, Hash = "hash-2" }],
        };

        _userDataTrackerMock.Setup(t => t.InstallUserDataAsync(
            manifest1Id,
            profileId,
            gameType,
            It.IsAny<IEnumerable<ManifestFile>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<UserDataManifest>.CreateSuccess(new UserDataManifest { ManifestId = manifest1Id, ProfileId = profileId }));

        _userDataTrackerMock.Setup(t => t.InstallUserDataAsync(
            manifest2Id,
            profileId,
            gameType,
            It.IsAny<IEnumerable<ManifestFile>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<UserDataManifest>.CreateFailure("Disk full"));

        _userDataTrackerMock.Setup(t => t.UninstallUserDataAsync(It.IsAny<string>(), profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act - install both manifests
        var result = await _linkerService.UpdateProfileUserDataAsync(profileId, [manifest1, manifest2], gameType);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Disk full", result.FirstError, StringComparison.OrdinalIgnoreCase);

        // Verify both manifests were uninstalled as part of rollback compensation
        _userDataTrackerMock.Verify(t => t.UninstallUserDataAsync(manifest1Id, profileId, It.IsAny<CancellationToken>()), Times.Once);
        _userDataTrackerMock.Verify(t => t.UninstallUserDataAsync(manifest2Id, profileId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that UpdateProfileUserDataAsync reports that live rollback was incomplete when rollback compensation itself fails.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task UpdateProfileUserDataAsync_WhenRollbackFails_ReturnsFailureWithIncompleteRollbackNoticeAsync()
    {
        // Arrange
        const string profileId = "profile-rollback-fail";
        const GameType gameType = GameType.ZeroHour;
        const string manifest1Id = "1.0.0.map.desert1";
        const string manifest2Id = "1.0.0.map.desert2";

        var existing1 = new UserDataManifest
        {
            ManifestId = manifest1Id,
            ProfileId = profileId,
            TargetGame = gameType,
            InstalledFiles = [new UserDataFileEntry { AbsolutePath = "C:\\path\\1.map", RelativePath = "1.map", InstallTarget = ContentInstallTarget.UserMapsDirectory }],
        };
        var existing2 = new UserDataManifest
        {
            ManifestId = manifest2Id,
            ProfileId = profileId,
            TargetGame = gameType,
            InstalledFiles = [new UserDataFileEntry { AbsolutePath = "C:\\path\\2.map", RelativePath = "2.map", InstallTarget = ContentInstallTarget.UserMapsDirectory }],
        };

        _userDataTrackerMock.Setup(t => t.GetProfileUserDataAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<UserDataManifest>>.CreateSuccess([existing1, existing2]));

        _userDataTrackerMock.Setup(t => t.UninstallUserDataAsync(manifest1Id, profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        _userDataTrackerMock.Setup(t => t.UninstallUserDataAsync(manifest2Id, profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("File locked"));

        // Compensating reinstall fails during rollback
        _userDataTrackerMock.Setup(t => t.InstallUserDataAsync(
            It.IsAny<string>(),
            profileId,
            gameType,
            It.IsAny<IEnumerable<ManifestFile>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<UserDataManifest>.CreateFailure("Disk unreadable during rollback"));

        // Act - remove both manifests
        var result = await _linkerService.UpdateProfileUserDataAsync(profileId, [], gameType);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("live rollback was incomplete", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that a workspace-targeted map is recognized as profile user data and is not uninstalled when another map is added during live sync.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task UpdateProfileUserDataAsync_WhenWorkspaceTargetedMapExistsAndNewUserMapAdded_PreservesWorkspaceTargetedMapAsync()
    {
        // Arrange
        const string profileId = "profile-workspace-map";
        const GameType gameType = GameType.ZeroHour;
        const string legacyMapId = "1.0.0.map.legacymap";
        const string newMapId = "1.0.0.map.newmap";

        var existingLegacyMap = new UserDataManifest
        {
            ManifestId = legacyMapId,
            ProfileId = profileId,
            TargetGame = gameType,
            IsActive = true,
            InstalledFiles = [new UserDataFileEntry { AbsolutePath = "C:\\path\\legacy.map", RelativePath = "legacy.map", InstallTarget = ContentInstallTarget.UserMapsDirectory }],
        };

        _userDataTrackerMock.Setup(t => t.GetProfileUserDataAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<UserDataManifest>>.CreateSuccess([existingLegacyMap]));

        var legacyMapManifest = new ContentManifest
        {
            Id = ManifestId.Create(legacyMapId),
            Name = "Legacy Map",
            ContentType = ContentType.Map,
            Files = [new ManifestFile { RelativePath = "Maps\\legacy.map", InstallTarget = ContentInstallTarget.Workspace, Hash = "hash-legacy" }],
        };

        var newMapManifest = new ContentManifest
        {
            Id = ManifestId.Create(newMapId),
            Name = "New Map",
            ContentType = ContentType.Map,
            Files = [new ManifestFile { RelativePath = "Maps\\new.map", InstallTarget = ContentInstallTarget.UserMapsDirectory, Hash = "hash-new" }],
        };

        _userDataTrackerMock.Setup(t => t.InstallUserDataAsync(
            newMapId,
            profileId,
            gameType,
            It.IsAny<IEnumerable<ManifestFile>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<UserDataManifest>.CreateSuccess(new UserDataManifest { ManifestId = newMapId, ProfileId = profileId }));

        _userDataTrackerMock.Setup(t => t.ActivateProfileUserDataAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _linkerService.UpdateProfileUserDataAsync(profileId, [legacyMapManifest, newMapManifest], gameType);

        // Assert
        Assert.True(result.Success);
        _userDataTrackerMock.Verify(
            t => t.UninstallUserDataAsync(legacyMapId, profileId, It.IsAny<CancellationToken>()),
            Times.Never);
        _userDataTrackerMock.Verify(
            t => t.InstallUserDataAsync(
                newMapId,
                profileId,
                gameType,
                It.IsAny<IEnumerable<ManifestFile>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that variant-only maps (with empty root Files but files in Variants) are resolved and installed during user data sync.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task UpdateProfileUserDataAsync_WhenVariantOnlyMapExists_ResolvesAndInstallsVariantFilesAsync()
    {
        // Arrange
        const string profileId = "profile-variant-map";
        const GameType gameType = GameType.ZeroHour;
        const string variantMapId = "1.0.0.map.variantmap";

        _userDataTrackerMock.Setup(t => t.GetProfileUserDataAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<UserDataManifest>>.CreateSuccess([]));

        var variantMapManifest = new ContentManifest
        {
            Id = ManifestId.Create(variantMapId),
            Name = "Variant Map",
            ContentType = ContentType.Map,
            Files = [],
            Variants =
            [
                new ArtifactVariant
                {
                    Files = [new ManifestFile { RelativePath = "Maps\\variant.map", InstallTarget = ContentInstallTarget.UserMapsDirectory, Hash = "hash-variant" }],
                },
            ],
        };

        _userDataTrackerMock.Setup(t => t.InstallUserDataAsync(
            variantMapId,
            profileId,
            gameType,
            It.IsAny<IEnumerable<ManifestFile>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<UserDataManifest>.CreateSuccess(new UserDataManifest { ManifestId = variantMapId, ProfileId = profileId }));

        // Act
        var result = await _linkerService.UpdateProfileUserDataAsync(profileId, [variantMapManifest], gameType);

        // Assert
        Assert.True(result.Success);
        _userDataTrackerMock.Verify(
            t => t.InstallUserDataAsync(
                variantMapId,
                profileId,
                gameType,
                It.Is<IEnumerable<ManifestFile>>(files => files.Any(f => f.Hash == "hash-variant")),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
