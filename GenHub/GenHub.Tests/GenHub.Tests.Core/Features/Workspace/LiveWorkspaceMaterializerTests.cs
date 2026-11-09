using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Workspace.Live;
using Microsoft.Extensions.Logging.Abstractions;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Exercises the live overlay lifecycle: replacing snapshot paths, incremental removal,
/// hole detection and clearing the last live package.
/// </summary>
public sealed class LiveWorkspaceMaterializerTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;
    private readonly string _sourceA;
    private readonly string _sourceB;
    private readonly LiveWorkspaceMaterializer _materializer;
    private readonly FakeLinker _linker;
    private readonly MemoryStateStore _stateStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveWorkspaceMaterializerTests"/> class.
    /// </summary>
    public LiveWorkspaceMaterializerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "genhub-live-tests-" + Guid.NewGuid().ToString("N"));
        _workspace = Path.Combine(_root, "workspace");
        _sourceA = Path.Combine(_root, "source-a");
        _sourceB = Path.Combine(_root, "source-b");

        Directory.CreateDirectory(Path.Combine(_workspace, "Data"));
        Directory.CreateDirectory(Path.Combine(_sourceA, "Data"));
        Directory.CreateDirectory(Path.Combine(_sourceB, "Data"));

        var realLinker = new LiveWorkspaceLinker(NullLogger<LiveWorkspaceLinker>.Instance);
        _linker = new FakeLinker(realLinker);
        _stateStore = new MemoryStateStore();
        _materializer = new LiveWorkspaceMaterializer(
            _linker,
            _stateStore,
            NullLogger<LiveWorkspaceMaterializer>.Instance);
    }

    /// <summary>
    /// A live file replaces a snapshot file at the same path, and a later source edit is
    /// refreshed while a simulated game write is preserved.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ApplyAsync_LiveCopyOverridesSnapshotAndRefreshesSourceEditAsync()
    {
        var snapshotFile = Path.Combine(_workspace, "Data", "Shared.ini");
        await File.WriteAllTextAsync(snapshotFile, "snapshot");

        var sourceFile = Path.Combine(_sourceA, "Data", "Shared.ini");
        await File.WriteAllTextAsync(sourceFile, "live");

        var snapshot = CreateManifest("1.0.local.mod.snapshot", ContentSourceMode.Snapshot, "Data/Shared.ini");
        var live = CreateManifest("1.0.local.mod.live-a", ContentSourceMode.Live, "Data/Shared.ini", _sourceA);
        var info = CreateWorkspace();
        var config = CreateConfiguration(snapshot, live);

        var applied = await _materializer.ApplyAsync(
            info,
            config,
            CreateConfiguration(snapshot),
            null,
            CancellationToken.None);

        Assert.True(applied.Success, applied.FirstError);
        Assert.Equal("live", await File.ReadAllTextAsync(snapshotFile));

        await File.WriteAllTextAsync(snapshotFile, "game-write");
        var rescued = await _materializer.ResyncAsync(
            info,
            config,
            CreateConfiguration(snapshot),
            CancellationToken.None);
        Assert.True(rescued.Success, rescued.FirstError);
        Assert.Equal("game-write", await File.ReadAllTextAsync(snapshotFile));

        await Task.Delay(1100);
        await File.WriteAllTextAsync(sourceFile, "live-edit");
        var refreshed = await _materializer.ResyncAsync(
            info,
            config,
            CreateConfiguration(snapshot),
            CancellationToken.None);
        Assert.True(refreshed.Success, refreshed.FirstError);
        Assert.Equal("live-edit", await File.ReadAllTextAsync(snapshotFile));
    }

    /// <summary>
    /// Removing one of two live packages keeps the other package and the snapshot file.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ApplyAsync_IncrementalRemoveKeepsSnapshotAndOtherLiveAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace, "Data", "Snapshot.ini"), "snapshot");
        await File.WriteAllTextAsync(Path.Combine(_sourceA, "Data", "A.ini"), "A");
        await File.WriteAllTextAsync(Path.Combine(_sourceB, "Data", "B.ini"), "B");

        var snapshot = CreateManifest("1.0.local.mod.snapshot", ContentSourceMode.Snapshot, "Data/Snapshot.ini");
        var liveA = CreateManifest("1.0.local.mod.live-a", ContentSourceMode.Live, "Data/A.ini", _sourceA);
        var liveB = CreateManifest("1.0.local.mod.live-b", ContentSourceMode.Live, "Data/B.ini", _sourceB);
        var info = CreateWorkspace();
        var full = CreateConfiguration(snapshot, liveA, liveB);
        var snapshotConfig = CreateConfiguration(snapshot);

        var initial = await _materializer.ApplyAsync(info, full, snapshotConfig, null, CancellationToken.None);
        Assert.True(initial.Success, initial.FirstError);

        var bOnly = CreateConfiguration(snapshot, liveB);
        Assert.True(await _materializer.CanApplyIncrementallyAsync(info, bOnly, snapshotConfig, CancellationToken.None));

        var removed = await _materializer.ApplyAsync(
            info,
            bOnly,
            snapshotConfig,
            null,
            CancellationToken.None,
            replaceSnapshotTargets: false);
        Assert.True(removed.Success, removed.FirstError);
        Assert.False(File.Exists(Path.Combine(_workspace, "Data", "A.ini")));
        Assert.True(File.Exists(Path.Combine(_workspace, "Data", "B.ini")));
        Assert.Equal("snapshot", await File.ReadAllTextAsync(Path.Combine(_workspace, "Data", "Snapshot.ini")));
    }

    /// <summary>
    /// Clearing the last live package deletes only live entries and preserves snapshot files.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ClearAsync_RemovesOnlyLiveEntriesAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace, "Data", "Snapshot.ini"), "snapshot");
        await File.WriteAllTextAsync(Path.Combine(_sourceB, "Data", "B.ini"), "B");

        var snapshot = CreateManifest("1.0.local.mod.snapshot", ContentSourceMode.Snapshot, "Data/Snapshot.ini");
        var liveB = CreateManifest("1.0.local.mod.live-b", ContentSourceMode.Live, "Data/B.ini", _sourceB);
        var info = CreateWorkspace();
        var snapshotConfig = CreateConfiguration(snapshot);

        var applied = await _materializer.ApplyAsync(
            info,
            CreateConfiguration(snapshot, liveB),
            snapshotConfig,
            null,
            CancellationToken.None);
        Assert.True(applied.Success, applied.FirstError);
        Assert.True(File.Exists(Path.Combine(_workspace, "Data", "B.ini")));

        Assert.True(await _materializer.CanClearIncrementallyAsync(info, snapshotConfig));
        var cleared = await _materializer.ClearAsync(info, CancellationToken.None);
        Assert.True(cleared.Success, cleared.FirstError);
        Assert.False(File.Exists(Path.Combine(_workspace, "Data", "B.ini")));
        Assert.Equal("snapshot", await File.ReadAllTextAsync(Path.Combine(_workspace, "Data", "Snapshot.ini")));
        Assert.False(info.IsLive);
    }

    /// <summary>
    /// Removing a live entry that shadowed a snapshot path fails before mutating the workspace.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ResyncAsync_RemovingShadowingSourceFailsClosedAsync()
    {
        var sourceFile = Path.Combine(_sourceA, "Data", "Shared.ini");
        await File.WriteAllTextAsync(sourceFile, "live");
        var snapshotFile = Path.Combine(_workspace, "Data", "Shared.ini");
        await File.WriteAllTextAsync(snapshotFile, "snapshot");

        var snapshot = CreateManifest("1.0.local.mod.snapshot", ContentSourceMode.Snapshot, "Data/Shared.ini");
        var live = CreateManifest("1.0.local.mod.live-a", ContentSourceMode.Live, "Data/Shared.ini", _sourceA);
        var info = CreateWorkspace();
        var config = CreateConfiguration(snapshot, live);
        var snapshotConfig = CreateConfiguration(snapshot);

        var applied = await _materializer.ApplyAsync(info, config, snapshotConfig, null, CancellationToken.None);
        Assert.True(applied.Success, applied.FirstError);
        Assert.Equal("live", await File.ReadAllTextAsync(snapshotFile));

        File.Delete(sourceFile);
        var resync = await _materializer.ResyncAsync(info, config, snapshotConfig, CancellationToken.None);
        Assert.False(resync.Success);
        Assert.Equal("live", await File.ReadAllTextAsync(snapshotFile));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of a unique temporary test root.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup of a unique temporary test root.
        }
    }

    private WorkspaceInfo CreateWorkspace() => new()
    {
        Id = "live-materializer-test",
        WorkspacePath = _workspace,
        Strategy = WorkspaceStrategy.HardLink,
        IsPrepared = true,
    };

    private WorkspaceConfiguration CreateConfiguration(params ContentManifest[] manifests)
    {
        var configuration = new WorkspaceConfiguration
        {
            Id = "live-materializer-test",
            Manifests = [.. manifests],
            WorkspaceRootPath = _root,
            BaseInstallationPath = _sourceA,
            Strategy = WorkspaceStrategy.HardLink,
            ValidateAfterPreparation = false,
        };

        foreach (var manifest in manifests.Where(manifest => manifest.SourceMode == ContentSourceMode.Live))
        {
            configuration.ManifestSourcePaths[manifest.Id.Value] = manifest.SourcePath!;
        }

        return configuration;
    }

    private ContentManifest CreateManifest(
        string id,
        ContentSourceMode mode,
        string relativePath,
        string? sourcePath = null)
    {
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create(id),
            Name = id,
            Version = "1.0",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceMode = mode,
            SourcePath = sourcePath,
        };

        manifest.Files.Add(new ManifestFile
        {
            RelativePath = relativePath,
            SourceType = ContentSourceType.LocalFile,
            InstallTarget = ContentInstallTarget.Workspace,
        });

        return manifest;
    }

    private sealed class FakeLinker(LiveWorkspaceLinker realLinker) : ILiveWorkspaceLinker
    {
        private readonly Dictionary<string, string> _links = new(StringComparer.OrdinalIgnoreCase);

        public OperationResult EnsureLink(string workspaceRoot, string relativePath, string sourcePath)
        {
            var target = Path.Combine(workspaceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                File.Delete(target);
            }

            File.WriteAllText(target, "LINK:" + sourcePath);
            _links[target] = sourcePath;
            return OperationResult.CreateSuccess();
        }

        public OperationResult EnsureCopy(string workspaceRoot, string relativePath, string sourcePath) =>
            realLinker.EnsureCopy(workspaceRoot, relativePath, sourcePath);

        public bool TryDeleteLiveLink(string linkPath, IReadOnlyList<string> liveRoots)
        {
            if (!_links.Remove(linkPath))
            {
                return false;
            }

            if (File.Exists(linkPath))
            {
                File.Delete(linkPath);
            }

            return true;
        }

        public bool IsLinkTo(string path, string sourcePath) =>
            _links.TryGetValue(path, out var target) &&
            string.Equals(target, sourcePath, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MemoryStateStore : ILiveWorkspaceStateStore
    {
        private readonly Dictionary<string, LiveWorkspaceState> _states = new(StringComparer.Ordinal);

        public LiveWorkspaceState Load(string workspaceId) =>
            _states.TryGetValue(workspaceId, out var state)
                ? new LiveWorkspaceState { WorkspaceId = state.WorkspaceId, Entries = [.. state.Entries] }
                : new LiveWorkspaceState { WorkspaceId = workspaceId };

        public void Save(LiveWorkspaceState state) =>
            _states[state.WorkspaceId] = new LiveWorkspaceState
            {
                WorkspaceId = state.WorkspaceId,
                Entries = [.. state.Entries],
            };

        public void Delete(string workspaceId) => _states.Remove(workspaceId);
    }
}
