using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Storage.Services;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Tests to verify that WorkspaceManager correctly reuses or recreates workspaces based on manifest versions.
/// </summary>
public class WorkspaceManagerReuseTests : IDisposable
{
    private readonly Mock<IConfigurationProviderService> _mockConfigProvider;
    private readonly Mock<ILogger<WorkspaceManager>> _mockLogger;
    private readonly Mock<IWorkspaceValidator> _mockWorkspaceValidator;
    private readonly Mock<IWorkspaceStrategy> _mockStrategy;
    private readonly CasReferenceTracker _casTracker;
    private readonly WorkspaceReconciler _reconciler;
    private readonly string _tempPath;
    private readonly string _metadataPath;
    private readonly WorkspaceManager _manager;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceManagerReuseTests"/> class.
    /// </summary>
    public WorkspaceManagerReuseTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempPath);
        _metadataPath = Path.Combine(_tempPath, "workspaces.json");

        _mockConfigProvider = new Mock<IConfigurationProviderService>();
        _mockConfigProvider.Setup(x => x.GetApplicationDataPath()).Returns(_tempPath);

        _mockLogger = new Mock<ILogger<WorkspaceManager>>();
        _mockWorkspaceValidator = new Mock<IWorkspaceValidator>();

        _mockStrategy = new Mock<IWorkspaceStrategy>();
        _mockStrategy.Setup(x => x.Name).Returns("TestStrategy");
        _mockStrategy.Setup(x => x.CanHandle(It.IsAny<WorkspaceConfiguration>())).Returns(true);

        var mockCasConfig = new Mock<IOptions<CasConfiguration>>();
        mockCasConfig.Setup(x => x.Value).Returns(new CasConfiguration { CasRootPath = Path.Combine(_tempPath, "cas") });
        _casTracker = new CasReferenceTracker(mockCasConfig.Object, new Mock<ILogger<CasReferenceTracker>>().Object);

        var mockFileOps = new Mock<IFileOperationsService>();
        _reconciler = new WorkspaceReconciler(new Mock<ILogger<WorkspaceReconciler>>().Object, mockFileOps.Object);

        _manager = new WorkspaceManager(
            [_mockStrategy.Object],
            _mockConfigProvider.Object,
            _mockLogger.Object,
            _casTracker,
            _mockWorkspaceValidator.Object,
            _reconciler);
    }

    /// <summary>
    /// Verifies that a workspace is recreated when the manifest version changes.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task PrepareWorkspaceAsync_WhenManifestVersionChanges_ShouldRecreateWorkspace()
    {
        // Arrange
        var workspaceId = "test-workspace";
        var manifestId = "1.0.local.mod.testmanifest";
        var workspacePath = Path.Combine(_tempPath, "workspace");
        Directory.CreateDirectory(workspacePath);
        File.WriteAllText(Path.Combine(workspacePath, "test.txt"), "content");

        // Create cached metadata with version 1.0
        var cachedWorkspace = new WorkspaceInfo
        {
            Id = workspaceId,
            WorkspacePath = workspacePath,
            ManifestIds = [manifestId],
            ManifestVersions = new Dictionary<string, string> { { manifestId, "1.0" } },
            Strategy = WorkspaceStrategy.HardLink,
            IsPrepared = true,
            FileCount = 1,
            IsValid = true,
        };
        await File.WriteAllTextAsync(_metadataPath, System.Text.Json.JsonSerializer.Serialize(new[] { cachedWorkspace }));

        // New configuration with version 2.0
        var config = new WorkspaceConfiguration
        {
            Id = workspaceId,
            Strategy = WorkspaceStrategy.HardLink,
            Manifests = [new ContentManifest { Id = ManifestId.Create(manifestId), Version = "2.0" }],
            BaseInstallationPath = _tempPath,
            WorkspaceRootPath = _tempPath,
            ValidateAfterPreparation = false,
        };

        _mockWorkspaceValidator.Setup(x => x.ValidateConfigurationAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(workspaceId, []));
        _mockWorkspaceValidator.Setup(x => x.ValidatePrerequisitesAsync(It.IsAny<IWorkspaceStrategy>(), It.IsAny<WorkspaceConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(workspaceId, []));

        // Ensure successful validation result
        var successValidation = new ValidationResult(workspaceId, []);
        _mockWorkspaceValidator.Setup(x => x.ValidateWorkspaceAsync(It.IsAny<WorkspaceInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ValidationResult>.CreateSuccess(successValidation));

        _mockStrategy.Setup(x => x.PrepareAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceInfo { Id = workspaceId, IsPrepared = true, WorkspacePath = workspacePath });

        // Act
        var result = await _manager.PrepareWorkspaceAsync(config);

        // Assert
        result.Success.Should().BeTrue();
        _mockStrategy.Verify(x => x.PrepareAsync(It.Is<WorkspaceConfiguration>(c => c.ForceRecreate == true), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that a workspace is reused when the manifest version remains the same.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task PrepareWorkspaceAsync_WhenManifestVersionSame_ShouldReuseWorkspace()
    {
        // Arrange
        var workspaceId = "test-workspace";
        var manifestId = "1.0.local.mod.testmanifest";
        var workspacePath = Path.Combine(_tempPath, "workspace");
        Directory.CreateDirectory(workspacePath);
        File.WriteAllText(Path.Combine(workspacePath, "test.txt"), "content");

        // Create cached metadata with version 1.0
        var cachedWorkspace = new WorkspaceInfo
        {
            Id = workspaceId,
            WorkspacePath = workspacePath,
            ManifestIds = [manifestId],
            ManifestVersions = new Dictionary<string, string> { { manifestId, "1.0" } },
            Strategy = WorkspaceStrategy.HardLink,
            IsPrepared = true,
            FileCount = 1,
            IsValid = true,
        };
        await File.WriteAllTextAsync(_metadataPath, System.Text.Json.JsonSerializer.Serialize(new[] { cachedWorkspace }));

        // New configuration with SAME version 1.0
        var config = new WorkspaceConfiguration
        {
            Id = workspaceId,
            Strategy = WorkspaceStrategy.HardLink,
            Manifests = [new ContentManifest { Id = ManifestId.Create(manifestId), Version = "1.0", Files = [new ManifestFile { RelativePath = "test.txt" }] }],
            BaseInstallationPath = _tempPath,
            WorkspaceRootPath = _tempPath,
            ValidateAfterPreparation = false,
        };

        // Ensure successful validation result for this test too, although it might skip post-validation if reused
        var successValidation = new ValidationResult(workspaceId, []);

        _mockWorkspaceValidator.Setup(x => x.ValidateConfigurationAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successValidation);
        _mockWorkspaceValidator.Setup(x => x.ValidatePrerequisitesAsync(It.IsAny<IWorkspaceStrategy>(), It.IsAny<WorkspaceConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successValidation);

        _mockWorkspaceValidator.Setup(x => x.ValidateWorkspaceAsync(It.IsAny<WorkspaceInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ValidationResult>.CreateSuccess(successValidation));

        // Act
        var result = await _manager.PrepareWorkspaceAsync(config);

        // Assert
        result.Success.Should().BeTrue();

        // Should NOT call strategy.PrepareAsync because it reuses existing
        _mockStrategy.Verify(x => x.PrepareAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Disposes of temporary test resources.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempPath, true);
        }
        catch
        {
            // Ignore deletion errors in test cleanup
        }

        GC.SuppressFinalize(this);
    }
}
