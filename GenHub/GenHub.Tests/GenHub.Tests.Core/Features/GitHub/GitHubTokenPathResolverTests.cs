using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Features.GitHub.Services;
using GenHub.Tests.Core.Collections;
using System;
using System.IO;
using Xunit;

namespace GenHub.Tests.Core.Features.GitHub;

/// <summary>
/// Contains unit tests for <see cref="GitHubTokenPathResolver"/>.
/// </summary>
[Collection(StorageMigrationStaticStateCollection.Name)]
public class GitHubTokenPathResolverTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _defaultRootDir;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubTokenPathResolverTests"/> class.
    /// </summary>
    public GitHubTokenPathResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GenHubTokenPathResolverTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _defaultRootDir = Path.Combine(Path.GetTempPath(), "GenHubTokenPathResolverDefaultRootTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_defaultRootDir);
        StorageMigrationService.SetDefaultDataRootOverrideForTesting(_defaultRootDir);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StorageMigrationService.SetDefaultDataRootOverrideForTesting(null);
        DeleteDirectoryBestEffort(_tempDir);
        DeleteDirectoryBestEffort(_defaultRootDir);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Verifies that the primary token path points inside the given data directory.
    /// </summary>
    [Fact]
    public void GetPrimaryTokenFilePath_ReturnsTokenFileInGivenDirectory()
    {
        // Act
        var primary = GitHubTokenPathResolver.GetPrimaryTokenFilePath(_tempDir);

        // Assert
        Assert.Equal(Path.Combine(_tempDir, AppConstants.TokenFileName), primary);
    }

    /// <summary>
    /// Verifies that no fallback path is returned when the data directory already is the default root.
    /// </summary>
    [Fact]
    public void GetFallbackTokenFilePath_WhenSameAsDefaultRoot_ReturnsNull()
    {
        // Act
        var fallback = GitHubTokenPathResolver.GetFallbackTokenFilePath(_defaultRootDir);

        // Assert
        Assert.Null(fallback);
    }

    /// <summary>
    /// Verifies that the fallback path points at the default data root for a relocated data directory.
    /// </summary>
    [Fact]
    public void GetFallbackTokenFilePath_WhenRelocated_ReturnsDefaultRootTokenFile()
    {
        // Act
        var fallback = GitHubTokenPathResolver.GetFallbackTokenFilePath(_tempDir);

        // Assert
        Assert.Equal(Path.Combine(_defaultRootDir, AppConstants.TokenFileName), fallback);
    }

    /// <summary>
    /// Verifies that the active path prefers the primary copy when both copies exist.
    /// </summary>
    [Fact]
    public void ResolveActiveTokenFilePath_WithBothCopies_PrefersPrimary()
    {
        // Arrange
        var primary = GitHubTokenPathResolver.GetPrimaryTokenFilePath(_tempDir);
        var fallback = GitHubTokenPathResolver.GetFallbackTokenFilePath(_tempDir);
        Assert.NotNull(fallback);
        File.WriteAllText(primary, "primary");
        File.WriteAllText(fallback, "fallback");

        // Act
        var active = GitHubTokenPathResolver.ResolveActiveTokenFilePath(primary, fallback);

        // Assert
        Assert.Equal(primary, active);
    }

    /// <summary>
    /// Verifies that the active path falls back to the default root copy when the primary copy is missing.
    /// </summary>
    [Fact]
    public void ResolveActiveTokenFilePath_WithoutPrimaryCopy_FallsBack()
    {
        // Arrange
        var primary = GitHubTokenPathResolver.GetPrimaryTokenFilePath(_tempDir);
        var fallback = GitHubTokenPathResolver.GetFallbackTokenFilePath(_tempDir);
        Assert.NotNull(fallback);
        File.WriteAllText(fallback, "fallback");

        // Act
        var active = GitHubTokenPathResolver.ResolveActiveTokenFilePath(primary, fallback);

        // Assert
        Assert.Equal(fallback, active);
    }

    /// <summary>
    /// Verifies that no active path is returned when neither copy exists.
    /// </summary>
    [Fact]
    public void ResolveActiveTokenFilePath_WithoutCopies_ReturnsNull()
    {
        // Arrange
        var primary = GitHubTokenPathResolver.GetPrimaryTokenFilePath(_tempDir);
        var fallback = GitHubTokenPathResolver.GetFallbackTokenFilePath(_tempDir);

        // Act
        var active = GitHubTokenPathResolver.ResolveActiveTokenFilePath(primary, fallback);

        // Assert
        Assert.Null(active);
    }

    /// <summary>
    /// Verifies that no fallback path is returned when the data directory is a symbolic
    /// link to the default root, so the post-save cleanup cannot delete the token just written.
    /// </summary>
    [Fact]
    public void GetFallbackTokenFilePath_WhenAliasedToDefaultRoot_ReturnsNull()
    {
        // Arrange
        var link = Path.Combine(_tempDir, "alias");

        try
        {
            if (!TryCreateDirectorySymbolicLink(link, _defaultRootDir))
            {
                return;
            }

            // Act
            var fallback = GitHubTokenPathResolver.GetFallbackTokenFilePath(link);

            // Assert
            Assert.Null(fallback);
        }
        finally
        {
            DeleteLinkBestEffort(link);
        }
    }

    /// <summary>
    /// Verifies that deleting a missing fallback copy is a no-op.
    /// </summary>
    [Fact]
    public void DeleteFallbackCopyBestEffort_WithoutFallback_DoesNothing()
    {
        // Act & Assert
        GitHubTokenPathResolver.DeleteFallbackCopyBestEffort(null);
        GitHubTokenPathResolver.DeleteFallbackCopyBestEffort(Path.Combine(_tempDir, "missing-token-file"));
    }

    /// <summary>
    /// Verifies that deleting the fallback copy removes the stale file.
    /// </summary>
    [Fact]
    public void DeleteFallbackCopyBestEffort_WithFallback_RemovesFile()
    {
        // Arrange
        var fallback = Path.Combine(_tempDir, "stale-token-file");
        File.WriteAllText(fallback, "stale");

        // Act
        GitHubTokenPathResolver.DeleteFallbackCopyBestEffort(fallback);

        // Assert
        Assert.False(File.Exists(fallback));
    }

    private static void DeleteDirectoryBestEffort(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup of the temp directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup of the temp directory.
        }
    }

    private static void DeleteLinkBestEffort(string linkPath)
    {
        try
        {
            Directory.Delete(linkPath);
        }
        catch (IOException)
        {
            // Best effort cleanup of the test link.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup of the test link.
        }
    }

    private static bool TryCreateDirectorySymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);

            return true;
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
