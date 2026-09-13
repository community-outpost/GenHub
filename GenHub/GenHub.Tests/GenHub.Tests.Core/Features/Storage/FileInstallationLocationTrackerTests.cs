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
    private readonly string _originalUserProfile;
    private readonly string _originalHome;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileInstallationLocationTrackerTests"/> class.
    /// </summary>
    public FileInstallationLocationTrackerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "FileTrackerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE") ?? string.Empty;
        _originalHome = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;

        Environment.SetEnvironmentVariable("USERPROFILE", _tempRoot);
        Environment.SetEnvironmentVariable("HOME", _tempRoot);
    }

    /// <summary>
    /// Cleans up temporary resources.
    /// </summary>
    public void Dispose()
    {
        StorageMigrationService.SetCustomInstallRootOverrideForTesting(null);

        Environment.SetEnvironmentVariable("USERPROFILE", string.IsNullOrEmpty(_originalUserProfile) ? null : _originalUserProfile);
        Environment.SetEnvironmentVariable("HOME", string.IsNullOrEmpty(_originalHome) ? null : _originalHome);

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
        var path = FileInstallationLocationTracker.GetLocationFilePath();

        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.Contains(StorageMigrationConstants.GenHubConfigDirectoryName, path);
        Assert.Contains(StorageMigrationConstants.CustomInstallPathFileName, path);
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
}
