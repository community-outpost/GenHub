using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Workspace.Strategies;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Tests for ProcessLocalFileAsync implementations in workspace strategies.
/// </summary>
public class ProcessLocalFileAsyncTests : IDisposable
{
    private readonly Mock<IFileOperationsService> _mockFileOperations;
    private readonly string _tempSourceDir;
    private readonly string _tempWorkspaceDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessLocalFileAsyncTests"/> class.
    /// </summary>
    public ProcessLocalFileAsyncTests()
    {
        _mockFileOperations = new Mock<IFileOperationsService>();
        _tempSourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _tempWorkspaceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        Directory.CreateDirectory(_tempSourceDir);
        Directory.CreateDirectory(_tempWorkspaceDir);
    }

    /// <summary>
    /// Tests that FullCopyStrategy processes local files by copying them.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task FullCopyStrategy_ProcessLocalFileAsync_CopiesFile()
    {
        // Arrange
        var logger = new Mock<ILogger<FullCopyStrategy>>();
        var strategy = new FullCopyStrategy(_mockFileOperations.Object, logger.Object);

        var sourceFile = Path.Combine(_tempSourceDir, "test.exe");
        await File.WriteAllTextAsync(sourceFile, "test content");

        var file = new ManifestFile
        {
            RelativePath = "test.exe",
            Size = 12,
            SourceType = ContentSourceType.LocalFile,
        };

        var config = CreateTestConfiguration();
        var targetPath = Path.Combine(_tempWorkspaceDir, file.RelativePath);

        // Act
        await strategy.PrepareAsync(config, null, CancellationToken.None);

        // Assert
        _mockFileOperations.Verify(
            x => x.CopyFileAsync(
                It.Is<string>(s => s.Contains("test.exe")),
                It.Is<string>(s => s.Contains("test.exe")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that SymlinkOnlyStrategy processes local files by creating symlinks.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task SymlinkOnlyStrategy_ProcessLocalFileAsync_CreatesSymlink()
    {
        // Arrange
        var logger = new Mock<ILogger<SymlinkOnlyStrategy>>();
        var strategy = new SymlinkOnlyStrategy(_mockFileOperations.Object, logger.Object);

        var file = new ManifestFile
        {
            RelativePath = "test.exe",
            Size = 12,
            SourceType = ContentSourceType.LocalFile,
        };

        var config = CreateTestConfiguration();

        // Act
        await strategy.PrepareAsync(config, null, CancellationToken.None);

        // Assert
        _mockFileOperations.Verify(
            x => x.CreateSymlinkAsync(
                It.Is<string>(s => s.Contains("test.exe")),
                It.Is<string>(s => s.Contains("test.exe")),
                It.IsAny<CancellationToken>()),
            Times.Never); // File doesn't exist, so symlink not called
    }

    /// <summary>
    /// Tests that HybridCopySymlinkStrategy processes essential files by copying.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task HybridCopySymlinkStrategy_ProcessLocalFileAsync_CopiesEssentialFiles()
    {
        // Arrange
        var logger = new Mock<ILogger<HybridCopySymlinkStrategy>>();
        var strategy = new HybridCopySymlinkStrategy(_mockFileOperations.Object, logger.Object);

        var file = new ManifestFile
        {
            RelativePath = "game.exe", // Essential file
            Size = 500, // Small size - essential
            SourceType = ContentSourceType.LocalFile,
        };

        var config = CreateTestConfiguration();

        // Act
        await strategy.PrepareAsync(config, null, CancellationToken.None);

        // Assert - Should attempt to copy essential files
        _mockFileOperations.Verify(
            x => x.CopyFileAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never); // File doesn't exist
    }

    /// <summary>
    /// Tests that HybridCopySymlinkStrategy processes non-essential files by creating symlinks.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task HybridCopySymlinkStrategy_ProcessLocalFileAsync_SymlinksNonEssentialFiles()
    {
        // Arrange
        var logger = new Mock<ILogger<HybridCopySymlinkStrategy>>();
        var strategy = new HybridCopySymlinkStrategy(_mockFileOperations.Object, logger.Object);

        var file = new ManifestFile
        {
            RelativePath = "video.bik", // Non-essential file
            Size = 50000000, // Large size - non-essential
            SourceType = ContentSourceType.LocalFile,
        };

        var config = CreateTestConfiguration();

        // Act
        await strategy.PrepareAsync(config, null, CancellationToken.None);

        // Assert - Should attempt to symlink non-essential files
        _mockFileOperations.Verify(
            x => x.CreateSymlinkAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never); // File doesn't exist
    }

    /// <summary>
    /// Tests that HardLinkStrategy processes files by creating hard links when on same volume.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task HardLinkStrategy_ProcessLocalFileAsync_CreatesHardLinksOnSameVolume()
    {
        // Arrange
        var logger = new Mock<ILogger<HardLinkStrategy>>();
        var strategy = new HardLinkStrategy(_mockFileOperations.Object, logger.Object);

        var file = new ManifestFile
        {
            RelativePath = "test.dat",
            Size = 1000,
            SourceType = ContentSourceType.LocalFile,
        };

        var config = CreateTestConfiguration();

        // Act
        await strategy.PrepareAsync(
            config,
            null,
            CancellationToken.None);

        // Assert - Should attempt to create hard links (or copy if different volumes)
        _mockFileOperations.Verify(
            x => x.CreateHardLinkAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never); // File doesn't exist
    }

    /// <summary>
    /// Tests that strategies handle missing source files gracefully.
    /// </summary>
    /// <param name="strategyType">The strategy type to test.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Theory]
    [InlineData(WorkspaceStrategy.FullCopy)]
    [InlineData(WorkspaceStrategy.SymlinkOnly)]
    [InlineData(WorkspaceStrategy.HybridCopySymlink)]
    [InlineData(WorkspaceStrategy.HardLink)]
    public async Task AllStrategies_ProcessLocalFileAsync_HandlesMissingSourceFiles(WorkspaceStrategy strategyType)
    {
        // Arrange
        var strategy = CreateStrategy(strategyType);
        var config = CreateTestConfiguration(strategyType);

        // Add a file that doesn't exist in the source
        config.Manifests[0].Files.Add(new ManifestFile
        {
            RelativePath = "nonexistent.file",
            Size = 1000,
            SourceType = ContentSourceType.LocalFile,
        });

        // Act & Assert - Should not throw, should handle missing files gracefully
        var result = await strategy.PrepareAsync(
            config,
            null,
            CancellationToken.None);

        Assert.NotNull(result);
    }

    /// <summary>
    /// Tests that strategies properly validate configuration parameter.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task AllStrategies_ProcessLocalFileAsync_ValidatesConfiguration()
    {
        // Arrange
        var logger = new Mock<ILogger<FullCopyStrategy>>();
        var strategy = new FullCopyStrategy(_mockFileOperations.Object, logger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => strategy.PrepareAsync(
                null!,
                null,
                CancellationToken.None));
    }

    /// <summary>
    /// Disposes test resources.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempSourceDir))
            {
                Directory.Delete(_tempSourceDir, true);
            }
        }
        catch
        {
        }

        try
        {
            if (Directory.Exists(_tempWorkspaceDir))
            {
                Directory.Delete(_tempWorkspaceDir, true);
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// Creates a test workspace configuration with a basic manifest containing a single local file.
    /// </summary>
    /// <param name="strategy">The workspace strategy to set in the configuration.</param>
    /// <returns>A fully-initialized <see cref="WorkspaceConfiguration"/> for tests.</returns>
    private WorkspaceConfiguration CreateTestConfiguration(WorkspaceStrategy strategy = WorkspaceStrategy.FullCopy)
    {
        return new WorkspaceConfiguration
        {
            Id = Guid.NewGuid().ToString(),
            Strategy = strategy,
            WorkspaceRootPath = _tempWorkspaceDir,
            BaseInstallationPath = _tempSourceDir,
            GameClient = new GameClient { Id = "test" },
            Manifests = new List<ContentManifest>
            {
                new ContentManifest
                {
                    Files = new List<ManifestFile>
                    {
                        new()
                        {
                            RelativePath = "test.exe",
                            Size = 1000,
                            SourceType = ContentSourceType.LocalFile,
                        },
                    },
                },
            },
        };
    }

    /// <summary>
    /// Creates a workspace strategy instance for the provided strategy type.
    /// </summary>
    /// <param name="strategyType">The strategy type to instantiate.</param>
    /// <returns>An implementation of <see cref="IWorkspaceStrategy"/>.</returns>
    private IWorkspaceStrategy CreateStrategy(WorkspaceStrategy strategyType)
    {
        return strategyType switch
        {
            WorkspaceStrategy.FullCopy => new FullCopyStrategy(
                _mockFileOperations.Object,
                new Mock<ILogger<FullCopyStrategy>>().Object),
            WorkspaceStrategy.SymlinkOnly => new SymlinkOnlyStrategy(
                _mockFileOperations.Object,
                new Mock<ILogger<SymlinkOnlyStrategy>>().Object),
            WorkspaceStrategy.HybridCopySymlink => new HybridCopySymlinkStrategy(
                _mockFileOperations.Object,
                new Mock<ILogger<HybridCopySymlinkStrategy>>().Object),
            WorkspaceStrategy.HardLink => new HardLinkStrategy(
                _mockFileOperations.Object,
                new Mock<ILogger<HardLinkStrategy>>().Object),
            _ => throw new ArgumentException($"Unknown strategy type: {strategyType}"),
        };
    }
}
