using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Workspace;
using GenHub.Tests.Core.Helpers;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Tests that supplemental archives (base Generals content linked into a Zero Hour workspace)
/// are not mistaken for orphan files by delta analysis, while genuine orphans still are.
/// </summary>
public class WorkspaceReconcilerSupplementalTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly WorkspaceReconciler _reconciler;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceReconcilerSupplementalTests"/> class.
    /// </summary>
    public WorkspaceReconcilerSupplementalTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"GenHubTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        var mockLogger = new Mock<ILogger<WorkspaceReconciler>>();
        var mockFileOps = new Mock<IFileOperationsService>();
        _reconciler = new WorkspaceReconciler(mockLogger.Object, mockFileOps.Object);
    }

    /// <summary>
    /// Verifies that supplemental archives materialized as regular files (copy fallback) are not
    /// flagged as orphan Remove operations.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_SupplementalArchives_ProduceNoRemoveDeltasAsync()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_testDirectory, "generals");
        Directory.CreateDirectory(supplementalRoot);
        File.WriteAllText(Path.Combine(supplementalRoot, "Textures.big"), "generals textures");

        var workspacePath = Path.Combine(_testDirectory, "ws1");
        Directory.CreateDirectory(workspacePath);
        File.WriteAllText(Path.Combine(workspacePath, "game.dat"), "game binary");
        File.WriteAllText(Path.Combine(workspacePath, "Textures.big"), "generals textures");

        var (workspaceInfo, config) = CreateWorkspace("ws1", workspacePath, supplementalRoot);

        // Act
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(workspaceInfo, config);

        // Assert
        var removeDeltas = result.FindAll(d => d.Operation == WorkspaceDeltaOperation.Remove);
        Assert.Empty(removeDeltas);
    }

    /// <summary>
    /// Verifies that a supplemental archive linked into the workspace root is not flagged as an
    /// orphan Remove operation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_SupplementalLinkUnderRoot_ProducesNoRemoveDeltaAsync()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_testDirectory, "generals");
        Directory.CreateDirectory(supplementalRoot);
        var sourceArchive = Path.Combine(supplementalRoot, "Textures.big");
        File.WriteAllText(sourceArchive, "generals textures");

        var workspacePath = Path.Combine(_testDirectory, "ws2");
        Directory.CreateDirectory(workspacePath);
        File.WriteAllText(Path.Combine(workspacePath, "game.dat"), "game binary");

        if (!SymlinkTestHelper.TryCreateFileSymlink(Path.Combine(workspacePath, "Textures.big"), sourceArchive))
        {
            return;
        }

        var (workspaceInfo, config) = CreateWorkspace("ws2", workspacePath, supplementalRoot);

        // Act
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(workspaceInfo, config);

        // Assert
        var removeDeltas = result.FindAll(d => d.Operation == WorkspaceDeltaOperation.Remove);
        Assert.Empty(removeDeltas);
    }

    /// <summary>
    /// Verifies that a link sharing a supplemental archive's name but pointing outside the
    /// current root (a leftover from a previous root) is still flagged for removal so a
    /// recreation cleans it up.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_ForeignLinkWithSupplementalName_StillRemovedAsync()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_testDirectory, "generals");
        Directory.CreateDirectory(supplementalRoot);
        File.WriteAllText(Path.Combine(supplementalRoot, "Textures.big"), "generals textures");

        var previousRoot = Path.Combine(_testDirectory, "previous");
        Directory.CreateDirectory(previousRoot);
        var previousArchive = Path.Combine(previousRoot, "Textures.big");
        File.WriteAllText(previousArchive, "previous textures");

        var workspacePath = Path.Combine(_testDirectory, "ws3");
        Directory.CreateDirectory(workspacePath);
        File.WriteAllText(Path.Combine(workspacePath, "game.dat"), "game binary");

        if (!SymlinkTestHelper.TryCreateFileSymlink(Path.Combine(workspacePath, "Textures.big"), previousArchive))
        {
            return;
        }

        var (workspaceInfo, config) = CreateWorkspace("ws3", workspacePath, supplementalRoot);

        // Act
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(workspaceInfo, config);

        // Assert
        var removeDeltas = result.FindAll(d => d.Operation == WorkspaceDeltaOperation.Remove);
        Assert.Contains(removeDeltas, d => string.Equals(Path.GetFileName(d.WorkspacePath), "Textures.big", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that unrelated orphan files are still flagged for removal when a supplemental
    /// root is configured.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_OrphanNonSupplementalFile_StillRemovedAsync()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_testDirectory, "generals");
        Directory.CreateDirectory(supplementalRoot);
        File.WriteAllText(Path.Combine(supplementalRoot, "Textures.big"), "generals textures");

        var workspacePath = Path.Combine(_testDirectory, "ws4");
        Directory.CreateDirectory(workspacePath);
        File.WriteAllText(Path.Combine(workspacePath, "game.dat"), "game binary");
        File.WriteAllText(Path.Combine(workspacePath, "stray.txt"), "orphan content");

        var (workspaceInfo, config) = CreateWorkspace("ws4", workspacePath, supplementalRoot);

        // Act
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(workspaceInfo, config);

        // Assert
        var removeDeltas = result.FindAll(d => d.Operation == WorkspaceDeltaOperation.Remove);
        Assert.Contains(removeDeltas, d => string.Equals(Path.GetFileName(d.WorkspacePath), "stray.txt", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that a regular file carrying a supplemental name but stale content (e.g. a
    /// disabled mod's override) is still flagged for removal instead of shadowing the base
    /// archive indefinitely.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_StaleRegularFileWithSupplementalName_StillRemovedAsync()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_testDirectory, "generals");
        Directory.CreateDirectory(supplementalRoot);
        File.WriteAllText(Path.Combine(supplementalRoot, "Textures.big"), "generals textures");

        var workspacePath = Path.Combine(_testDirectory, "ws6");
        Directory.CreateDirectory(workspacePath);
        File.WriteAllText(Path.Combine(workspacePath, "game.dat"), "game binary");
        File.WriteAllText(Path.Combine(workspacePath, "Textures.big"), "stale mod content with a different size");

        var (workspaceInfo, config) = CreateWorkspace("ws6", workspacePath, supplementalRoot);

        // Act
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(workspaceInfo, config);

        // Assert
        var removeDeltas = result.FindAll(d => d.Operation == WorkspaceDeltaOperation.Remove);
        Assert.Contains(removeDeltas, d => string.Equals(Path.GetFileName(d.WorkspacePath), "Textures.big", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that a same-size small file with different content is still flagged for
    /// removal: small files are compared byte for byte.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_SameSizeSmallFileWithDifferentContent_StillRemovedAsync()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_testDirectory, "generals");
        Directory.CreateDirectory(supplementalRoot);
        File.WriteAllText(Path.Combine(supplementalRoot, "Textures.big"), "aaaaaaaaaa");

        var workspacePath = Path.Combine(_testDirectory, "ws7");
        Directory.CreateDirectory(workspacePath);
        File.WriteAllText(Path.Combine(workspacePath, "game.dat"), "game binary");
        File.WriteAllText(Path.Combine(workspacePath, "Textures.big"), "bbbbbbbbbb");

        var (workspaceInfo, config) = CreateWorkspace("ws7", workspacePath, supplementalRoot);

        // Act
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(workspaceInfo, config);

        // Assert
        var removeDeltas = result.FindAll(d => d.Operation == WorkspaceDeltaOperation.Remove);
        Assert.Contains(removeDeltas, d => string.Equals(Path.GetFileName(d.WorkspacePath), "Textures.big", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that a size-matching large file is trusted without a byte comparison, mirroring
    /// the manifest staleness standard that skips hashing multi-megabyte files every run.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_LargeSameSizeFile_TrustedWithoutComparisonAsync()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_testDirectory, "generals");
        Directory.CreateDirectory(supplementalRoot);
        await File.WriteAllBytesAsync(Path.Combine(supplementalRoot, "Textures.big"), new byte[6 * 1024 * 1024]);

        var workspacePath = Path.Combine(_testDirectory, "ws8");
        Directory.CreateDirectory(workspacePath);
        File.WriteAllText(Path.Combine(workspacePath, "game.dat"), "game binary");
        var workspaceBytes = new byte[6 * 1024 * 1024];
        workspaceBytes[0] = 1;
        await File.WriteAllBytesAsync(Path.Combine(workspacePath, "Textures.big"), workspaceBytes);

        var (workspaceInfo, config) = CreateWorkspace("ws8", workspacePath, supplementalRoot);

        // Act
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(workspaceInfo, config);

        // Assert
        var removeDeltas = result.FindAll(d => d.Operation == WorkspaceDeltaOperation.Remove);
        Assert.Empty(removeDeltas);
    }

    /// <summary>
    /// Verifies that the supplemental exclusion applies only at the workspace root: an archive
    /// name inside a subdirectory is still an orphan.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_SupplementalNameInSubdirectory_StillRemovedAsync()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_testDirectory, "generals");
        Directory.CreateDirectory(supplementalRoot);
        File.WriteAllText(Path.Combine(supplementalRoot, "Textures.big"), "generals textures");

        var workspacePath = Path.Combine(_testDirectory, "ws5");
        Directory.CreateDirectory(Path.Combine(workspacePath, "Data"));
        File.WriteAllText(Path.Combine(workspacePath, "game.dat"), "game binary");
        File.WriteAllText(Path.Combine(workspacePath, "Data", "Textures.big"), "nested content");

        var (workspaceInfo, config) = CreateWorkspace("ws5", workspacePath, supplementalRoot);

        // Act
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(workspaceInfo, config);

        // Assert
        var removeDeltas = result.FindAll(d => d.Operation == WorkspaceDeltaOperation.Remove);
        Assert.Contains(removeDeltas, d => d.WorkspacePath.EndsWith(Path.Combine("Data", "Textures.big"), StringComparison.OrdinalIgnoreCase));
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

    private static (WorkspaceInfo WorkspaceInfo, WorkspaceConfiguration Config) CreateWorkspace(
        string id,
        string workspacePath,
        string supplementalRoot)
    {
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.genhub.gameinstallation.testmanifest"),
            ContentType = ContentType.GameInstallation,
            Name = "Test installation",
            Version = "1.0.0",
            Files = new List<ManifestFile>
            {
                new()
                {
                    RelativePath = "game.dat",
                    Hash = "game-hash",
                    Size = 1024,
                },
            },
        };

        var workspaceInfo = new WorkspaceInfo
        {
            Id = id,
            WorkspacePath = workspacePath,
            Strategy = WorkspaceStrategy.HardLink,
        };

        var config = new WorkspaceConfiguration
        {
            Id = id,
            WorkspaceRootPath = Path.GetDirectoryName(workspacePath) ?? string.Empty,
            Manifests = new List<ContentManifest> { manifest },
            Strategy = WorkspaceStrategy.HardLink,
            SupplementalArchiveRoot = supplementalRoot,
        };

        return (workspaceInfo, config);
    }
}
