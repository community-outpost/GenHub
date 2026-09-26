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
        // Arrange
        var legacyDir = Path.Combine(_testRoot, AppConstants.AppName);
        Directory.CreateDirectory(legacyDir);
        var legacySettings = Path.Combine(legacyDir, PublisherStudioConstants.SettingsFileName);
        await File.WriteAllTextAsync(legacySettings, "{\"LastProjectPath\":\"C:/project/catalog.json\"}");

        // Act
        await _service.RunMaintenanceAsync();

        // Assert
        var targetSettings = Path.Combine(_testRoot, PublisherStudioConstants.StudioFolderName, PublisherStudioConstants.SettingsFileName);
        File.Exists(targetSettings).Should().BeTrue();
        (await File.ReadAllTextAsync(targetSettings)).Should().Contain("C:/project/catalog.json");
        Directory.Exists(legacyDir).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that images in the root Images directory are moved to Cache/Images and the legacy folder is deleted.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RunMaintenanceAsync_MigratesImageCache_AndDeletesEmptyLegacyDir()
    {
        // Arrange
        var legacyImagesDir = Path.Combine(_testRoot, "Images");
        Directory.CreateDirectory(legacyImagesDir);
        var dummyImage = Path.Combine(legacyImagesDir, "test-icon.png");
        await File.WriteAllBytesAsync(dummyImage, [1, 2, 3, 4]);

        // Act
        await _service.RunMaintenanceAsync();

        // Assert
        var targetImage = Path.Combine(_testRoot, DirectoryNames.Cache, "Images", "test-icon.png");
        File.Exists(targetImage).Should().BeTrue();
        (await File.ReadAllBytesAsync(targetImage)).Should().Equal(1, 2, 3, 4);
        Directory.Exists(legacyImagesDir).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that ModBuilder sample caches are migrated to Cache/ModBuilderSampleCache.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RunMaintenanceAsync_MigratesModBuilderSampleCache_AndDeletesEmptyLegacyDir()
    {
        // Arrange
        var legacyCacheDir = Path.Combine(_testRoot, ModBuilderConstants.SampleCacheDirName);
        Directory.CreateDirectory(legacyCacheDir);
        var dummyZip = Path.Combine(legacyCacheDir, "sample-project.zip");
        await File.WriteAllBytesAsync(dummyZip, [5, 6, 7, 8]);

        // Act
        await _service.RunMaintenanceAsync();

        // Assert
        var targetZip = Path.Combine(_testRoot, DirectoryNames.Cache, ModBuilderConstants.SampleCacheDirName, "sample-project.zip");
        File.Exists(targetZip).Should().BeTrue();
        (await File.ReadAllBytesAsync(targetZip)).Should().Equal(5, 6, 7, 8);
        Directory.Exists(legacyCacheDir).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that empty ghost directories like Backups, mappacks, and sub_markers are cleaned up.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RunMaintenanceAsync_CleansEmptyGhostDirectories()
    {
        // Arrange
        var backupsDir = Path.Combine(_testRoot, "Backups");
        var mappacksDir = Path.Combine(_testRoot, "mappacks");
        var subMarkersDir = Path.Combine(_testRoot, "sub_markers");

        Directory.CreateDirectory(backupsDir);
        Directory.CreateDirectory(mappacksDir);
        Directory.CreateDirectory(subMarkersDir);

        // Act
        await _service.RunMaintenanceAsync();

        // Assert
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
        // Arrange
        var backupsDir = Path.Combine(_testRoot, "Backups");
        Directory.CreateDirectory(backupsDir);
        var keepFile = Path.Combine(backupsDir, "user-backup.zip");
        await File.WriteAllTextAsync(keepFile, "keep me");

        // Act
        await _service.RunMaintenanceAsync();

        // Assert
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
        // Arrange
        var canonicalExe = Path.Combine(_testRoot, "GenHub.exe");
        var orphanedExe = Path.Combine(_testRoot, "GenHub.Windows.exe");
        await File.WriteAllTextAsync(canonicalExe, "new exe");
        await File.WriteAllTextAsync(orphanedExe, "old exe");

        // Act
        await _service.RunMaintenanceAsync();

        // Assert
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
        // Arrange
        var orphanedExe = Path.Combine(_testRoot, "GenHub.Windows.exe");
        await File.WriteAllTextAsync(orphanedExe, "only exe");

        // Act
        await _service.RunMaintenanceAsync();

        // Assert
        File.Exists(orphanedExe).Should().BeTrue();
    }
}
