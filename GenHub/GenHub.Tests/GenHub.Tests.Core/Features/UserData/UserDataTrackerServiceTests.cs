using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.UserData.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.UserData;

/// <summary>
/// Unit tests for <see cref="UserDataTrackerService"/>.
/// </summary>
public sealed class UserDataTrackerServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _appDataDir;
    private readonly string _zeroHourDataDir;
    private readonly string _generalsDataDir;
    private readonly Mock<IConfigurationProviderService> _configProviderMock;
    private readonly Mock<IFileOperationsService> _fileOperationsMock;
    private readonly Mock<ILogger<UserDataTrackerService>> _loggerMock;
    private readonly Mock<IGamePathProvider> _pathProviderMock;
    private readonly UserDataTrackerService _trackerService;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserDataTrackerServiceTests"/> class.
    /// </summary>
    public UserDataTrackerServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GenHub_UserDataTrackerTests_" + Guid.NewGuid().ToString("N"));
        _appDataDir = Path.Combine(_tempDir, "AppData");
        _zeroHourDataDir = Path.Combine(_tempDir, GameSettingsConstants.FolderNames.ZeroHour);
        _generalsDataDir = Path.Combine(_tempDir, GameSettingsConstants.FolderNames.Generals);

        Directory.CreateDirectory(_appDataDir);
        Directory.CreateDirectory(_zeroHourDataDir);
        Directory.CreateDirectory(_generalsDataDir);

        _configProviderMock = new Mock<IConfigurationProviderService>();
        _configProviderMock.Setup(c => c.GetApplicationDataPath()).Returns(_appDataDir);

        _fileOperationsMock = new Mock<IFileOperationsService>();
        _loggerMock = new Mock<ILogger<UserDataTrackerService>>();

        _pathProviderMock = new Mock<IGamePathProvider>();
        _pathProviderMock.Setup(p => p.GetOptionsDirectory(GameType.ZeroHour)).Returns(_zeroHourDataDir);
        _pathProviderMock.Setup(p => p.GetOptionsDirectory(GameType.Generals)).Returns(_generalsDataDir);

        // Default mock for CAS linking: creates a file at targetPath
        _fileOperationsMock
            .Setup(f => f.LinkFromCasAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<ContentType?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, ContentType?, CancellationToken>((hash, targetPath, useHardLink, contentType, token) =>
            {
                var dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(targetPath, "cas-content-" + hash);
            })
            .ReturnsAsync(true);

        _fileOperationsMock
            .Setup(f => f.VerifyFileHashAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _trackerService = new UserDataTrackerService(
            _configProviderMock.Object,
            _fileOperationsMock.Object,
            _loggerMock.Object,
            _pathProviderMock.Object);
    }

    /// <summary>
    /// Cleans up test resources.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore test cleanup errors
        }
    }

    /// <summary>
    /// Verifies that data patch files targeting UserDataDirectory are placed into the correct Zero Hour Documents directory.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_ZeroHourGameDataPatch_DeploysPreservingSubdirectories()
    {
        // Arrange
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/splash.bmp",
                Hash = "hash-splash-123",
                Size = 1024,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
            new()
            {
                RelativePath = "GeneralsOnlineGameData/500_900_CommunityPatch_CoreINI.big",
                Hash = "hash-big-456",
                Size = 2048,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        // Act
        var result = await _trackerService.InstallUserDataAsync(
            "1.1015255.generalsonline.patch.gamedata",
            "profile-zh-1",
            GameType.ZeroHour,
            files,
            "101525_QFE5",
            "GameData Patch",
            CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.InstalledFiles.Count);

        var expectedSplashPath = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData", "splash.bmp");
        var expectedBigPath = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData", "500_900_CommunityPatch_CoreINI.big");

        Assert.True(File.Exists(expectedSplashPath));
        Assert.True(File.Exists(expectedBigPath));
        Assert.Equal("cas-content-hash-splash-123", File.ReadAllText(expectedSplashPath));
        Assert.Equal("cas-content-hash-big-456", File.ReadAllText(expectedBigPath));
    }

    /// <summary>
    /// Verifies that data patch files targeting UserDataDirectory are placed into the correct Generals Documents directory.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_GeneralsGameDataPatch_DeploysToGeneralsDirectory()
    {
        // Arrange
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/splash.bmp",
                Hash = "hash-gen-splash",
                Size = 512,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        // Act
        var result = await _trackerService.InstallUserDataAsync(
            "1.1015255.generalsonline.patch.gamedata",
            "profile-gen-1",
            GameType.Generals,
            files,
            "101525_QFE5",
            "GameData Patch",
            CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var expectedSplashPath = Path.Combine(_generalsDataDir, "GeneralsOnlineGameData", "splash.bmp");
        Assert.True(File.Exists(expectedSplashPath));
    }

    /// <summary>
    /// Verifies that pre-existing user files are safely backed up before being overwritten, and restored on uninstall.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallAndUninstall_WithExistingUserFile_SafelyBacksUpAndRestoresOriginal()
    {
        // Arrange: simulate pre-existing user file in Documents\...\GeneralsOnlineGameData\splash.bmp
        var gameDataDir = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData");
        Directory.CreateDirectory(gameDataDir);
        var existingSplashPath = Path.Combine(gameDataDir, "splash.bmp");
        var originalUserContent = "original-user-splash-bmp";
        File.WriteAllText(existingSplashPath, originalUserContent);

        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/splash.bmp",
                Hash = "hash-patch-splash",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        // Act 1: Install data patch
        var installResult = await _trackerService.InstallUserDataAsync(
            "1.1015255.generalsonline.patch.gamedata",
            "profile-backup-test",
            GameType.ZeroHour,
            files,
            "101525_QFE5",
            "GameData Patch",
            CancellationToken.None);

        // Assert 1: File overwritten with patch content, backup recorded
        Assert.True(installResult.Success);
        Assert.True(installResult.Data!.InstalledFiles[0].WasOverwritten);
        Assert.NotNull(installResult.Data.InstalledFiles[0].BackupPath);
        Assert.True(File.Exists(installResult.Data.InstalledFiles[0].BackupPath));
        Assert.Equal(originalUserContent, File.ReadAllText(installResult.Data.InstalledFiles[0].BackupPath!));
        Assert.Equal("cas-content-hash-patch-splash", File.ReadAllText(existingSplashPath));

        // Act 2: Uninstall data patch
        var uninstallResult = await _trackerService.UninstallUserDataAsync(
            "1.1015255.generalsonline.patch.gamedata",
            "profile-backup-test",
            CancellationToken.None);

        // Assert 2: Original user content restored
        Assert.True(uninstallResult.Success);
        Assert.True(File.Exists(existingSplashPath));
        Assert.Equal(originalUserContent, File.ReadAllText(existingSplashPath));
    }

    /// <summary>
    /// Verifies that deactivating and reactivating a profile preserves and restores state cleanly.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeactivateAndActivateProfileUserDataAsync_ProperlyTogglesFiles()
    {
        // Arrange
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/patch.big",
                Hash = "hash-big-file",
                Size = 1000,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        var installResult = await _trackerService.InstallUserDataAsync(
            "1.1015255.generalsonline.patch.gamedata",
            "profile-switch-test",
            GameType.ZeroHour,
            files,
            "101525_QFE5",
            "GameData Patch",
            CancellationToken.None);

        Assert.True(installResult.Success);
        var targetBigPath = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData", "patch.big");
        Assert.True(File.Exists(targetBigPath));

        // Act 1: Deactivate profile
        var deactivateResult = await _trackerService.DeactivateProfileUserDataAsync("profile-switch-test", CancellationToken.None);

        // Assert 1: Deactivated files removed, empty subfolder cleaned up, base folder preserved
        Assert.True(deactivateResult.Success);
        Assert.False(File.Exists(targetBigPath));
        Assert.True(Directory.Exists(_zeroHourDataDir));

        // Act 2: Reactivate profile
        var activateResult = await _trackerService.ActivateProfileUserDataAsync("profile-switch-test", CancellationToken.None);

        // Assert 2: Files re-materialized from CAS
        Assert.True(activateResult.Success);
        Assert.True(File.Exists(targetBigPath));
        Assert.Equal("cas-content-hash-big-file", File.ReadAllText(targetBigPath));
    }

    /// <summary>
    /// Verifies that uninstall cleans up empty subdirectories without deleting the root game data folder.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task UninstallUserDataAsync_CleansUpEmptySubdirectory_PreservesRootUserDataFolder()
    {
        // Arrange
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/temp.big",
                Hash = "hash-temp-big",
                Size = 500,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        await _trackerService.InstallUserDataAsync(
            "1.1015255.generalsonline.patch.gamedata",
            "profile-cleanup-test",
            GameType.ZeroHour,
            files,
            "101525_QFE5",
            "GameData Patch",
            CancellationToken.None);

        var subDir = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData");
        Assert.True(Directory.Exists(subDir));

        // Act
        var uninstallResult = await _trackerService.UninstallUserDataAsync(
            "1.1015255.generalsonline.patch.gamedata",
            "profile-cleanup-test",
            CancellationToken.None);

        // Assert
        Assert.True(uninstallResult.Success);
        Assert.False(Directory.Exists(subDir)); // Empty subfolder cleaned up
        Assert.True(Directory.Exists(_zeroHourDataDir)); // Root folder kept safe
    }

    /// <summary>
    /// Verifies that if a user modifies a deployed file, deactivation preserves the modified file and does not overwrite it with the backup.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeactivateProfileUserDataAsync_WhenUserModifiesDeployedFile_PreservesModifiedFileAndDoesNotOverwriteWithBackup()
    {
        // Arrange: pre-existing user file
        var gameDataDir = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData");
        Directory.CreateDirectory(gameDataDir);
        var splashPath = Path.Combine(gameDataDir, "splash.bmp");
        var originalUserContent = "original-user-splash";
        File.WriteAllText(splashPath, originalUserContent);

        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/splash.bmp",
                Hash = "hash-splash-expected",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        var installResult = await _trackerService.InstallUserDataAsync(
            "1.1015255.generalsonline.patch.gamedata",
            "profile-user-edit-test",
            GameType.ZeroHour,
            files,
            "101525_QFE5",
            "GameData Patch",
            CancellationToken.None);

        Assert.True(installResult.Success);
        Assert.True(installResult.Data!.InstalledFiles[0].WasOverwritten);

        // Simulate user editing the deployed splash.bmp after install
        var modifiedContent = "user-edited-splash-content";
        File.WriteAllText(splashPath, modifiedContent);

        // Configure hash verification to fail for the modified file
        _fileOperationsMock
            .Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act: Deactivate profile
        var deactivateResult = await _trackerService.DeactivateProfileUserDataAsync("profile-user-edit-test", CancellationToken.None);

        // Assert: Modified file was preserved and NOT overwritten by the backup
        Assert.True(deactivateResult.Success);
        Assert.True(File.Exists(splashPath));
        Assert.Equal(modifiedContent, File.ReadAllText(splashPath));

        // Backup file remains intact in the backup store for recovery
        var backupPath = installResult.Data.InstalledFiles[0].BackupPath;
        Assert.NotNull(backupPath);
        Assert.True(File.Exists(backupPath));
        Assert.Equal(originalUserContent, File.ReadAllText(backupPath!));
    }

    /// <summary>
    /// Verifies that when a user modifies a restored backup while deactivated, reactivation creates a new backup of the new content and restores it cleanly on subsequent deactivation.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReactivateProfileUserDataAsync_WhenUserModifiesRestoredFileWhileDeactivated_BacksUpNewContentAndRestoresItOnSubsequentDeactivation()
    {
        // Arrange
        var gameDataDir = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData");
        Directory.CreateDirectory(gameDataDir);
        var splashPath = Path.Combine(gameDataDir, "splash.bmp");
        var originalUserContent = "original-user-splash";
        File.WriteAllText(splashPath, originalUserContent);

        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/splash.bmp",
                Hash = "hash-splash-expected",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        // 1. Install profile
        var installResult = await _trackerService.InstallUserDataAsync(
            "1.1015255.generalsonline.patch.gamedata",
            "profile-reactivate-test",
            GameType.ZeroHour,
            files,
            "101525_QFE5",
            "GameData Patch",
            CancellationToken.None);

        Assert.True(installResult.Success);

        // 2. Deactivate profile (restores original backup)
        var deactivateResult = await _trackerService.DeactivateProfileUserDataAsync("profile-reactivate-test", CancellationToken.None);
        Assert.True(deactivateResult.Success);
        Assert.Equal(originalUserContent, File.ReadAllText(splashPath));

        // 3. User modifies the file while profile is inactive
        var newerUserContent = "newer-user-splash-created-while-inactive";
        File.WriteAllText(splashPath, newerUserContent);

        // Configure hash check: deployed CAS file matches "hash-splash-expected", user file does not
        _fileOperationsMock
            .Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, string hash, CancellationToken _) => File.Exists(path) && File.ReadAllText(path).StartsWith("cas-content", StringComparison.Ordinal));

        // 4. Reactivate profile (should back up newerUserContent and deploy CAS file)
        var reactivateResult = await _trackerService.ActivateProfileUserDataAsync("profile-reactivate-test", CancellationToken.None);
        Assert.True(reactivateResult.Success);
        Assert.Equal("cas-content-hash-splash-expected", File.ReadAllText(splashPath));

        // 5. Deactivate profile again (should restore the newerUserContent, NOT the stale original)
        var secondDeactivateResult = await _trackerService.DeactivateProfileUserDataAsync("profile-reactivate-test", CancellationToken.None);
        Assert.True(secondDeactivateResult.Success);
        Assert.True(File.Exists(splashPath));
        Assert.Equal(newerUserContent, File.ReadAllText(splashPath));
    }

    /// <summary>
    /// Verifies that when activation materialization fails, rollback restores the existing user file backup.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ActivateProfileUserDataAsync_WhenMaterializationFails_RollsBackAndRestoresBackup()
    {
        // Arrange
        var gameDataDir = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData");
        Directory.CreateDirectory(gameDataDir);
        var splashPath = Path.Combine(gameDataDir, "splash.bmp");
        var originalUserContent = "original-user-splash";
        File.WriteAllText(splashPath, originalUserContent);

        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/splash.bmp",
                Hash = "hash-splash-expected",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        // 1. Install & Deactivate
        await _trackerService.InstallUserDataAsync(
            "1.1015255.generalsonline.patch.gamedata",
            "profile-fail-materialize-test",
            GameType.ZeroHour,
            files,
            "101525_QFE5",
            "GameData Patch",
            CancellationToken.None);

        await _trackerService.DeactivateProfileUserDataAsync("profile-fail-materialize-test", CancellationToken.None);
        Assert.Equal(originalUserContent, File.ReadAllText(splashPath));

        // 2. Mock CAS materialization failure and hash check
        _fileOperationsMock
            .Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _fileOperationsMock
            .Setup(f => f.LinkFromCasAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<ContentType?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _fileOperationsMock
            .Setup(f => f.CopyFromCasAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ContentType?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // 3. Act: Activate profile
        var activateResult = await _trackerService.ActivateProfileUserDataAsync("profile-fail-materialize-test", CancellationToken.None);

        // 4. Assert: Activation failed, rollback restored user backup
        Assert.False(activateResult.Success);
        Assert.True(File.Exists(splashPath));
        Assert.Equal(originalUserContent, File.ReadAllText(splashPath));
    }

    /// <summary>
    /// Verifies that when activation is cancelled mid-materialization, rollback restores user files and rethrows OperationCanceledException.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ActivateProfileUserDataAsync_WhenCanceled_RollsBackAndRethrows()
    {
        // Arrange
        var gameDataDir = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData");
        Directory.CreateDirectory(gameDataDir);
        var splashPath = Path.Combine(gameDataDir, "splash.bmp");
        var originalUserContent = "original-user-splash";
        File.WriteAllText(splashPath, originalUserContent);

        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/splash.bmp",
                Hash = "hash-splash-expected",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        await _trackerService.InstallUserDataAsync(
            "1.1015255.generalsonline.patch.gamedata",
            "profile-cancel-activate-test",
            GameType.ZeroHour,
            files,
            "101525_QFE5",
            "GameData Patch",
            CancellationToken.None);

        await _trackerService.DeactivateProfileUserDataAsync("profile-cancel-activate-test", CancellationToken.None);
        Assert.Equal(originalUserContent, File.ReadAllText(splashPath));

        using var cts = new CancellationTokenSource();

        _fileOperationsMock
            .Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _fileOperationsMock
            .Setup(f => f.LinkFromCasAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<ContentType?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        _fileOperationsMock
            .Setup(f => f.CopyFromCasAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ContentType?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _trackerService.ActivateProfileUserDataAsync("profile-cancel-activate-test", cts.Token);
        });

        // Rollback restores user backup
        Assert.True(File.Exists(splashPath));
        Assert.Equal(originalUserContent, File.ReadAllText(splashPath));
    }

    /// <summary>
    /// Verifies that when deactivation is cancelled mid-loop, manifest remains active on disk so a retry completes remaining files.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeactivateProfileUserDataAsync_WhenCanceled_PreservesManifestActiveForRetry()
    {
        // Arrange
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/file1.bmp",
                Hash = "hash-file1-expected",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
            new()
            {
                RelativePath = "GeneralsOnlineGameData/file2.bmp",
                Hash = "hash-file2-expected",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        await _trackerService.InstallUserDataAsync(
            "1.1015255.generalsonline.patch.gamedata",
            "profile-cancel-deactivate-test",
            GameType.ZeroHour,
            files,
            "101525_QFE5",
            "GameData Patch",
            CancellationToken.None);

        using var cts = new CancellationTokenSource();

        var verifiedCount = 0;
        _fileOperationsMock
            .Setup(f => f.VerifyFileHashAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((path, hash, token) =>
            {
                if (Interlocked.Increment(ref verifiedCount) > 1)
                {
                    cts.Cancel();
                    return Task.FromException<bool>(new OperationCanceledException(cts.Token));
                }

                return Task.FromResult(true);
            });

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _trackerService.DeactivateProfileUserDataAsync("profile-cancel-deactivate-test", cts.Token);
        });

        // Manifest remains active on disk to allow retry
        var profileData = await _trackerService.GetProfileUserDataAsync("profile-cancel-deactivate-test", CancellationToken.None);
        Assert.True(profileData.Success);
        Assert.Single(profileData.Data!);
        Assert.True(profileData.Data![0].IsActive);
    }

    /// <summary>
    /// Tests that installing a file targeted to UserMapsDirectory normalizes the path correctly and detects conflicts.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_WithUserMapsDirectoryTarget_NormalizesPathAndDetectsConflict()
    {
        // Arrange
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "Maps/CustomMap/map.ini",
                Hash = "hash-custom-map",
                Size = 50,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
        };

        // Act
        var result = await _trackerService.InstallUserDataAsync(
            "1.1015255.generalsonline.patch.custommap",
            "profile-map-test",
            GameType.ZeroHour,
            files,
            "101525_QFE5",
            "Custom Map",
            CancellationToken.None);

        Assert.True(result.Success);

        var expectedPath = Path.Combine(_zeroHourDataDir, "Maps", "CustomMap", "map.ini");
        var conflictResult = await _trackerService.CheckFileConflictAsync(expectedPath);
        Assert.True(conflictResult.Success);
        Assert.Equal("1.1015255.generalsonline.patch.custommap_profile-map-test", conflictResult.Data);
    }

    /// <summary>
    /// Tests that installing a manifest with a relative path escaping user data directory fails containment check without leaving partial artifacts.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_WhenRelativePathEscapesUserDataDirectory_Fails()
    {
        // Arrange
        var validFilePath = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData", "valid.bmp");
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/valid.bmp",
                Hash = "hash-valid",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
            new()
            {
                RelativePath = "../../evil.ini",
                Hash = "hash-evil",
                Size = 10,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        // Act
        var result = await _trackerService.InstallUserDataAsync(
            "1.1015255.generalsonline.patch.evil",
            "profile-evil-test",
            GameType.ZeroHour,
            files,
            "101525_QFE5",
            "Evil Patch",
            CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.False(File.Exists(validFilePath));
    }
}
