using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Tests for WorkspaceReconciler file conflict resolution using priority system.
/// </summary>
public class WorkspaceReconcilerConflictTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly Mock<ILogger<WorkspaceReconciler>> _mockLogger;
    private readonly WorkspaceReconciler _reconciler;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceReconcilerConflictTests"/> class.
    /// </summary>
    public WorkspaceReconcilerConflictTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"GenHubTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _mockLogger = new Mock<ILogger<WorkspaceReconciler>>();
        var mockFileOps = new Mock<IFileOperationsService>();
        _reconciler = new WorkspaceReconciler(_mockLogger.Object, mockFileOps.Object);
    }

    /// <summary>
    /// Verifies that when a Mod and GameInstallation both provide the same file,
    /// the Mod version wins due to higher priority (100 vs 10).
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_ModVsGameInstallation_ModWinsAsync()
    {
        // Arrange
        var testFile = "Data\\Art\\Textures\\test.dds";
        var modManifest = CreateManifest(ContentType.Mod, testFile, "mod-hash");
        var installationManifest = CreateManifest(ContentType.GameInstallation, testFile, "base-hash");

        var config = new WorkspaceConfiguration
        {
            WorkspaceRootPath = _testDirectory,
            Manifests = new List<ContentManifest> { installationManifest, modManifest },
            Strategy = WorkspaceStrategy.HybridCopySymlink,
        };

        // Act
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(null, config);

        // Assert
        Assert.NotEmpty(result);
        var modFiles = result.FindAll(d => d.File.Hash == "mod-hash");
        Assert.Single(modFiles);
        var baseFiles = result.FindAll(d => d.File.Hash == "base-hash");
        Assert.Empty(baseFiles); // GameInstallation version should not be selected
    }

    /// <summary>
    /// Verifies that when a Patch and GameClient both provide the same file,
    /// the Patch version wins due to higher priority (90 vs 50).
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_PatchVsGameClient_PatchWinsAsync()
    {
        // Arrange
        var testFile = "generals.exe";
        var patchManifest = CreateManifest(ContentType.Patch, testFile, "patch-hash");
        var clientManifest = CreateManifest(ContentType.GameClient, testFile, "client-hash");

        var config = new WorkspaceConfiguration
        {
            WorkspaceRootPath = _testDirectory,
            Manifests = new List<ContentManifest> { clientManifest, patchManifest },
            Strategy = WorkspaceStrategy.HybridCopySymlink,
        };

        // Act
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(null, config);

        // Assert
        Assert.NotEmpty(result);
        var patchFiles = result.FindAll(d => d.File.Hash == "patch-hash");
        Assert.Single(patchFiles);
        var clientFiles = result.FindAll(d => d.File.Hash == "client-hash");
        Assert.Empty(clientFiles); // GameClient version should not be selected
    }

    /// <summary>
    /// Verifies that when GameClient and GameInstallation both provide the same file,
    /// the GameClient version wins due to higher priority (50 vs 10).
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_GameClientVsGameInstallation_GameClientWinsAsync()
    {
        // Arrange
        var testFile = "options.ini";
        var clientManifest = CreateManifest(ContentType.GameClient, testFile, "client-hash");
        var installationManifest = CreateManifest(ContentType.GameInstallation, testFile, "base-hash");

        var config = new WorkspaceConfiguration
        {
            WorkspaceRootPath = _testDirectory,
            Manifests = new List<ContentManifest> { installationManifest, clientManifest },
            Strategy = WorkspaceStrategy.HybridCopySymlink,
        };

        // Act
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(null, config);

        // Assert
        Assert.NotEmpty(result);
        var clientFiles = result.FindAll(d => d.File.Hash == "client-hash");
        Assert.Single(clientFiles);
        var baseFiles = result.FindAll(d => d.File.Hash == "base-hash");
        Assert.Empty(baseFiles); // GameInstallation version should not be selected
    }

    /// <summary>
    /// Verifies that when three content types provide the same file,
    /// the highest priority wins (Mod > GameClient > GameInstallation).
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_ThreeWayConflict_HighestPriorityWinsAsync()
    {
        // Arrange
        var testFile = "Data\\INI\\GameData.ini";
        var modManifest = CreateManifest(ContentType.Mod, testFile, "mod-hash");
        var clientManifest = CreateManifest(ContentType.GameClient, testFile, "client-hash");
        var installationManifest = CreateManifest(ContentType.GameInstallation, testFile, "base-hash");

        var config = new WorkspaceConfiguration
        {
            WorkspaceRootPath = _testDirectory,
            Manifests = new List<ContentManifest> { installationManifest, clientManifest, modManifest },
            Strategy = WorkspaceStrategy.HybridCopySymlink,
        };

        // Act
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(null, config);

        // Assert
        Assert.NotEmpty(result);
        var modFiles = result.FindAll(d => d.File.Hash == "mod-hash");
        Assert.Single(modFiles); // Mod has highest priority
        var clientFiles = result.FindAll(d => d.File.Hash == "client-hash");
        Assert.Empty(clientFiles);
        var baseFiles = result.FindAll(d => d.File.Hash == "base-hash");
        Assert.Empty(baseFiles);
    }

    /// <summary>
    /// Verifies that when there is no conflict (single source), the file is added normally.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_NoConflict_FileAddedNormallyAsync()
    {
        // Arrange
        var testFile = "unique.dat";
        var modManifest = CreateManifest(ContentType.Mod, testFile, "unique-hash");

        var config = new WorkspaceConfiguration
        {
            WorkspaceRootPath = _testDirectory,
            Manifests = new List<ContentManifest> { modManifest },
            Strategy = WorkspaceStrategy.HybridCopySymlink,
        };

        // Act
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(null, config);

        // Assert
        Assert.NotEmpty(result);
        var uniqueFiles = result.FindAll(d => d.File.Hash == "unique-hash");
        Assert.Single(uniqueFiles);
    }

    /// <summary>
    /// Verifies that conflicts are logged appropriately.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_ConflictOccurs_LogsWarningAsync()
    {
        // Arrange
        var testFile = "conflict.txt";
        var modManifest = CreateManifest(ContentType.Mod, testFile, "mod-hash");
        var installationManifest = CreateManifest(ContentType.GameInstallation, testFile, "base-hash");

        var config = new WorkspaceConfiguration
        {
            WorkspaceRootPath = _testDirectory,
            Manifests = new List<ContentManifest> { installationManifest, modManifest },
            Strategy = WorkspaceStrategy.HybridCopySymlink,
        };

        // Act
        await _reconciler.AnalyzeWorkspaceDeltaAsync(null, config);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("File conflict")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that when EA_LOGO.BIK is missing from workspace (e.g. deleted by Skip EA Logo setting),
    /// AnalyzeWorkspaceDeltaAsync treats it as a Skip operation instead of forcing a workspace rebuild.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_MissingEaLogo_ProducesSkipOperationAsync()
    {
        // Arrange
        var logoFile = Path.Combine("Data", "English", "Movies", "EA_LOGO.BIK");
        var manifest = CreateManifest(ContentType.GameInstallation, logoFile, "logo-hash");
        var workspacePath = Path.Combine(_testDirectory, "ws1");
        Directory.CreateDirectory(workspacePath);

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "ws1",
            WorkspacePath = workspacePath,
            Strategy = WorkspaceStrategy.HardLink,
        };

        var config = new WorkspaceConfiguration
        {
            Id = "ws1",
            WorkspaceRootPath = _testDirectory,
            Manifests = new List<ContentManifest> { manifest },
            Strategy = WorkspaceStrategy.HardLink,
        };

        // Act
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(workspaceInfo, config);

        // Assert
        Assert.Single(result);
        Assert.Equal(WorkspaceDeltaOperation.Skip, result[0].Operation);
    }

    /// <summary>
    /// Verifies that runtime workspace files like receipts and log files are not flagged as orphan Remove operations.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_RuntimeWorkspaceFiles_IgnoredFromRemoveDeltasAsync()
    {
        // Arrange
        var normalFile = "generals.exe";
        var manifest = CreateManifest(ContentType.GameInstallation, normalFile, "exe-hash");
        var workspacePath = Path.Combine(_testDirectory, "ws2");
        Directory.CreateDirectory(workspacePath);

        // Create runtime artifacts in the workspace directory
        await File.WriteAllTextAsync(Path.Combine(workspacePath, normalFile), "exe content");
        await File.WriteAllTextAsync(Path.Combine(workspacePath, "launch.receipt.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(workspacePath, "game.log"), "log content");

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "ws2",
            WorkspacePath = workspacePath,
            Strategy = WorkspaceStrategy.HardLink,
        };

        var config = new WorkspaceConfiguration
        {
            Id = "ws2",
            WorkspaceRootPath = _testDirectory,
            Manifests = new List<ContentManifest> { manifest },
            Strategy = WorkspaceStrategy.HardLink,
        };

        // Act
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(workspaceInfo, config);

        // Assert
        var removeDeltas = result.FindAll(d => d.Operation == WorkspaceDeltaOperation.Remove);
        Assert.Empty(removeDeltas);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Creates a test manifest with a single file.
    /// </summary>
    /// <param name="contentType">The content type for the manifest.</param>
    /// <param name="relativePath">The relative file path.</param>
    /// <param name="hash">The file hash.</param>
    /// <returns>A content manifest for testing.</returns>
    private static ContentManifest CreateManifest(ContentType contentType, string relativePath, string hash)
    {
        var typeStr = contentType.ToString().ToLowerInvariant();
        return new ContentManifest
        {
            Id = ManifestId.Create($"1.0.genhub.{typeStr}.testmanifest"),
            ContentType = contentType,
            Name = $"Test {contentType}",
            Version = "1.0.0",
            Files = new List<ManifestFile>
            {
                new()
                {
                    RelativePath = relativePath,
                    Hash = hash,
                    Size = 1024,
                },
            },
        };
    }
}
