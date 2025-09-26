using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Workspace.Strategies;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Tests for WorkspaceStrategyBase functionality.
/// </summary>
public class WorkspaceStrategyBaseTests : IDisposable
{
    private readonly Mock<IFileOperationsService> _mockFileOperations;
    private readonly TestWorkspaceStrategy _strategy;
    private readonly string _tempDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceStrategyBaseTests"/> class.
    /// </summary>
    public WorkspaceStrategyBaseTests()
    {
        _mockFileOperations = new Mock<IFileOperationsService>();
        _strategy = new TestWorkspaceStrategy(_mockFileOperations.Object);
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// Tests that IsEssentialFile correctly identifies essential files.
    /// </summary>
    /// <param name="relativePath">The file path to test.</param>
    /// <param name="fileSize">The file size in bytes.</param>
    /// <param name="expectedEssential">Whether the file should be considered essential.</param>
    [Theory]
    [InlineData("game.exe", 1000000, true)]
    [InlineData("config.ini", 500, true)]
    [InlineData("mods/mod1.ini", 1000, true)]
    [InlineData("data/textures/large.tga", 10000000, true)] // Fixed: Large TGA files in data directory are considered essential
    [InlineData("sounds/music.wav", 5000000, false)]
    [InlineData("generals.exe", 2000000, true)]
    [InlineData("patch.big", 50000000, true)]
    [InlineData("video.bik", 100000000, false)]
    [InlineData("small.tga", 500, true)] // Small files are essential
    public void IsEssentialFile_VariousFiles_ReturnsExpected(string relativePath, long fileSize, bool expectedEssential)
    {
        // Act
        var result = TestWorkspaceStrategy.TestIsEssentialFile(relativePath, fileSize);

        // Assert
        Assert.Equal(expectedEssential, result);
    }

    /// <summary>
    /// Tests that ValidateSourceFile returns correct results.
    /// </summary>
    [Fact]
    public void ValidateSourceFile_ExistingFile_ReturnsTrue()
    {
        // Arrange
        var testFile = Path.Combine(_tempDir, "test.txt");
        File.WriteAllText(testFile, "test content");

        // Act
        var result = _strategy.TestValidateSourceFile(testFile, "test.txt");

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// Tests that ValidateSourceFile returns false for non-existent files.
    /// </summary>
    [Fact]
    public void ValidateSourceFile_NonExistentFile_ReturnsFalse()
    {
        // Arrange
        var nonExistentFile = Path.Combine(_tempDir, "nonexistent.txt");

        // Act
        var result = _strategy.TestValidateSourceFile(nonExistentFile, "nonexistent.txt");

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Tests that CreateBaseWorkspaceInfo creates correct workspace info.
    /// </summary>
    [Fact]
    public void CreateBaseWorkspaceInfo_ValidConfiguration_ReturnsCorrectInfo()
    {
        // Arrange
        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            WorkspaceRootPath = _tempDir,
            GameClient = new GameClient { Id = "test-version" },
            Strategy = WorkspaceStrategy.FullCopy,
        };

        // Act
        var result = _strategy.TestCreateBaseWorkspaceInfo(config);

        // Assert
        Assert.Equal("test-workspace", result.Id);
        Assert.Equal(Path.Combine(_tempDir, "test-workspace"), result.WorkspacePath);
        Assert.Equal("test-version", result.GameClientId);
        Assert.Equal(WorkspaceStrategy.FullCopy, result.Strategy);
        Assert.True(result.IsValid);
        Assert.True((DateTime.UtcNow - result.CreatedAt).TotalSeconds < 5);
    }

    /// <summary>
    /// Tests that UpdateWorkspaceInfo correctly updates workspace information.
    /// </summary>
    [Fact]
    public void UpdateWorkspaceInfo_WithExecutable_SetsExecutablePath()
    {
        // Arrange
        var workspaceInfo = new WorkspaceInfo
        {
            WorkspacePath = _tempDir,
        };

        var config = new WorkspaceConfiguration
        {
            Manifests = new List<ContentManifest>
            {
                new()
                {
                    Files =
                    [
                        new() { RelativePath = "generals.exe", Size = 1000 },
                        new() { RelativePath = "config.ini", Size = 500 },
                    ],
                },
            },
            GameClient = new GameClient { ExecutablePath = "generals.exe" },
        };

        // Act
        _strategy.TestUpdateWorkspaceInfo(workspaceInfo, 2, 1500L, config);

        // Assert
        Assert.Equal(2, workspaceInfo.FileCount);
        Assert.Equal(1500L, workspaceInfo.TotalSizeBytes);
        Assert.Equal(Path.Combine(_tempDir, "generals.exe"), workspaceInfo.ExecutablePath);
        Assert.Equal(_tempDir, workspaceInfo.WorkingDirectory);
    }

    /// <summary>
    /// Tests that GetFileSizeSafe handles missing files gracefully.
    /// </summary>
    [Fact]
    public void GetFileSizeSafe_NonExistentFile_ReturnsZero()
    {
        // Arrange
        var nonExistentFile = Path.Combine(_tempDir, "nonexistent.txt");

        // Act
        var result = _strategy.TestGetFileSizeSafe(nonExistentFile);

        // Assert
        Assert.Equal(0L, result);
    }

    /// <summary>
    /// Tests that GetFileSizeSafe returns correct file size.
    /// </summary>
    [Fact]
    public void GetFileSizeSafe_ExistingFile_ReturnsCorrectSize()
    {
        // Arrange
        var testFile = Path.Combine(_tempDir, "test.txt");
        var content = "This is test content";
        File.WriteAllText(testFile, content);

        // Act
        var result = _strategy.TestGetFileSizeSafe(testFile);

        // Assert
        Assert.Equal(content.Length, result);
    }

    /// <summary>
    /// Disposes test resources.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    /// <summary>
    /// Test implementation of WorkspaceStrategyBase for testing purposes.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="TestWorkspaceStrategy"/> class.
    /// </remarks>
    /// <param name="fileOperations">The file operations service.</param>
    public sealed class TestWorkspaceStrategy(IFileOperationsService fileOperations)
        : WorkspaceStrategyBase<TestWorkspaceStrategy>(fileOperations, new NullLogger<TestWorkspaceStrategy>())
    {
        /// <inheritdoc/>
        public override string Name => "Test Strategy";

        /// <inheritdoc/>
        public override string Description => "Test strategy for unit testing";

        /// <inheritdoc/>
        public override bool RequiresAdminRights => false;

        /// <inheritdoc/>
        public override bool RequiresSameVolume => false;

        /// <summary>
        /// Exposes IsEssentialFile for testing.
        /// </summary>
        /// <param name="relativePath">The relative path.</param>
        /// <param name="fileSize">The file size.</param>
        /// <returns>True if essential; otherwise, false.</returns>
        public static bool TestIsEssentialFile(string relativePath, long fileSize) => IsEssentialFile(relativePath, fileSize);

        /// <inheritdoc/>
        public override bool CanHandle(WorkspaceConfiguration configuration) => true;

        /// <inheritdoc/>
        public override long EstimateDiskUsage(WorkspaceConfiguration configuration) => 1000L;

        /// <inheritdoc/>
        public override Task<WorkspaceInfo> PrepareAsync(
            WorkspaceConfiguration configuration,
            IProgress<WorkspacePreparationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var workspaceInfo = CreateBaseWorkspaceInfo(configuration);
            return Task.FromResult(workspaceInfo);
        }

        /// <summary>
        /// Exposes ValidateSourceFile for testing.
        /// </summary>
        /// <param name="sourcePath">The source path.</param>
        /// <param name="relativePath">The relative path.</param>
        /// <returns>True if valid; otherwise, false.</returns>
        public bool TestValidateSourceFile(string sourcePath, string relativePath) => ValidateSourceFile(sourcePath, relativePath);

        /// <summary>
        /// Exposes CreateBaseWorkspaceInfo for testing.
        /// </summary>
        /// <param name="configuration">The workspace configuration.</param>
        /// <returns>The workspace info.</returns>
        public WorkspaceInfo TestCreateBaseWorkspaceInfo(WorkspaceConfiguration configuration) => CreateBaseWorkspaceInfo(configuration);

        /// <summary>
        /// Exposes UpdateWorkspaceInfo for testing.
        /// </summary>
        /// <param name="workspaceInfo">The workspace info.</param>
        /// <param name="fileCount">The file count.</param>
        /// <param name="totalSize">The total size.</param>
        /// <param name="configuration">The workspace configuration.</param>
        public void TestUpdateWorkspaceInfo(WorkspaceInfo workspaceInfo, int fileCount, long totalSize, WorkspaceConfiguration configuration) =>
            UpdateWorkspaceInfo(workspaceInfo, fileCount, totalSize, configuration);

        /// <summary>
        /// Exposes GetFileSizeSafe for testing.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        /// <returns>The file size in bytes.</returns>
        public long TestGetFileSizeSafe(string filePath) => GetFileSizeSafe(filePath);

        /// <summary>
        /// Exposes CalculateActualTotalSize for testing.
        /// </summary>
        /// <param name="configuration">The workspace configuration.</param>
        /// <returns>The total size in bytes.</returns>
        public long TestCalculateActualTotalSize(WorkspaceConfiguration configuration) => CalculateActualTotalSize(configuration);

        /// <inheritdoc/>
        protected override Task CreateCasLinkAsync(string hash, string targetPath, CancellationToken cancellationToken)
        {
            // For testing, just simulate a completed task.
            return Task.CompletedTask;
        }
    }
}
