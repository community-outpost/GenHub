using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Workspace.Strategies;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Tests for the HybridCopySymlinkStrategy class.
/// </summary>
public class HybridCopySymlinkStrategyTests : IDisposable
{
    private readonly Mock<IFileOperationsService> _mockFileOperations;
    private readonly Mock<ILogger<HybridCopySymlinkStrategy>> _mockLogger;
    private readonly HybridCopySymlinkStrategy _strategy;
    private readonly string _tempDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="HybridCopySymlinkStrategyTests"/> class.
    /// </summary>
    public HybridCopySymlinkStrategyTests()
    {
        _mockFileOperations = new Mock<IFileOperationsService>();
        _mockLogger = new Mock<ILogger<HybridCopySymlinkStrategy>>();
        _strategy = new HybridCopySymlinkStrategy(_mockFileOperations.Object, _mockLogger.Object);
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    }

    /// <summary>
    /// Test that the strategy can handle HybridCopySymlink configuration.
    /// </summary>
    [Fact]
    public void CanHandle_HybridCopySymlinkStrategy_ReturnsTrue()
    {
        // Arrange
        var config = new WorkspaceConfiguration
        {
            Strategy = WorkspaceStrategy.HybridCopySymlink,
        };

        // Act
        var result = _strategy.CanHandle(config);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// Test that the strategy correctly estimates disk usage based on file classification.
    /// </summary>
    [Fact]
    public void EstimateDiskUsage_MixedFiles_ReturnsCorrectEstimate()
    {
        // Arrange
        var manifest = new GameManifest
        {
            Files =
            [
                new ManifestFile { RelativePath = "game.exe", Size = 1000000 }, // Essential
                new ManifestFile { RelativePath = "config.ini", Size = 1000 }, // Essential (small)
                new ManifestFile { RelativePath = "textures/large.tga", Size = 5000000 }, // Non-essential
                new ManifestFile { RelativePath = "sounds/music.wav", Size = 10000000 }, // Non-essential
            ],
        };

        var config = new WorkspaceConfiguration
        {
            Manifest = manifest,
            Strategy = WorkspaceStrategy.HybridCopySymlink,
        };

        // Act
        var estimate = _strategy.EstimateDiskUsage(config);

        // Assert
        // Should copy exe and ini (1001000 bytes) + symlink overhead for media files (2 * LinkOverheadBytes)
        const long LinkOverheadBytes = 1024L;
        Assert.Equal(1001000 + (2 * LinkOverheadBytes), estimate);
    }

    /// <summary>
    /// Cleanup after each test.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Cleanup after each test.
    /// </summary>
    /// <param name="disposing">True if disposing, false otherwise.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing && Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }
}
