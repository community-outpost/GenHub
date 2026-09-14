using System;
using System.IO;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Tests.Core.Collections;
using Xunit;

namespace GenHub.Tests.Core.Features.Storage;

/// <summary>
/// Unit tests for <see cref="FileInstallationLocationTracker"/>.
/// </summary>
[Collection(StorageMigrationStaticStateCollection.Name)]
public class FileInstallationLocationTrackerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _customLocationFile;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileInstallationLocationTrackerTests"/> class.
    /// </summary>
    public FileInstallationLocationTrackerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "FileTrackerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _customLocationFile = Path.Combine(_tempRoot, StorageMigrationConstants.GenHubConfigDirectoryName, StorageMigrationConstants.CustomInstallPathFileName);
        FileInstallationLocationTracker.SetLocationFilePathOverrideForTesting(_customLocationFile);
    }

    /// <summary>
    /// Cleans up temporary resources.
    /// </summary>
    public void Dispose()
    {
        StorageMigrationService.SetCustomInstallRootOverrideForTesting(null);
        FileInstallationLocationTracker.SetLocationFilePathOverrideForTesting(null);

        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best effort cleanup
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort cleanup
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Verifies that <see cref="FileInstallationLocationTracker.GetLocationFilePath"/> returns a path
    /// containing the expected directory and file names.
    /// </summary>
    [Fact]
    public void GetLocationFilePath_ContainsExpectedComponents()
    {
        FileInstallationLocationTracker.SetLocationFilePathOverrideForTesting(null);
        try
        {
            var path = FileInstallationLocationTracker.GetLocationFilePath();

            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.Contains(StorageMigrationConstants.GenHubConfigDirectoryName, path);
            Assert.Contains(StorageMigrationConstants.CustomInstallPathFileName, path);
        }
        finally
        {
            FileInstallationLocationTracker.SetLocationFilePathOverrideForTesting(_customLocationFile);
        }
    }

    /// <summary>
    /// Verifies that non-existent tracker file returns null.
    /// </summary>
    [Fact]
    public void GetRegisteredCustomInstallPath_WhenNotConfigured_ReturnsNull()
    {
        var tracker = new FileInstallationLocationTracker();
        var path = tracker.GetRegisteredCustomInstallPath();

        Assert.Null(path);
    }

    /// <summary>
    /// Verifies that recording an install location writes to the user profile file and can be cleared.
    /// </summary>
    [Fact]
    public void RecordInstallLocation_WhenCustomRoot_RecordsAndClearsSuccessfully()
    {
        StorageMigrationService.SetCustomInstallRootOverrideForTesting(true);

        var tracker = new FileInstallationLocationTracker();
        tracker.RecordInstallLocation();

        var filePath = FileInstallationLocationTracker.GetLocationFilePath();
        Assert.True(File.Exists(filePath));

        tracker.ClearCustomInstallPath();
        Assert.False(File.Exists(filePath));
    }

    /// <summary>
    /// Verifies that when the tracking file contains a UNC or invalid local path, it is safely rejected without error.
    /// </summary>
    [Fact]
    public void GetRegisteredCustomInstallPath_WhenUncOrInvalid_ReturnsNull()
    {
        var dir = Path.GetDirectoryName(_customLocationFile)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(_customLocationFile, "\\\\malicious-server\\share\\fake");

        var tracker = new FileInstallationLocationTracker();
        var result = tracker.GetRegisteredCustomInstallPath();

        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that when the tracking file points to the default install root, it returns null.
    /// </summary>
    [Fact]
    public void GetRegisteredCustomInstallPath_WhenPointsToDefaultRoot_ReturnsNull()
    {
        var dir = Path.GetDirectoryName(_customLocationFile)!;
        Directory.CreateDirectory(dir);
        var defaultDir = Path.Combine(_tempRoot, "DefaultInstallRoot");
        Directory.CreateDirectory(defaultDir);
        File.WriteAllText(Path.Combine(defaultDir, StorageMigrationConstants.VelopackUpdateExe), "stub");

        File.WriteAllText(_customLocationFile, defaultDir);

        try
        {
            StorageMigrationService.SetDefaultInstallRootPathOverrideForTesting(defaultDir);
            var tracker = new FileInstallationLocationTracker();
            var result = tracker.GetRegisteredCustomInstallPath();

            Assert.Null(result);
        }
        finally
        {
            StorageMigrationService.SetDefaultInstallRootPathOverrideForTesting(null);
        }
    }

    /// <summary>
    /// Verifies that RecordCustomInstallPathStatic writes the explicit custom path to the tracking file.
    /// </summary>
    [Fact]
    public void RecordCustomInstallPathStatic_WritesPathToFile()
    {
        var customPath = Path.Combine(_tempRoot, "ExplicitCustomRoot");
        Directory.CreateDirectory(customPath);

        FileInstallationLocationTracker.RecordCustomInstallPathStatic(customPath);

        Assert.True(File.Exists(_customLocationFile));
        Assert.Equal(customPath, File.ReadAllText(_customLocationFile).Trim());
    }
}
