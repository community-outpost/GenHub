using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
        _reconciler = new WorkspaceReconciler(_mockLogger.Object);
    }

    /// <summary>
    /// Verifies that when a Mod and GameInstallation both provide the same file,
    /// the Mod version wins due to higher priority (100 vs 10).
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_ModVsGameInstallation_ModWins()
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
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(null, config, CancellationToken.None);

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
    public async Task AnalyzeWorkspaceDelta_PatchVsGameClient_PatchWins()
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
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(null, config, CancellationToken.None);

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
    public async Task AnalyzeWorkspaceDelta_GameClientVsGameInstallation_GameClientWins()
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
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(null, config, CancellationToken.None);

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
    public async Task AnalyzeWorkspaceDelta_ThreeWayConflict_HighestPriorityWins()
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
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(null, config, CancellationToken.None);

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
    public async Task AnalyzeWorkspaceDelta_NoConflict_FileAddedNormally()
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
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(null, config, CancellationToken.None);

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
    public async Task AnalyzeWorkspaceDelta_ConflictOccurs_LogsWarning()
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
        await _reconciler.AnalyzeWorkspaceDeltaAsync(null, config, CancellationToken.None);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString() !.Contains("File conflict")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
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
