using System.IO;
using System.Threading.Tasks;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Storage;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Storage;

/// <summary>
/// Unit tests for <see cref="InstallationConflictService"/>.
/// </summary>
public class InstallationConflictServiceTests : System.IDisposable
{
    private readonly string _tempRoot;
    private readonly Mock<IInstallationLocationTracker> _mockTracker;
    private readonly Mock<INotificationService> _mockNotificationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstallationConflictServiceTests"/> class.
    /// </summary>
    public InstallationConflictServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "GenHubConflictTests_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _mockTracker = new Mock<IInstallationLocationTracker>();
        _mockNotificationService = new Mock<INotificationService>();
    }

    /// <summary>
    /// Cleans up test resources.
    /// </summary>
    public void Dispose()
    {
        StorageMigrationService.SetCustomInstallRootOverrideForTesting(null);
        StorageMigrationService.WasEarlyAdopted = false;
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
            _mockNotificationService.Object);

        await service.CheckAndResolveConflictsAsync();

        _mockTracker.Verify(t => t.ClearCustomInstallPath(), Times.Once);
        _mockNotificationService.Verify(
            n => n.ShowWarning(
                StorageMigrationConstants.DuplicateInstallationDetectedTitle,
                It.Is<string>(msg => msg.Contains(customDir)),
                It.IsAny<int?>(),
                true),
            Times.Once);
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
            _mockNotificationService.Object);

        await service.CheckAndResolveConflictsAsync();

        _mockTracker.Verify(t => t.ClearCustomInstallPath(), Times.Once);
        _mockNotificationService.Verify(
            n => n.ShowWarning(
                StorageMigrationConstants.DuplicateInstallationDetectedTitle,
                It.Is<string>(msg => msg.Contains("preserved") && msg.Contains(customDir)),
                StorageMigrationConstants.DuplicateInstallationNotificationDismissMs,
                true),
            Times.Once);
    }
}
