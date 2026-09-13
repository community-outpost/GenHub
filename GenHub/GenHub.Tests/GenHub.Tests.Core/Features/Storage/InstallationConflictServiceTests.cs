using System;
using System.IO;
using System.Threading.Tasks;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Storage;
using GenHub.Tests.Core.Collections;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Storage;

/// <summary>
/// Unit tests for <see cref="InstallationConflictService"/>.
/// </summary>
[Collection(StorageMigrationStaticStateCollection.Name)]
public class InstallationConflictServiceTests : System.IDisposable
{
    private readonly string _tempRoot;
    private readonly Mock<IInstallationLocationTracker> _mockTracker;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<IUserSettingsService> _mockUserSettingsService;
    private readonly string _defaultRoot;
    private readonly string _markerPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstallationConflictServiceTests"/> class.
    /// </summary>
    public InstallationConflictServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "GenHubConflictTests_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _defaultRoot = Path.Combine(_tempRoot, "DefaultDataRoot");
        Directory.CreateDirectory(_defaultRoot);
        StorageMigrationService.SetDefaultDataRootOverrideForTesting(_defaultRoot);

        _markerPath = Path.Combine(_defaultRoot, StorageMigrationConstants.AdoptionPendingMarkerFileName);
        _mockTracker = new Mock<IInstallationLocationTracker>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockUserSettingsService = new Mock<IUserSettingsService>();

