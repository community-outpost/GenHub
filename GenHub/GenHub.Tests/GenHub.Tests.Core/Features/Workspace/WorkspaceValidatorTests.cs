using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Validation;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Workspace;
using GenHub.Features.Workspace.Strategies;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Tests for the WorkspaceValidator class.
/// </summary>
public class WorkspaceValidatorTests : IDisposable
{
    private readonly Mock<ILogger<WorkspaceValidator>> _mockLogger;
    private readonly WorkspaceValidator _validator;
    private readonly string _tempDir;
    private readonly string _sourceDir;
    private readonly string _workspaceDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceValidatorTests"/> class.
    /// </summary>
    public WorkspaceValidatorTests()
    {
        _mockLogger = new Mock<ILogger<WorkspaceValidator>>();
        _validator = new WorkspaceValidator(_mockLogger.Object);
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _sourceDir = Path.Combine(_tempDir, "source");
        _workspaceDir = Path.Combine(_tempDir, "workspace");

        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_workspaceDir);
    }

    /// <summary>
    /// Tests validation of a valid workspace configuration.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateConfigurationAsync_ValidConfiguration_ReturnsSuccess()
    {
        // Arrange
        var config = CreateValidConfiguration();

        // Act
        var result = await _validator.ValidateConfigurationAsync(config);

        // Assert
        Assert.NotNull(result);
        Assert.DoesNotContain(result.Issues, i => i.Severity == ValidationSeverity.Error);
    }

    /// <summary>
    /// Tests validation fails when required properties are missing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateConfigurationAsync_MissingRequiredProperties_ReturnsErrors()
    {
        // Arrange
        var config = new WorkspaceConfiguration
        {
            Id = string.Empty,
            BaseInstallationPath = string.Empty,
            WorkspaceRootPath = string.Empty,
            Manifest = new GameManifest { Files = new List<ManifestFile>() },
        };

        // Act
        var result = await _validator.ValidateConfigurationAsync(config);

        // Assert
        Assert.True(result.Issues.Count(i => i.Severity == ValidationSeverity.Error) >= 3);
    }

    /// <summary>
    /// Tests validation fails when source directory doesn't exist.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateConfigurationAsync_NonExistentSourcePath_ReturnsError()
    {
        // Arrange
        var config = CreateValidConfiguration();
        config.BaseInstallationPath = Path.Combine(_tempDir, "nonexistent");

        // Act
        var result = await _validator.ValidateConfigurationAsync(config);

        // Assert
        Assert.Contains(result.Issues, i => i.IssueType == ValidationIssueType.DirectoryMissing && i.Severity == ValidationSeverity.Error);
    }

    /// <summary>
    /// Tests validation fails when manifest has no files.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateConfigurationAsync_EmptyManifest_ReturnsError()
    {
        // Arrange
        var config = CreateValidConfiguration();
        config.Manifest = new GameManifest { Files = new List<ManifestFile>() };

        // Act
        var result = await _validator.ValidateConfigurationAsync(config);

        // Assert
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error);
    }

    /// <summary>
    /// Tests prerequisite validation for strategies requiring admin rights.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidatePrerequisitesAsync_AdminRequired_ValidatesCorrectly()
    {
        // Arrange
        var mockStrategy = new Mock<IWorkspaceStrategy>();
        mockStrategy.Setup(s => s.Name).Returns("Test Strategy");

        // Use reflection to simulate admin requirement
        var strategyType = mockStrategy.Object.GetType();
        var propAdmin = strategyType.GetProperty("RequiresAdminRights");
        if (propAdmin?.CanWrite == true)
        {
            propAdmin.SetValue(mockStrategy.Object, true);
        }

        // Act
        var result = await _validator.ValidatePrerequisitesAsync(mockStrategy.Object, _sourceDir, _workspaceDir);

        // Assert
        Assert.NotNull(result);
    }

    /// <summary>
    /// Tests prerequisite validation for different volume scenarios.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidatePrerequisitesAsync_DifferentVolumes_ReturnsWarning()
    {
        // Arrange
        var mockStrategy = new Mock<IWorkspaceStrategy>();
        mockStrategy.Setup(s => s.Name).Returns("Hard Link Strategy");

        // Create a concrete strategy that requires same volume for testing
        var fileOps = new Mock<IFileOperationsService>();
        var logger = new Mock<ILogger<HardLinkStrategy>>();
        var hardLinkStrategy = new HardLinkStrategy(fileOps.Object, logger.Object);

        // Create paths on potentially different volumes
        var sourcePath = _sourceDir;
        var destPath = Path.Combine(Path.GetTempPath(), "different", Guid.NewGuid().ToString());

        // Act
        var result = await _validator.ValidatePrerequisitesAsync(hardLinkStrategy, sourcePath, destPath);

        // Assert
        Assert.NotNull(result);

        // The warning should appear if paths are on different volumes
        var volumeWarning = result.Issues.FirstOrDefault(i => i.Path == "VolumeCheck");
        if (Path.GetPathRoot(sourcePath) != Path.GetPathRoot(destPath))
        {
            Assert.NotNull(volumeWarning);
            Assert.Equal(ValidationSeverity.Warning, volumeWarning.Severity);
        }
    }

    /// <summary>
    /// Tests disk space validation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidatePrerequisitesAsync_InsufficientDiskSpace_ReturnsWarning()
    {
        // Arrange - Use a concrete strategy that can return large disk usage
        var fileOps = new Mock<IFileOperationsService>();
        var logger = new Mock<ILogger<FullCopyStrategy>>();
        var strategy = new FullCopyStrategy(fileOps.Object, logger.Object);

        // Create a configuration with large files to trigger disk space warning
        var largeFileManifest = new GameManifest
        {
            Files = new List<ManifestFile>
            {
                new() { RelativePath = "huge.bin", Size = long.MaxValue / 2 },
            },
        };

        var config = new WorkspaceConfiguration
        {
            Manifest = largeFileManifest,
            Strategy = WorkspaceStrategy.FullCopy,
        };

        // Mock EstimateDiskUsage to return a huge value by using the manifest
        var mockStrategyWithLargeUsage = new Mock<IWorkspaceStrategy>();
        mockStrategyWithLargeUsage.Setup(s => s.Name).Returns("Full Copy Strategy");
        mockStrategyWithLargeUsage.Setup(s => s.RequiresAdminRights).Returns(false);
        mockStrategyWithLargeUsage.Setup(s => s.RequiresSameVolume).Returns(false);
        mockStrategyWithLargeUsage.Setup(s => s.EstimateDiskUsage(It.IsAny<WorkspaceConfiguration>()))
                                  .Returns(long.MaxValue / 2);

        // Act
        var result = await _validator.ValidatePrerequisitesAsync(mockStrategyWithLargeUsage.Object, _sourceDir, _workspaceDir);

        // Assert
        Assert.NotNull(result);
        Assert.Contains(result.Issues, i => i.IssueType == ValidationIssueType.InsufficientSpace ||
                                           (i.Severity == ValidationSeverity.Warning && i.Message.Contains("disk space")));
    }

    /// <summary>
    /// Disposes of test resources.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    /// <summary>
    /// Creates a valid workspace configuration for testing.
    /// </summary>
    /// <returns>A valid workspace configuration.</returns>
    private WorkspaceConfiguration CreateValidConfiguration()
    {
        return new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _sourceDir,
            WorkspaceRootPath = _workspaceDir,
            GameVersion = new GameVersion { Id = "test-version" },
            Strategy = WorkspaceStrategy.FullCopy,
            Manifest = new GameManifest
            {
                Files = new List<ManifestFile>
                {
                    new() { RelativePath = "test.exe", Size = 1000 },
                    new() { RelativePath = "config.ini", Size = 500 },
                },
            },
        };
    }
}
