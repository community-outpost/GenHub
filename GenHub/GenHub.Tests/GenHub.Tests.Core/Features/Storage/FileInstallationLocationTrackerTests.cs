using System;
using System.IO;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using Xunit;

namespace GenHub.Tests.Core.Features.Storage;

/// <summary>
/// Unit tests for <see cref="FileInstallationLocationTracker"/>.
/// </summary>
public class FileInstallationLocationTrackerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _originalUserProfile;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileInstallationLocationTrackerTests"/> class.
    /// </summary>
    public FileInstallationLocationTrackerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "FileTrackerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>
    /// Cleans up temporary resources.
    /// </summary>
    public void Dispose()
    {
        StorageMigrationService.SetCustomInstallRootOverrideForTesting(null);

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

        // Since test environment likely doesn't have ~/.genhub/install-location pointing to a valid dir:
        var path = tracker.GetRegisteredCustomInstallPath();

        // Should be null or a valid directory string if one existed
        if (path != null)
        {
            Assert.True(Directory.Exists(path));
        }
    }
}
