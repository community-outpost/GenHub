using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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
/// Tests covering the data-safety guarantees of <see cref="UserDataTrackerService"/>: deployed user
/// data must be independent of the CAS object it came from, and a pristine backup must survive a
/// deployed file the user has since modified.
/// </summary>
public sealed partial class UserDataTrackerServiceSafetyTests : IDisposable
{
    private const string TestManifestId = "1.1015255.generalsonline.patch.gamedata";
    private const string TestProfileId = "profile-zh-safety";
    private const string TestVersion = "101525_QFE5";
    private const string TestManifestName = "GameData Patch";
    private const string TestRelativePath = "GeneralsOnlineGameData/splash.bmp";
    private const string TestHash = "hash-splash-safety";
    private const string CasContent = "pristine-cas-content";

    private readonly string _tempDir;
    private readonly string _appDataDir;
    private readonly string _casDir;
    private readonly string _zeroHourDataDir;
    private readonly Mock<IConfigurationProviderService> _configProviderMock;
    private readonly Mock<IFileOperationsService> _fileOperationsMock;
    private readonly Mock<ILogger<UserDataTrackerService>> _loggerMock;
    private readonly Mock<IGamePathProvider> _pathProviderMock;
    private readonly UserDataTrackerService _trackerService;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserDataTrackerServiceSafetyTests"/> class.
    /// </summary>
    public UserDataTrackerServiceSafetyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GenHub_UserDataSafetyTests_" + Guid.NewGuid().ToString("N"));
        _appDataDir = Path.Combine(_tempDir, "AppData");
        _casDir = Path.Combine(_tempDir, "Cas");
        _zeroHourDataDir = Path.Combine(_tempDir, GameSettingsConstants.FolderNames.ZeroHour);

        Directory.CreateDirectory(_appDataDir);
        Directory.CreateDirectory(_casDir);
        Directory.CreateDirectory(_zeroHourDataDir);
        File.WriteAllText(Path.Combine(_casDir, TestHash), CasContent);

        _configProviderMock = new Mock<IConfigurationProviderService>();
        _configProviderMock.Setup(c => c.GetApplicationDataPath()).Returns(_appDataDir);

        _loggerMock = new Mock<ILogger<UserDataTrackerService>>();

        _pathProviderMock = new Mock<IGamePathProvider>();
        _pathProviderMock.Setup(p => p.GetOptionsDirectory(GameType.ZeroHour)).Returns(_zeroHourDataDir);

        _fileOperationsMock = new Mock<IFileOperationsService>();

        // Faithful CAS behaviour: a hard link really shares storage with the object, a copy does not.
        _fileOperationsMock
            .Setup(f => f.LinkFromCasAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<ContentType?>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, string, bool, ContentType?, CancellationToken>((hash, targetPath, useHardLink, contentType, token) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                return Task.FromResult(TryCreateHardLink(Path.Combine(_casDir, hash), targetPath));
            });

