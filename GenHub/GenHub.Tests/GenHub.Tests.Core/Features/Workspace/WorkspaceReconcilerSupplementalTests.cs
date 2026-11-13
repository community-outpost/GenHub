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
using System.Linq;
using System.Threading;
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
        var (workspaceInfo, config) = await CreateLargeSameSizeWorkspaceAsync("ws8");

        // Act
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(workspaceInfo, config);

        // Assert
        var removeDeltas = result.FindAll(d => d.Operation == WorkspaceDeltaOperation.Remove);
        Assert.Empty(removeDeltas);
    }

    /// <summary>
    /// Verifies that a workspace copy whose supplemental source disappeared (e.g. the base
    /// install was uninstalled) is flagged for removal so the workspace self-strips content
    /// it can no longer verify.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_MissingSupplementalSource_RemovesWorkspaceCopyAsync()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_testDirectory, "generals");
        Directory.CreateDirectory(supplementalRoot);
        var sourceArchive = Path.Combine(supplementalRoot, "Textures.big");
        File.WriteAllText(sourceArchive, "generals textures");

        var workspacePath = Path.Combine(_testDirectory, "ws9");
        Directory.CreateDirectory(workspacePath);
        File.WriteAllText(Path.Combine(workspacePath, "game.dat"), "game binary");
        File.WriteAllText(Path.Combine(workspacePath, "Textures.big"), "generals textures");

        var (workspaceInfo, config) = CreateWorkspace("ws9", workspacePath, supplementalRoot);
        File.Delete(sourceArchive);

        // Act
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(workspaceInfo, config);

        // Assert
        var removeDeltas = result.FindAll(d => d.Operation == WorkspaceDeltaOperation.Remove);
        Assert.Contains(removeDeltas, d => string.Equals(Path.GetFileName(d.WorkspacePath), "Textures.big", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that a forced full verification byte-compares even large supplemental copies
    /// instead of trusting size alone, mirroring the manifest staleness standard.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_LargeSameSizeFileWithFullVerification_StillRemovedAsync()
    {
        // Arrange
        var (workspaceInfo, config) = await CreateLargeSameSizeWorkspaceAsync("ws10");

        // Act
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(workspaceInfo, config, forceFullVerification: true);

        // Assert
        var removeDeltas = result.FindAll(d => d.Operation == WorkspaceDeltaOperation.Remove);
        Assert.Contains(removeDeltas, d => string.Equals(Path.GetFileName(d.WorkspacePath), "Textures.big", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that a forced full verification accepts a large supplemental copy whose
    /// content is identical, streaming the comparison instead of loading both archives
    /// fully into memory.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_LargeIdenticalFileWithFullVerification_ProducesNoRemoveDeltaAsync()
    {
        // Arrange
        var (workspaceInfo, config) = await CreateLargeWorkspaceAsync("ws12", null);

        // Act
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(workspaceInfo, config, forceFullVerification: true);

        // Assert
        var removeDeltas = result.FindAll(d => d.Operation == WorkspaceDeltaOperation.Remove);
        Assert.Empty(removeDeltas);
    }

    /// <summary>
    /// Verifies that a forced full verification still detects a difference past the first
    /// comparison chunk: the streaming comparison must read to the end rather than trust
    /// an early prefix.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_LargeFileDifferingAtEndWithFullVerification_StillRemovedAsync()
    {
        // Arrange
        var (workspaceInfo, config) = await CreateLargeWorkspaceAsync("ws13", bytes => bytes[bytes.Length - 1] = 1);

        // Act
        var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(workspaceInfo, config, forceFullVerification: true);

        // Assert
        var removeDeltas = result.FindAll(d => d.Operation == WorkspaceDeltaOperation.Remove);
        Assert.Contains(removeDeltas, d => string.Equals(Path.GetFileName(d.WorkspacePath), "Textures.big", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that streams short-reading asymmetrically (legal on network mounts) still
    /// compare aligned: each chunk is filled fully before comparing, so identical content
    /// is not misreported as different.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StreamsHaveIdenticalContent_AsymmetricShortReadsOnIdenticalContent_ReturnsTrueAsync()
    {
        // Arrange
        var content = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        using var first = new CappedReadStream(new MemoryStream(content), 7);
        using var second = new CappedReadStream(new MemoryStream(content), 13);

        // Act
        var result = await WorkspaceReconciler.StreamsHaveIdenticalContentAsync(first, second, CancellationToken.None);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// Verifies that chunk filling does not mask a genuine difference under asymmetric
    /// short reads.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StreamsHaveIdenticalContent_AsymmetricShortReadsOnDifferentContent_ReturnsFalseAsync()
    {
        // Arrange
        var firstBytes = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        var secondBytes = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        secondBytes[secondBytes.Length - 1] = 255;
        using var first = new CappedReadStream(new MemoryStream(firstBytes), 7);
        using var second = new CappedReadStream(new MemoryStream(secondBytes), 13);

        // Act
        var result = await WorkspaceReconciler.StreamsHaveIdenticalContentAsync(first, second, CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Verifies that an unreadable supplemental root degrades to treating linked archives as
    /// orphans, forcing one workspace recreation rather than silently keeping content that
    /// can no longer be verified.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDelta_UnreadableSupplementalRoot_TreatsLinkedArchivesAsOrphansAsync()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            return;
        }

        // Arrange
        var parent = Directory.CreateDirectory(Path.Combine(_testDirectory, "locked")).FullName;
        var supplementalRoot = Directory.CreateDirectory(Path.Combine(parent, "generals")).FullName;
        File.WriteAllText(Path.Combine(supplementalRoot, "Textures.big"), "generals textures");

        var workspacePath = Path.Combine(_testDirectory, "ws11");
        Directory.CreateDirectory(workspacePath);
        File.WriteAllText(Path.Combine(workspacePath, "game.dat"), "game binary");
        File.WriteAllText(Path.Combine(workspacePath, "Textures.big"), "generals textures");

        var (workspaceInfo, config) = CreateWorkspace("ws11", workspacePath, supplementalRoot);
        File.SetUnixFileMode(parent, UnixFileMode.UserWrite);

        try
        {
            // Act
            var result = await _reconciler.AnalyzeWorkspaceDeltaAsync(workspaceInfo, config);

            // Assert
            var removeDeltas = result.FindAll(d => d.Operation == WorkspaceDeltaOperation.Remove);
            Assert.Contains(removeDeltas, d => string.Equals(Path.GetFileName(d.WorkspacePath), "Textures.big", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.SetUnixFileMode(
                parent,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
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

    private async Task<(WorkspaceInfo WorkspaceInfo, WorkspaceConfiguration Config)> CreateLargeSameSizeWorkspaceAsync(string id)
    {
        return await CreateLargeWorkspaceAsync(id, bytes => bytes[0] = 1);
    }

    private async Task<(WorkspaceInfo WorkspaceInfo, WorkspaceConfiguration Config)> CreateLargeWorkspaceAsync(
        string id,
        Action<byte[]>? mutateWorkspaceBytes)
    {
        var supplementalRoot = Path.Combine(_testDirectory, "generals");
        Directory.CreateDirectory(supplementalRoot);
        await File.WriteAllBytesAsync(Path.Combine(supplementalRoot, "Textures.big"), new byte[6 * 1024 * 1024]);

        var workspacePath = Path.Combine(_testDirectory, id);
        Directory.CreateDirectory(workspacePath);
        File.WriteAllText(Path.Combine(workspacePath, "game.dat"), "game binary");
        var workspaceBytes = new byte[6 * 1024 * 1024];
        mutateWorkspaceBytes?.Invoke(workspaceBytes);
        await File.WriteAllBytesAsync(Path.Combine(workspacePath, "Textures.big"), workspaceBytes);

        return CreateWorkspace(id, workspacePath, supplementalRoot);
    }

    /// <summary>
    /// A stream wrapper capping every read at a fixed size, simulating the short reads
    /// network mounts may legally return.
    /// </summary>
    private sealed class CappedReadStream(Stream inner, int maxBytesPerRead) : Stream
    {
        /// <inheritdoc/>
        public override bool CanRead => true;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => false;

        /// <inheritdoc/>
        public override long Length => inner.Length;

        /// <inheritdoc/>
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        /// <inheritdoc/>
        public override void Flush() => inner.Flush();

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, Math.Min(count, maxBytesPerRead));

        /// <inheritdoc/>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer[..Math.Min(buffer.Length, maxBytesPerRead)], cancellationToken);

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        /// <inheritdoc/>
        public override void SetLength(long value) => inner.SetLength(value);

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
