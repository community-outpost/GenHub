using FluentAssertions;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>
/// Unit tests for <see cref="StorageMaintenanceService"/>.
/// </summary>
public class StorageMaintenanceServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly Mock<ILogger<StorageMaintenanceService>> _loggerMock;
    private readonly Mock<IConfigurationProviderService> _configMock;
    private readonly StorageMaintenanceService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageMaintenanceServiceTests"/> class.
    /// </summary>
    public StorageMaintenanceServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"genhub-maint-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);

        _loggerMock = new Mock<ILogger<StorageMaintenanceService>>();
        _configMock = new Mock<IConfigurationProviderService>();
        _configMock.Setup(c => c.GetApplicationDataPath()).Returns(_testRoot);

        _service = new StorageMaintenanceService(_configMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Cleans up test directories.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch
        {
            // Ignore test cleanup exceptions
        }
    }

    /// <summary>
    /// Verifies that publisher studio settings in GenHub/GenHub are migrated to PublisherStudio and empty legacy folder is deleted.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RunMaintenanceAsync_MigratesPublisherStudioSettings_AndDeletesEmptyLegacyDir()
    {
        var legacyDir = Path.Combine(_testRoot, AppConstants.AppName);
        Directory.CreateDirectory(legacyDir);
        var legacySettings = Path.Combine(legacyDir, PublisherStudioConstants.SettingsFileName);
        await File.WriteAllTextAsync(legacySettings, "{\"LastProjectPath\":\"C:/project/catalog.json\"}");

        await _service.RunMaintenanceAsync();

        var targetSettings = Path.Combine(_testRoot, PublisherStudioConstants.StudioFolderName, PublisherStudioConstants.SettingsFileName);
        File.Exists(targetSettings).Should().BeTrue();
        (await File.ReadAllTextAsync(targetSettings)).Should().Contain("C:/project/catalog.json");
        Directory.Exists(legacyDir).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that when target settings already exist and legacy settings are newer, a backup file is created before removing legacy.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RunMaintenanceAsync_BacksUpNewerLegacySettings_WhenTargetAlreadyExists()
    {
        var legacyDir = Path.Combine(_testRoot, AppConstants.AppName);
        Directory.CreateDirectory(legacyDir);
        var legacySettings = Path.Combine(legacyDir, PublisherStudioConstants.SettingsFileName);
        await File.WriteAllTextAsync(legacySettings, "{\"LastProjectPath\":\"C:/newer/catalog.json\"}");

        var targetDir = Path.Combine(_testRoot, PublisherStudioConstants.StudioFolderName);
        Directory.CreateDirectory(targetDir);
        var targetSettings = Path.Combine(targetDir, PublisherStudioConstants.SettingsFileName);
        await File.WriteAllTextAsync(targetSettings, "{\"LastProjectPath\":\"C:/older/catalog.json\"}");

        File.SetLastWriteTimeUtc(targetSettings, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(legacySettings, DateTime.UtcNow);

        await _service.RunMaintenanceAsync();

        var backupFile = Path.Combine(targetDir, $"{PublisherStudioConstants.SettingsFileName}.legacy.bak");
        File.Exists(backupFile).Should().BeTrue();
        (await File.ReadAllTextAsync(backupFile)).Should().Contain("C:/newer/catalog.json");
        File.Exists(legacySettings).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that legacy cache folders are relocated into the centralized Cache directory and legacy folders are cleaned up.
    /// </summary>
    /// <param name="legacyFolderName">The name of the legacy cache directory.</param>
    /// <param name="fileName">The test file name.</param>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData("Images", "test-icon.png")]
    [InlineData(ModBuilderConstants.SampleCacheDirName, "sample-project.zip")]
    public async Task RunMaintenanceAsync_MigratesLegacyCachesToCentralizedCacheFolder(string legacyFolderName, string fileName)
    {
        var legacyDir = Path.Combine(_testRoot, legacyFolderName);
        Directory.CreateDirectory(legacyDir);
        var sourceFile = Path.Combine(legacyDir, fileName);
        await File.WriteAllBytesAsync(sourceFile, [10, 20, 30, 40]);

        await _service.RunMaintenanceAsync();

        var targetFile = Path.Combine(_testRoot, DirectoryNames.Cache, legacyFolderName, fileName);
        File.Exists(targetFile).Should().BeTrue();
        (await File.ReadAllBytesAsync(targetFile)).Should().Equal(10, 20, 30, 40);
        Directory.Exists(legacyDir).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that empty ghost directories like Backups, mappacks, and sub_markers are cleaned up.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RunMaintenanceAsync_CleansEmptyGhostDirectories()
    {
        var backupsDir = Path.Combine(_testRoot, "Backups");
        var mappacksDir = Path.Combine(_testRoot, "mappacks");
        var subMarkersDir = Path.Combine(_testRoot, "sub_markers");

        Directory.CreateDirectory(backupsDir);
        Directory.CreateDirectory(mappacksDir);
        Directory.CreateDirectory(subMarkersDir);

        await _service.RunMaintenanceAsync();

        Directory.Exists(backupsDir).Should().BeFalse();
        Directory.Exists(mappacksDir).Should().BeFalse();
        Directory.Exists(subMarkersDir).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that ghost directories containing files are preserved and never deleted.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RunMaintenanceAsync_PreservesGhostDirectoriesIfNotEmpty()
    {
        var backupsDir = Path.Combine(_testRoot, "Backups");
        Directory.CreateDirectory(backupsDir);
        var keepFile = Path.Combine(backupsDir, "user-backup.zip");
        await File.WriteAllTextAsync(keepFile, "keep me");

        await _service.RunMaintenanceAsync();

        Directory.Exists(backupsDir).Should().BeTrue();
        File.Exists(keepFile).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that orphaned GenHub.Windows.exe is deleted when GenHub.exe exists.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RunMaintenanceAsync_DeletesOrphanedGenHubWindowsExe_WhenCanonicalExeExists()
    {
        var canonicalExe = Path.Combine(_testRoot, "GenHub.exe");
        var orphanedExe = Path.Combine(_testRoot, "GenHub.Windows.exe");
        await File.WriteAllTextAsync(canonicalExe, "new exe");
        await File.WriteAllTextAsync(orphanedExe, "old exe");

        await _service.RunMaintenanceAsync();

        File.Exists(canonicalExe).Should().BeTrue();
        File.Exists(orphanedExe).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that GenHub.Windows.exe is preserved if no other executable exists.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RunMaintenanceAsync_PreservesGenHubWindowsExe_WhenCanonicalExeDoesNotExist()
    {
        var orphanedExe = Path.Combine(_testRoot, "GenHub.Windows.exe");
        await File.WriteAllTextAsync(orphanedExe, "only exe");

        await _service.RunMaintenanceAsync();

        File.Exists(orphanedExe).Should().BeTrue();
    }
}