        _fileOperationsMock
            .Setup(f => f.CopyFromCasAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ContentType?>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, string, ContentType?, CancellationToken>((hash, targetPath, contentType, token) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(Path.Combine(_casDir, hash), targetPath, overwrite: true);
                return Task.FromResult(true);
            });

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
    /// Verifies that a file installed into the user's game data directory is an independent copy, so
    /// writing to it — as the game engine and GenHub's own settings writer both do — cannot reach the
    /// CAS object that every profile referencing the hash shares.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_UserWritableTarget_DeploysIndependentCopyAsync()
    {
        // Arrange
        var casObjectPath = Path.Combine(_casDir, TestHash);

        // Act
        var result = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            TestProfileId,
            GameType.ZeroHour,
            BuildFiles(),
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var deployedPath = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData", "splash.bmp");
        Assert.True(File.Exists(deployedPath));
        Assert.Equal(CasContent, File.ReadAllText(deployedPath));

        // The game writes into this directory in place; that must not reach the CAS object.
        File.WriteAllText(deployedPath, "engine-rewrote-this-file-with-different-content");

        Assert.Equal(CasContent, File.ReadAllText(casObjectPath));
        Assert.False(result.Data!.InstalledFiles[0].IsHardLink);

        _fileOperationsMock.Verify(
            f => f.LinkFromCasAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<ContentType?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that a deployed file the user has modified is moved aside rather than left in place,
    /// so the pristine backup is still restored over the original path instead of being discarded.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task UninstallUserDataAsync_HashMismatch_PreservesModifiedFileAndRestoresBackupAsync()
    {
        // Arrange
        const string originalUserContent = "the-user-original-file";
        const string modifiedContent = "the-user-edited-this";
        var deployedPath = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData", "splash.bmp");
        Directory.CreateDirectory(Path.GetDirectoryName(deployedPath)!);
        File.WriteAllText(deployedPath, originalUserContent);

        var installResult = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            TestProfileId,
            GameType.ZeroHour,
            BuildFiles(),
            TestVersion,
            TestManifestName,
            CancellationToken.None);
        Assert.True(installResult.Success);

        File.WriteAllText(deployedPath, modifiedContent);
        _fileOperationsMock
            .Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var uninstallResult = await _trackerService.UninstallUserDataAsync(TestManifestId, TestProfileId, CancellationToken.None);

        // Assert
        Assert.True(uninstallResult.Success);
        Assert.Equal(originalUserContent, File.ReadAllText(deployedPath));

        var preservedPath = deployedPath + UserDataConstants.UserModifiedSuffix;
        Assert.True(File.Exists(preservedPath));
        Assert.Equal(modifiedContent, File.ReadAllText(preservedPath));
    }

    /// <summary>
    /// Verifies that a restore failure keeps the backups directory intact, so the user's pristine
    /// originals are still recoverable by hand after a delete-all.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeleteAllUserDataAsync_WhenRestoreFails_RetainsBackupsAsync()
    {
        // Arrange
        var deployedPath = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData", "splash.bmp");
        Directory.CreateDirectory(Path.GetDirectoryName(deployedPath)!);
        File.WriteAllText(deployedPath, "the-user-original-file");

        var installResult = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            TestProfileId,
            GameType.ZeroHour,
            BuildFiles(),
            TestVersion,
            TestManifestName,
            CancellationToken.None);
        Assert.True(installResult.Success);

        _fileOperationsMock
            .Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("hash verification unavailable"));

        // Act
        var deleteResult = await _trackerService.DeleteAllUserDataAsync(CancellationToken.None);

        // Assert
        Assert.True(deleteResult.Success);

        var backupsPath = Path.Combine(_appDataDir, "UserData", "backups");
        Assert.True(Directory.Exists(backupsPath));
        Assert.NotEmpty(Directory.GetFiles(backupsPath, "*", SearchOption.AllDirectories));

        Assert.False(File.Exists(Path.Combine(_appDataDir, "UserData", "index.json")));
        Assert.Empty(Directory.GetFiles(Path.Combine(_appDataDir, "UserData", "manifests"), "*", SearchOption.AllDirectories));
    }

    /// <summary>
    /// Verifies that a clean delete-all still restores the originals and clears the backups, so the
    /// retention path does not become the permanent behaviour.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeleteAllUserDataAsync_WhenRestoresSucceed_ClearsBackupsAsync()
    {
        // Arrange
        const string originalUserContent = "the-user-original-file";
        var deployedPath = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData", "splash.bmp");
        Directory.CreateDirectory(Path.GetDirectoryName(deployedPath)!);
        File.WriteAllText(deployedPath, originalUserContent);

        var installResult = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            TestProfileId,
            GameType.ZeroHour,
            BuildFiles(),
            TestVersion,
            TestManifestName,
            CancellationToken.None);
        Assert.True(installResult.Success);

        // Act
        var deleteResult = await _trackerService.DeleteAllUserDataAsync(CancellationToken.None);

        // Assert
        Assert.True(deleteResult.Success);
        Assert.Equal(originalUserContent, File.ReadAllText(deployedPath));

        var backupsPath = Path.Combine(_appDataDir, "UserData", "backups");
        Assert.True(Directory.Exists(backupsPath));
        Assert.Empty(Directory.GetFiles(backupsPath, "*", SearchOption.AllDirectories));
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLinkWindows(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    [LibraryImport("libc", EntryPoint = "link", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LinkUnix(string existingPath, string newPath);

    private static List<ManifestFile> BuildFiles() =>
    [
        new()
        {
            RelativePath = TestRelativePath,
            Hash = TestHash,
            Size = CasContent.Length,
            InstallTarget = ContentInstallTarget.UserDataDirectory,
        },
    ];

    private static bool TryCreateHardLink(string existingPath, string linkPath)
    {
        try
        {
            if (File.Exists(linkPath))
            {
                File.Delete(linkPath);
            }

            return OperatingSystem.IsWindows()
                ? CreateHardLinkWindows(linkPath, existingPath, IntPtr.Zero)
                : LinkUnix(existingPath, linkPath) == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }
}