        StorageMigrationService.SetDefaultInstallRootOverrideForTesting(true);
        StorageMigrationService.SetDefaultInstallRootPathOverrideForTesting(_defaultRoot);
    }

    /// <summary>
    /// Cleans up test resources.
    /// </summary>
    public void Dispose()
    {
        StorageMigrationService.SetCustomInstallRootOverrideForTesting(null);
        StorageMigrationService.SetDefaultDataRootOverrideForTesting(null);
        StorageMigrationService.SetDefaultInstallRootOverrideForTesting(null);
        StorageMigrationService.SetDefaultInstallRootPathOverrideForTesting(null);
        StorageMigrationService.WasEarlyAdopted = false;

        try
        {
            if (File.Exists(_markerPath))
            {
                File.Delete(_markerPath);
            }
        }
        catch (IOException)
        {
            // Ignore marker cleanup errors
        }

        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }
        catch (IOException)
        {
            // Ignore temp cleanup errors.
        }
    }

    /// <summary>
    /// Verifies that when running in a custom install root, the location is recorded and no warnings are shown.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndResolveConflictsAsync_WhenRunningInCustomRoot_RecordsLocationAndExits()
    {
        StorageMigrationService.SetCustomInstallRootOverrideForTesting(true);

        var service = new InstallationConflictService(
            _mockTracker.Object,
            _mockNotificationService.Object);

        await service.CheckAndResolveConflictsAsync();

        _mockTracker.Verify(t => t.RecordInstallLocation(), Times.Once);
        _mockNotificationService.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that when running in the default root with no custom install registered, no actions are taken.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndResolveConflictsAsync_WhenNoConflict_DoesNothing()
    {
        StorageMigrationService.SetCustomInstallRootOverrideForTesting(false);
        _mockTracker.Setup(t => t.GetRegisteredCustomInstallPath()).Returns((string?)null);

        var service = new InstallationConflictService(
            _mockTracker.Object,
            _mockNotificationService.Object);

        await service.CheckAndResolveConflictsAsync();

        _mockTracker.Verify(t => t.RecordInstallLocation(), Times.Never);
        _mockTracker.Verify(t => t.ClearCustomInstallPath(), Times.Never);
        _mockNotificationService.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that when running in the default root and a registered custom install root with valid Velopack markers
    /// is detected, user data is adopted, tracking markers are cleared, and a notification is displayed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndResolveConflictsAsync_WhenDuplicateDetected_AdoptsDataAndClearsTracker()
    {
        StorageMigrationService.SetCustomInstallRootOverrideForTesting(false);

        var customDir = Path.Combine(_tempRoot, "CustomInstall");
        Directory.CreateDirectory(customDir);
        File.WriteAllText(Path.Combine(customDir, StorageMigrationConstants.VelopackUpdateExe), "stub");
        File.WriteAllText(Path.Combine(customDir, FileTypes.SettingsFileName), "{\"custom\":true}");

        _mockTracker.Setup(t => t.GetRegisteredCustomInstallPath()).Returns(customDir);

        var service = new InstallationConflictService(
            _mockTracker.Object,
            _mockNotificationService.Object,
            _mockUserSettingsService.Object);

        await service.CheckAndResolveConflictsAsync();

        _mockUserSettingsService.Verify(s => s.Reload(), Times.Once);
        _mockTracker.Verify(t => t.ClearCustomInstallPath(), Times.Once);
        _mockNotificationService.Verify(
            n => n.ShowWarning(
                StorageMigrationConstants.DuplicateInstallationDetectedTitle,
                It.Is<string>(msg => msg.Contains("preserved") && msg.Contains(customDir)),
                It.IsAny<int?>(),
                true),
            Times.Once);

        var defaultSettings = Path.Combine(_defaultRoot, FileTypes.SettingsFileName);
        Assert.True(File.Exists(defaultSettings));
    }

    /// <summary>
    /// Verifies that when WasEarlyAdopted is true, the notification displays preserved message and badge is true.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndResolveConflictsAsync_WhenWasEarlyAdopted_ShowsPreservedNotificationWithBadge()
    {
        StorageMigrationService.SetCustomInstallRootOverrideForTesting(false);
        StorageMigrationService.WasEarlyAdopted = true;

        var customDir = Path.Combine(_tempRoot, "CustomInstallEarlyAdopted");
        Directory.CreateDirectory(customDir);
        File.WriteAllText(Path.Combine(customDir, StorageMigrationConstants.VelopackUpdateExe), "stub");

        _mockTracker.Setup(t => t.GetRegisteredCustomInstallPath()).Returns(customDir);

        var service = new InstallationConflictService(
            _mockTracker.Object,
            _mockNotificationService.Object,
            _mockUserSettingsService.Object);

        await service.CheckAndResolveConflictsAsync();

        _mockUserSettingsService.Verify(s => s.Reload(), Times.Once);
        _mockTracker.Verify(t => t.ClearCustomInstallPath(), Times.Once);
        _mockNotificationService.Verify(
            n => n.ShowWarning(
                StorageMigrationConstants.DuplicateInstallationDetectedTitle,
                It.Is<string>(msg => msg.Contains("preserved") && msg.Contains(customDir)),
                StorageMigrationConstants.DuplicateInstallationNotificationAutoDismissMs,
                true),
            Times.Once);
    }

    /// <summary>
    /// Verifies that when early adoption was not performed and adoption is declined (existing default data exists),
    /// the adoption marker and custom install tracker are cleared, and a detected-format notification is displayed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndResolveConflictsAsync_WhenNotEarlyAdoptedAndNotAdopting_ClearsMarkerAndTrackerAndShowsDetectedNotification()
    {
        StorageMigrationService.SetCustomInstallRootOverrideForTesting(false);

        var customDir = Path.Combine(_tempRoot, "CustomInstallDeclined");
        Directory.CreateDirectory(customDir);
        File.WriteAllText(Path.Combine(customDir, StorageMigrationConstants.VelopackUpdateExe), "stub");
        File.WriteAllText(Path.Combine(customDir, FileTypes.SettingsFileName), "{\"custom\":true}");

        // Create default settings so hasExistingData is true and adoption is not attempted
        File.WriteAllText(Path.Combine(_defaultRoot, FileTypes.SettingsFileName), "{\"default\":true}");

        // Pre-write a stale marker file to verify that the decline branch actively cleans it up
        File.WriteAllText(_markerPath, customDir);

        _mockTracker.Setup(t => t.GetRegisteredCustomInstallPath()).Returns(customDir);

        var service = new InstallationConflictService(
            _mockTracker.Object,
            _mockNotificationService.Object,
            _mockUserSettingsService.Object);

        await service.CheckAndResolveConflictsAsync();

        _mockTracker.Verify(t => t.ClearCustomInstallPath(), Times.Once);
        Assert.False(File.Exists(_markerPath));
        _mockNotificationService.Verify(
            n => n.ShowWarning(
                StorageMigrationConstants.DuplicateInstallationDetectedTitle,
                It.Is<string>(msg => !msg.Contains("preserved") && msg.Contains(customDir)),
                StorageMigrationConstants.DuplicateInstallationNotificationAutoDismissMs,
                true),
            Times.Once);
        _mockUserSettingsService.Verify(s => s.Reload(), Times.Never);
    }

    /// <summary>
    /// Verifies that when writing the adoption marker fails, adoption is aborted,
    /// the tracker is preserved, and the detected-conflict notification is shown.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndResolveConflictsAsync_WhenAdoptionMarkerWriteFails_AbortsAdoptionAndNotifies()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        StorageMigrationService.SetCustomInstallRootOverrideForTesting(false);

        var customDir = Path.Combine(_tempRoot, "CustomInstallMarkerFail");
        Directory.CreateDirectory(customDir);
        File.WriteAllText(Path.Combine(customDir, StorageMigrationConstants.VelopackUpdateExe), "stub");
        File.WriteAllText(Path.Combine(customDir, FileTypes.SettingsFileName), "{\"custom\":true}");

        _mockTracker.Setup(t => t.GetRegisteredCustomInstallPath()).Returns(customDir);

        var service = new InstallationConflictService(
            _mockTracker.Object,
            _mockNotificationService.Object,
            _mockUserSettingsService.Object);

        try
        {
            File.SetUnixFileMode(_defaultRoot, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            var isActuallyDenied = false;
            try
            {
                File.WriteAllText(_markerPath, "test");
            }
            catch (UnauthorizedAccessException)
            {
                isActuallyDenied = true;
            }
            catch (IOException)
            {
                isActuallyDenied = true;
            }

            if (!isActuallyDenied)
            {
                return;
            }

            await service.CheckAndResolveConflictsAsync();

            _mockTracker.Verify(t => t.ClearCustomInstallPath(), Times.Never);
            _mockUserSettingsService.Verify(s => s.Reload(), Times.Never);
            _mockNotificationService.Verify(
                n => n.ShowWarning(
                    StorageMigrationConstants.DuplicateInstallationDetectedTitle,
                    It.Is<string>(msg => !msg.Contains("preserved") && msg.Contains(customDir)),
                    StorageMigrationConstants.DuplicateInstallationNotificationAutoDismissMs,
                    true),
                Times.Once);
        }
        finally
        {
            File.SetUnixFileMode(_defaultRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>
    /// Verifies that when an inaccessible directory prevents full adoption inspection (tri-state null),
    /// the adoption marker and tracker record are NOT cleared, preserving retry state.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndResolveConflictsAsync_WhenUnadoptedDataFailsInspection_PreservesMarkerAndTracker()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        StorageMigrationService.SetCustomInstallRootOverrideForTesting(false);

        var customDir = Path.Combine(_tempRoot, "CustomInstallLocked");
        Directory.CreateDirectory(customDir);
        File.WriteAllText(Path.Combine(customDir, StorageMigrationConstants.VelopackUpdateExe), "stub");
        File.WriteAllText(Path.Combine(customDir, FileTypes.SettingsFileName), "{\"custom\":true}");

        var defaultProfiles = Path.Combine(_defaultRoot, DirectoryNames.Profiles);
        Directory.CreateDirectory(defaultProfiles);

        var lockedProfiles = Path.Combine(customDir, DirectoryNames.Profiles, "LockedSub");
        Directory.CreateDirectory(lockedProfiles);
        File.WriteAllText(Path.Combine(lockedProfiles, "p.json"), "{}");

        _mockTracker.Setup(t => t.GetRegisteredCustomInstallPath()).Returns(customDir);

        var service = new InstallationConflictService(
            _mockTracker.Object,
            _mockNotificationService.Object);

        try
        {
            File.SetUnixFileMode(lockedProfiles, UnixFileMode.None);

            var isActuallyDenied = false;
            try
            {
                _ = Directory.GetFiles(lockedProfiles);
            }
            catch (UnauthorizedAccessException)
            {
                isActuallyDenied = true;
            }
            catch (IOException)
            {
                isActuallyDenied = true;
            }

            if (!isActuallyDenied)
            {
                return;
            }

            await service.CheckAndResolveConflictsAsync();

            // Tracker must NOT be cleared because HasUnadoptedUserData returned null (error)
            _mockTracker.Verify(t => t.ClearCustomInstallPath(), Times.Never);

            // Adoption marker must still exist for retry
            Assert.True(File.Exists(_markerPath));
        }
        finally
        {
            File.SetUnixFileMode(lockedProfiles, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>
    /// Verifies that when early adopt records the custom path in the adoption marker, but subsequent
    /// tracker queries return the default install root (e.g. URI scheme re-registered to default),
    /// the conflict service resolves the conflict using the marker, notifies the user, and does
    /// not delete the marker until adoption is complete.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndResolveConflictsAsync_WhenTrackerResolvesToDefaultRootAfterEarlyAdopt_ResolvesFromMarker()
    {
        StorageMigrationService.SetCustomInstallRootOverrideForTesting(false);

        var customDir = Path.Combine(_tempRoot, "CustomInstall");
        Directory.CreateDirectory(customDir);
        File.WriteAllText(Path.Combine(customDir, StorageMigrationConstants.VelopackUpdateExe), "stub");
        File.WriteAllText(Path.Combine(customDir, FileTypes.SettingsFileName), "{\"custom\":true}");

        // Simulate: early adopt ran and wrote marker pointing to customDir
        File.WriteAllText(_markerPath, customDir);
        StorageMigrationService.WasEarlyAdopted = true;

        // Simulate: URI scheme overwritten to default install root, so tracker returns default root
        _mockTracker.Setup(t => t.GetRegisteredCustomInstallPath()).Returns(_defaultRoot);

        var service = new InstallationConflictService(
            _mockTracker.Object,
            _mockNotificationService.Object,
            _mockUserSettingsService.Object);

        await service.CheckAndResolveConflictsAsync();

        // Notification must still fire acknowledging the custom installation
        _mockNotificationService.Verify(
            n => n.ShowWarning(
                StorageMigrationConstants.DuplicateInstallationDetectedTitle,
                It.Is<string>(msg => msg.Contains("preserved") && msg.Contains(customDir)),
                It.IsAny<int?>(),
                true),
            Times.Once);

        _mockUserSettingsService.Verify(s => s.Reload(), Times.Once);
        _mockTracker.Verify(t => t.ClearCustomInstallPath(), Times.Once);
        Assert.False(File.Exists(_markerPath));
    }
}
