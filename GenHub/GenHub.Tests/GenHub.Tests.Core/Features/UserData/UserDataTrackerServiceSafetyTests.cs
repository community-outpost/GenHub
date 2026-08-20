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

        _fileOperationsMock
            .Setup(f => f.CheckFileHashAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileHashVerification.Match);

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
            .Setup(f => f.CheckFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileHashVerification.Mismatch);

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
    /// A deployed file whose hash could not be computed at all — an IO error, or the running game
    /// briefly holding it open — is not evidence that the user changed it. Moving it aside and
    /// restoring over it would churn a pristine file and log a preserved edit that never happened,
    /// so the file and its backup are both left alone.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task UninstallUserDataAsync_WhenVerificationFails_LeavesDeployedFileUntouchedAsync()
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

        var backupPath = installResult.Data!.InstalledFiles[0].BackupPath;
        Assert.NotNull(backupPath);

        _fileOperationsMock
            .Setup(f => f.CheckFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileHashVerification.Failed);

        // Act
        var uninstallResult = await _trackerService.UninstallUserDataAsync(TestManifestId, TestProfileId, CancellationToken.None);

        // Assert
        Assert.False(uninstallResult.Success);
        Assert.False(File.Exists(deployedPath + UserDataConstants.UserModifiedSuffix));
        Assert.Equal(CasContent, File.ReadAllText(deployedPath));
        Assert.True(File.Exists(backupPath));
        Assert.Equal(originalUserContent, File.ReadAllText(backupPath!));
    }

    /// <summary>
    /// Pins the dangerous window an uninstall opens: the deployed file has already been moved aside
    /// and the restore of the pristine original then fails, leaving the original path empty. The
    /// uninstall must report that failure and keep its tracking data, because the manifest is the
    /// only record tying a machine-named backup to the path it belongs at.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task UninstallUserDataAsync_WhenRestoreFailsAfterMoveAside_ReportsFailureAndKeepsTrackingDataAsync()
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

        var backupPath = installResult.Data!.InstalledFiles[0].BackupPath!;

        // The backup disappears in the window between the move-aside and the restore.
        _fileOperationsMock
            .Setup(f => f.CheckFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                File.Delete(backupPath);
                return FileHashVerification.Mismatch;
            });

        // Act
        var uninstallResult = await _trackerService.UninstallUserDataAsync(TestManifestId, TestProfileId, CancellationToken.None);

        // Assert
        Assert.False(uninstallResult.Success);
        Assert.False(File.Exists(deployedPath));

        var preservedPath = deployedPath + UserDataConstants.UserModifiedSuffix;
        Assert.True(File.Exists(preservedPath));
        Assert.Equal(CasContent, File.ReadAllText(preservedPath));

        var manifestsPath = Path.Combine(_appDataDir, "UserData", "manifests");
        Assert.NotEmpty(Directory.GetFiles(manifestsPath, "*", SearchOption.AllDirectories));
    }

    /// <summary>
    /// Profile cleanup runs the same uninstall, so it must not report success while an original the
    /// user never asked to lose is still sitting in the backups tree. Every caller above it reads
    /// this result and nothing else.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CleanupProfileAsync_WhenRestoreFails_ReportsTheUnfinishedUninstallAsync()
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

        var backupPath = installResult.Data!.InstalledFiles[0].BackupPath!;

        // The backup disappears in the window between the move-aside and the restore.
        _fileOperationsMock
            .Setup(f => f.CheckFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                File.Delete(backupPath);
                return FileHashVerification.Mismatch;
            });

        // Act
        var cleanupResult = await _trackerService.CleanupProfileAsync(TestProfileId, CancellationToken.None);

        // Assert
        Assert.False(cleanupResult.Success);
        Assert.Contains(Path.Combine(_appDataDir, "UserData", "backups"), cleanupResult.FirstError);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_appDataDir, "UserData", "manifests"), "*", SearchOption.AllDirectories));
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
            .Setup(f => f.CheckFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileHashVerification.Failed);

        // Act
        var deleteResult = await _trackerService.DeleteAllUserDataAsync(CancellationToken.None);

        // Assert
        var backupsPath = Path.Combine(_appDataDir, "UserData", "backups");

        // The caller must be told, and told where: "all user data deleted successfully" is a lie
        // while the user's pristine originals are still sitting in the backups folder.
        Assert.False(deleteResult.Success);
        Assert.Contains(backupsPath, deleteResult.FirstError);

        Assert.True(Directory.Exists(backupsPath));
        Assert.NotEmpty(Directory.GetFiles(backupsPath, "*", SearchOption.AllDirectories));

        // The manifests and the index are the only map from a machine-named backup file back to the
        // path it belongs at, so retaining the backups while deleting them would strand them.
        Assert.True(File.Exists(Path.Combine(_appDataDir, "UserData", "index.json")));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_appDataDir, "UserData", "manifests"), "*", SearchOption.AllDirectories));
    }

    /// <summary>
    /// A delete-all that retains backups keeps its tracking data, so retrying it once the restores
    /// can succeed must still finish the job rather than leave the tracking directory behind forever.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeleteAllUserDataAsync_RetriedAfterRetention_ClearsEverythingAsync()
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

        _fileOperationsMock
            .Setup(f => f.CheckFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileHashVerification.Failed);

        var firstAttempt = await _trackerService.DeleteAllUserDataAsync(CancellationToken.None);
        Assert.False(firstAttempt.Success);

        _fileOperationsMock
            .Setup(f => f.CheckFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileHashVerification.Match);

        // Act
        var retry = await _trackerService.DeleteAllUserDataAsync(CancellationToken.None);

        // Assert
        Assert.True(retry.Success);
        Assert.Equal(originalUserContent, File.ReadAllText(deployedPath));
        Assert.Empty(Directory.GetFiles(Path.Combine(_appDataDir, "UserData"), "*", SearchOption.AllDirectories));
    }

    /// <summary>
    /// Deactivation puts the user's original back at its own path, which consumes the backup. Keeping
    /// the backup file and its recorded path would make the following uninstall read that restored
    /// original as a user modification, move the byte-identical file aside and restore a duplicate.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeactivateThenUninstall_DoesNotDuplicateTheRestoredOriginalAsync()
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

        var backupPath = installResult.Data!.InstalledFiles[0].BackupPath;
        Assert.NotNull(backupPath);

        // Only the deployed CAS content matches the recorded hash; the user's own file does not.
        _fileOperationsMock
            .Setup(f => f.CheckFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, string hash, CancellationToken _) =>
                File.Exists(path) && File.ReadAllText(path) == CasContent
                    ? FileHashVerification.Match
                    : FileHashVerification.Mismatch);

        // Act
        var deactivateResult = await _trackerService.DeactivateProfileUserDataAsync(TestProfileId, CancellationToken.None);
        Assert.True(deactivateResult.Success);
        Assert.Equal(originalUserContent, File.ReadAllText(deployedPath));
        Assert.False(File.Exists(backupPath));

        var uninstallResult = await _trackerService.UninstallUserDataAsync(TestManifestId, TestProfileId, CancellationToken.None);

        // Assert
        Assert.True(uninstallResult.Success);
        Assert.Equal(originalUserContent, File.ReadAllText(deployedPath));
        Assert.False(File.Exists(deployedPath + UserDataConstants.UserModifiedSuffix));
    }

    /// <summary>
    /// The restore is what protects the user's data; deleting the consumed backup afterwards is
    /// housekeeping. A delete that fails must not report the restore as failed, because the retry
    /// would read the restored original as a modification and duplicate it.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task UninstallUserDataAsync_WhenConsumedBackupCannotBeDeleted_StillReportsSuccessAsync()
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

        var backupPath = installResult.Data!.InstalledFiles[0].BackupPath!;
        var backupDir = Path.GetDirectoryName(backupPath)!;

        // Deleting the backup has to fail while reading it still works: an open handle does that on
        // Windows, and a directory the process may not write to does it everywhere else.
        FileStream? openBackupHandle = null;
        UnixFileMode? originalDirectoryMode = null;
        string? probePath = null;
        if (OperatingSystem.IsWindows())
        {
            openBackupHandle = new FileStream(backupPath, System.IO.FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        else
        {
            probePath = Path.Combine(backupDir, "delete-permission-probe");
            File.WriteAllText(probePath, string.Empty);

            originalDirectoryMode = File.GetUnixFileMode(backupDir);
            File.SetUnixFileMode(backupDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            if (DeleteSucceeds(probePath))
            {
                // The mode is advisory for this process: root, and anything else holding
                // CAP_DAC_OVERRIDE, deletes regardless. There is no failing delete left to set up,
                // so the scenario cannot be reached here rather than the product being wrong.
                File.SetUnixFileMode(backupDir, originalDirectoryMode.Value);
                return;
            }
        }

        try
        {
            // Act
            var uninstallResult = await _trackerService.UninstallUserDataAsync(TestManifestId, TestProfileId, CancellationToken.None);

            // Assert
            Assert.True(uninstallResult.Success);
            Assert.True(File.Exists(backupPath));
            Assert.Equal(originalUserContent, File.ReadAllText(deployedPath));
            Assert.False(File.Exists(deployedPath + UserDataConstants.UserModifiedSuffix));
        }
        finally
        {
            openBackupHandle?.Dispose();
            if (!OperatingSystem.IsWindows() && originalDirectoryMode.HasValue)
            {
                File.SetUnixFileMode(backupDir, originalDirectoryMode.Value);
            }

            if (probePath is not null)
            {
                File.Delete(probePath);
            }
        }
    }

    /// <summary>
    /// A cancelled delete-all must abort before any tracking metadata is destroyed. Swallowing the
    /// cancellation and carrying on wipes the manifests and the index while the backups they describe
    /// are still on disk, leaving the user's originals unrecoverable by anything but hand.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeleteAllUserDataAsync_WhenCancelledMidCleanup_KeepsTrackingMetadataAsync()
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

        using var cts = new CancellationTokenSource();
        _fileOperationsMock
            .Setup(f => f.CheckFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((_, _, token) =>
            {
                cts.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult(FileHashVerification.Match);
            });

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _trackerService.DeleteAllUserDataAsync(cts.Token));

        // Assert
        Assert.True(File.Exists(Path.Combine(_appDataDir, "UserData", "index.json")));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_appDataDir, "UserData", "manifests"), "*", SearchOption.AllDirectories));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_appDataDir, "UserData", "backups"), "*", SearchOption.AllDirectories));
    }

    /// <summary>
    /// Cancellation that lands on the manifest read itself must abort the delete-all too. Treating
    /// the cancelled read as an unreadable manifest turns an abort into a retention decision and
    /// carries on into the step that removes the tracking data.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeleteAllUserDataAsync_WhenCancelledLoadingManifest_KeepsTrackingMetadataAsync()
    {
        // Arrange
        const string secondHash = "hash-splash-safety-second";
        const string secondRelativePath = "GeneralsOnlineGameData/loading.bmp";
        File.WriteAllText(Path.Combine(_casDir, secondHash), CasContent);

        var deployedPath = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData", "splash.bmp");
        Directory.CreateDirectory(Path.GetDirectoryName(deployedPath)!);
        File.WriteAllText(deployedPath, "the-user-original-file");

        Assert.True((await _trackerService.InstallUserDataAsync(
            TestManifestId,
            TestProfileId,
            GameType.ZeroHour,
            BuildFiles(),
            TestVersion,
            TestManifestName,
            CancellationToken.None)).Success);

        Assert.True((await _trackerService.InstallUserDataAsync(
            TestManifestId + ".loading",
            TestProfileId,
            GameType.ZeroHour,
            BuildFiles(secondRelativePath, secondHash),
            TestVersion,
            TestManifestName,
            CancellationToken.None)).Success);

        // Cancel while the first installation is being cleaned up, so the cancellation is first
        // observed by the read of the second installation's manifest.
        using var cts = new CancellationTokenSource();
        _fileOperationsMock
            .Setup(f => f.CheckFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                cts.Cancel();
                return FileHashVerification.Match;
            });

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _trackerService.DeleteAllUserDataAsync(cts.Token));

        // Assert
        Assert.True(File.Exists(Path.Combine(_appDataDir, "UserData", "index.json")));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_appDataDir, "UserData", "manifests"), "*", SearchOption.AllDirectories));
    }

    /// <summary>
    /// An index key whose manifest is already gone has nothing left to restore, so it must not put
    /// delete-all into the retention path forever: "Delete All Application Data" would then never be
    /// able to finish on an installation with one stale entry.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeleteAllUserDataAsync_WithStaleIndexEntry_StillClearsEverythingAsync()
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

        var userDataPath = Path.Combine(_appDataDir, "UserData");
        foreach (var manifestFile in Directory.GetFiles(Path.Combine(userDataPath, "manifests"), "*", SearchOption.AllDirectories))
        {
            File.Delete(manifestFile);
        }

        // Act
        var deleteResult = await _trackerService.DeleteAllUserDataAsync(CancellationToken.None);

        // Assert
        Assert.True(deleteResult.Success);
        Assert.Empty(Directory.GetFiles(userDataPath, "*", SearchOption.AllDirectories));
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

    private static List<ManifestFile> BuildFiles() => BuildFiles(TestRelativePath, TestHash);

    private static List<ManifestFile> BuildFiles(string relativePath, string hash) =>
    [
        new()
        {
            RelativePath = relativePath,
            Hash = hash,
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
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reports whether a delete inside a directory whose mode was just tightened still goes through.
    /// A process holding CAP_DAC_OVERRIDE - root in a dev container or a privileged CI image - is
    /// not bound by the mode, so a test that assumed the delete would fail would instead report the
    /// product as broken.
    /// </summary>
    /// <param name="path">The probe file the tightened directory is meant to protect.</param>
    /// <returns><c>true</c> when the delete succeeded despite the directory mode.</returns>
    private static bool DeleteSucceeds(string path)
    {
        try
        {
            File.Delete(path);
            return !File.Exists(path);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
