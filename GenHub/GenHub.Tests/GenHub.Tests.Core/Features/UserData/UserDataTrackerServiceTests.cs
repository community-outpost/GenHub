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
}
