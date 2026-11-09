using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using GenHub.Core.Models.Workspace;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Workspace.Live;

/// <summary>
/// Overlays live content manifests onto a workspace prepared by the profile's normal
/// strategy. The snapshot manifests are materialized first; this pass links the live
/// folders on top of them.
/// </summary>
public sealed class LiveWorkspaceMaterializer(
    ILiveWorkspaceLinker linker,
    ILiveWorkspaceStateStore stateStore,
    ILogger<LiveWorkspaceMaterializer> logger,
    ILocalizationService? localizationService = null)
{
    /// <summary>
    /// Applies every live manifest to an existing workspace.
    /// </summary>
    /// <param name="workspaceInfo">The workspace prepared by the snapshot strategy.</param>
    /// <param name="configuration">The full profile configuration, including live manifests.</param>
    /// <param name="snapshotConfiguration">The same configuration restricted to snapshot manifests.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="replaceSnapshotTargets">When true, live entries replace whatever the
    /// snapshot strategy materialized at the same path. False during watcher rescans, where
    /// a newer workspace copy may be a game write that must be preserved.</param>
    /// <returns>The updated workspace information, or a failure when linking is impossible.</returns>
    public async Task<OperationResult<WorkspaceInfo>> ApplyAsync(
        WorkspaceInfo workspaceInfo,
        WorkspaceConfiguration configuration,
        WorkspaceConfiguration snapshotConfiguration,
        IProgress<WorkspacePreparationProgress>? progress,
        CancellationToken cancellationToken,
        bool replaceSnapshotTargets = true)
    {
        ArgumentNullException.ThrowIfNull(workspaceInfo);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(snapshotConfiguration);

        var liveManifests = configuration.Manifests
            .Where(manifest => manifest.SourceMode == ContentSourceMode.Live)
            .ToList();

        if (liveManifests.Count == 0)
        {
            return OperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo);
        }

        if (string.IsNullOrWhiteSpace(workspaceInfo.WorkspacePath))
        {
            return OperationResult<WorkspaceInfo>.CreateFailure(
                GetLiveError(
                    "Workspace.Live.Error.NoWorkspace",
                    "Cannot mount live content without a workspace path."));
        }

        var snapshotPaths = BuildReservationPaths(snapshotConfiguration);
        var snapshotSizes = BuildSnapshotSizes(snapshotConfiguration);
        var liveRoots = new List<LiveRootBinding>();
        var plan = new Dictionary<string, LivePlanEntry>(StringComparer.OrdinalIgnoreCase);
        var overriddenSnapshotPaths = 0;

        foreach (var manifest in liveManifests)
        {
            var root = ResolveLiveRoot(configuration, manifest);
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return OperationResult<WorkspaceInfo>.CreateFailure(
                    GetLiveError(
                        "Workspace.Live.Error.MissingSource",
                        "The live content folder for '{0}' is missing: {1}",
                        manifest.Name,
                        root ?? manifest.SourcePath));
            }

            var manifestIndex = configuration.Manifests.IndexOf(manifest);
            var priority = ContentTypePriority.GetPriority(manifest.ContentType);
            liveRoots.Add(new LiveRootBinding(manifest.Id.Value, Path.GetFullPath(root!), priority, manifestIndex));

            foreach (var file in LiveSourceScanner.Enumerate(root!, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = Normalize(file.RelativePath);

                // Live content is the developer's working copy and always wins over snapshot
                // content. The workspace strategy runs first, then this pass replaces any
                // snapshot entry at the same path.
                if (snapshotPaths.Contains(relativePath))
                {
                    overriddenSnapshotPaths++;
                    logger.LogDebug(
                        "[Live] {RelativePath} from {Manifest} overrides the snapshot path",
                        relativePath,
                        manifest.Id.Value);
                }

                var candidate = new LivePlanEntry(
                    relativePath,
                    file.FullPath,
                    priority,
                    manifestIndex,
                    LiveContentConstants.IsProtected(relativePath));
                if (!plan.TryGetValue(relativePath, out var current) ||
                    candidate.Beats(current))
                {
                    plan[relativePath] = candidate;
                }
            }
        }

        logger.LogInformation(
            "[Live] {OverrideCount} live file(s) override snapshot paths",
            overriddenSnapshotPaths);

        var entries = plan.Values.ToList();
        var previousState = stateStore.Load(workspaceInfo.Id);
        var previousCopySources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in previousState.Entries.Where(entry => entry.IsCopy))
        {
            previousCopySources[Normalize(entry.RelativePath)] = entry.SourcePath;
        }

        if (!replaceSnapshotTargets)
        {
            // The watcher calls this path directly. If a source file disappeared and it had
            // replaced a snapshot file, deleting the live entry would leave a hole. Fail
            // before mutating anything; the next prepare detects the retained state entry
            // and performs a full rebuild.
            var hole = previousState.Entries.FirstOrDefault(entry =>
                !plan.ContainsKey(Normalize(entry.RelativePath)) &&
                snapshotPaths.Contains(Normalize(entry.RelativePath)));

            if (hole != null)
            {
                logger.LogWarning(
                    "[Live] Removing {RelativePath} would leave a snapshot path empty; a full rebuild is required",
                    hole.RelativePath);
                return OperationResult<WorkspaceInfo>.CreateFailure(
                    GetLiveError(
                        "Workspace.Live.Error.Hole",
                        "Removing live entry '{0}' would leave a snapshot path empty; a full rebuild is required.",
                        hole.RelativePath));
            }
        }

        logger.LogInformation(
            "[Live] Mounting {Count} live file(s) from {RootCount} folder(s) into {Workspace}",
            entries.Count,
            liveRoots.Count,
            workspaceInfo.WorkspacePath);

        RemoveForeignTargetsForCopies(
            workspaceInfo.WorkspacePath,
            entries,
            previousCopySources,
            replaceSnapshotTargets);
        await LinkEntriesAsync(workspaceInfo.WorkspacePath, entries, progress, cancellationToken).ConfigureAwait(false);

        RemoveStaleStateEntries(workspaceInfo.WorkspacePath, previousState, plan);

        // Include roots recorded on the workspace so links from a live manifest that was
        // removed from the profile are still recognized and deleted even when no state
        // file survives from an older build.
        var cleanupRoots = liveRoots.Select(root => root.Root)
            .Concat(workspaceInfo.LiveSources.Select(source => source.SourceRoot))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        RemoveStaleLinks(
            workspaceInfo.WorkspacePath,
            plan.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            cleanupRoots);

        var liveBytes = entries.Sum(entry => entry.IsCopy
            ? SafeLength(entry.SourcePath)
            : LiveWorkspaceLinker.LinkOverheadBytes);
        var replacedSnapshotEntries = entries
            .Where(entry => snapshotPaths.Contains(entry.RelativePath))
            .ToList();
        var replacedSnapshotBytes = replacedSnapshotEntries
            .Sum(entry => snapshotSizes.GetValueOrDefault(entry.RelativePath));
        var netLiveFileCount = entries.Count - replacedSnapshotEntries.Count;
        var netLiveBytes = liveBytes - replacedSnapshotBytes;

        workspaceInfo.IsLive = true;
        workspaceInfo.LiveSources = [.. liveRoots.Select(root => new LiveSourceBinding(root.ManifestId, root.Root))];
        workspaceInfo.FileCount = Math.Max(0, workspaceInfo.FileCount - workspaceInfo.LiveFileCount) + netLiveFileCount;
        workspaceInfo.TotalSizeBytes = Math.Max(
            0,
            workspaceInfo.TotalSizeBytes - workspaceInfo.LiveSizeBytes + netLiveBytes);
        workspaceInfo.LiveFileCount = netLiveFileCount;
        workspaceInfo.LiveSizeBytes = netLiveBytes;

        stateStore.Save(new LiveWorkspaceState
        {
            WorkspaceId = workspaceInfo.Id,
            Entries = [.. entries.Select(entry => new LiveWorkspaceEntry(entry.RelativePath, entry.SourcePath, entry.IsCopy))],
        });

        return OperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo);
    }

    /// <summary>
    /// Performs a full rescan and reconciliation for an attached live workspace.
    /// </summary>
    /// <param name="workspaceInfo">The workspace to resync.</param>
    /// <param name="configuration">The full profile configuration.</param>
    /// <param name="snapshotConfiguration">The configuration restricted to snapshot manifests.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or a failure describing the first problem.</returns>
    public async Task<OperationResult<WorkspaceInfo>> ResyncAsync(
        WorkspaceInfo workspaceInfo,
        WorkspaceConfiguration configuration,
        WorkspaceConfiguration snapshotConfiguration,
        CancellationToken cancellationToken)
    {
        var result = await ApplyAsync(
            workspaceInfo,
            configuration,
            snapshotConfiguration,
            null,
            cancellationToken,
            replaceSnapshotTargets: false)
            .ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Determines whether applying the current live set can preserve every existing
    /// workspace entry. Returns false when removing a live entry would leave a hole where a
    /// snapshot file used to be, which requires a full rebuild.
    /// </summary>
    /// <param name="workspaceInfo">The existing live workspace.</param>
    /// <param name="configuration">The full profile configuration.</param>
    /// <param name="snapshotConfiguration">The configuration restricted to snapshot manifests.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when the live set can be reconciled incrementally.</returns>
    public Task<bool> CanApplyIncrementallyAsync(
        WorkspaceInfo workspaceInfo,
        WorkspaceConfiguration configuration,
        WorkspaceConfiguration snapshotConfiguration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspaceInfo);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(snapshotConfiguration);

        var currentPaths = CollectCurrentLivePaths(configuration, cancellationToken);
        if (currentPaths == null)
        {
            return Task.FromResult(false);
        }

        var previousState = stateStore.Load(workspaceInfo.Id);
        var currentLiveIds = configuration.Manifests
            .Where(manifest => manifest.SourceMode == ContentSourceMode.Live)
            .Select(manifest => manifest.Id.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (previousState.Entries.Count == 0 &&
            workspaceInfo.LiveSources.Any(source => !currentLiveIds.Contains(source.ManifestId)))
        {
            logger.LogInformation(
                "[Live] Workspace has live sources but no ownership state; a full rebuild is required");
            return Task.FromResult(false);
        }

        return Task.FromResult(CanRemoveEntriesWithoutHoles(workspaceInfo, snapshotConfiguration, currentPaths));
    }

    /// <summary>
    /// Determines whether every live entry can be removed without leaving a hole where a
    /// snapshot file used to be.
    /// </summary>
    /// <param name="workspaceInfo">The existing live workspace.</param>
    /// <param name="snapshotConfiguration">The configuration restricted to snapshot manifests.</param>
    /// <returns><c>true</c> when the live overlay can be cleared incrementally.</returns>
    public Task<bool> CanClearIncrementallyAsync(
        WorkspaceInfo workspaceInfo,
        WorkspaceConfiguration snapshotConfiguration)
    {
        ArgumentNullException.ThrowIfNull(workspaceInfo);
        ArgumentNullException.ThrowIfNull(snapshotConfiguration);

        if (stateStore.Load(workspaceInfo.Id).Entries.Count == 0 &&
            workspaceInfo.LiveSources.Count > 0)
        {
            logger.LogInformation(
                "[Live] Workspace has live sources but no ownership state; a full rebuild is required to clear it");
            return Task.FromResult(false);
        }

        return Task.FromResult(CanRemoveEntriesWithoutHoles(
            workspaceInfo,
            snapshotConfiguration,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Removes every live entry from a workspace while leaving snapshot files untouched.
    /// </summary>
    /// <param name="workspaceInfo">The workspace to clear.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated workspace information, or a failure when an entry is locked.</returns>
    public Task<OperationResult<WorkspaceInfo>> ClearAsync(
        WorkspaceInfo workspaceInfo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspaceInfo);
        cancellationToken.ThrowIfCancellationRequested();

        var previousState = stateStore.Load(workspaceInfo.Id);
        var failures = new List<string>();

        foreach (var entry in previousState.Entries)
        {
            var targetPath = Path.Combine(
                workspaceInfo.WorkspacePath,
                Normalize(entry.RelativePath).Replace('/', Path.DirectorySeparatorChar));

            try
            {
                var existing = new FileInfo(targetPath);
                if (existing.LinkTarget != null)
                {
                    File.Delete(targetPath);
                }
                else if (entry.IsCopy && File.Exists(targetPath))
                {
                    File.SetAttributes(targetPath, FileAttributes.Normal);
                    File.Delete(targetPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "[Live] Could not remove live entry {RelativePath}", entry.RelativePath);
                failures.Add(entry.RelativePath);
            }
        }

        if (failures.Count > 0)
        {
            // Keep the whole state so the next attempt retries every entry.
            return Task.FromResult(OperationResult<WorkspaceInfo>.CreateFailure(
                GetLiveError(
                    "Workspace.Live.Error.Clear",
                    "Could not clear live entries: {0}",
                    string.Join(", ", failures.Take(5)))));
        }

        stateStore.Save(new LiveWorkspaceState { WorkspaceId = workspaceInfo.Id });
        workspaceInfo.IsLive = false;
        workspaceInfo.LiveSources = [];
        workspaceInfo.FileCount = Math.Max(0, workspaceInfo.FileCount - workspaceInfo.LiveFileCount);
        workspaceInfo.TotalSizeBytes = Math.Max(0, workspaceInfo.TotalSizeBytes - workspaceInfo.LiveSizeBytes);
        workspaceInfo.LiveFileCount = 0;
        workspaceInfo.LiveSizeBytes = 0;

        return Task.FromResult(OperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo));
    }

    /// <summary>
    /// Deletes the persisted ownership state for a workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    public void RemoveState(string workspaceId) => stateStore.Delete(workspaceId);

    private static long SafeLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static IReadOnlyList<string> EnumerateWorkspaceFilesNoFollow(string workspacePath)
    {
        var files = new List<string>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(workspacePath));

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            try
            {
                foreach (var file in directory.EnumerateFiles())
                {
                    files.Add(file.FullName);
                }

                foreach (var child in directory.EnumerateDirectories())
                {
                    pending.Push(child);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Skip unreadable directories; validation reports the remaining state.
            }
        }

        return files;
    }

    private static string? ResolveLiveRoot(WorkspaceConfiguration configuration, ContentManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.Id.Value) &&
            configuration.ManifestSourcePaths.TryGetValue(manifest.Id.Value, out var mapped) &&
            !string.IsNullOrWhiteSpace(mapped))
        {
            return mapped;
        }

        return manifest.SourcePath;
    }

    private static Dictionary<string, long> BuildSnapshotSizes(WorkspaceConfiguration snapshotConfiguration)
    {
        var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in snapshotConfiguration.Manifests)
        {
            foreach (var file in ManifestVariantResolver.ResolveFiles(manifest)
                         .Where(file => file.InstallTarget == ContentInstallTarget.Workspace))
            {
                sizes[Normalize(file.RelativePath)] = file.Size;
            }
        }

        return sizes;
    }

    private static HashSet<string> BuildReservationPaths(WorkspaceConfiguration snapshotConfiguration)
    {
        var reservations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var manifest in snapshotConfiguration.Manifests)
        {
            foreach (var file in ManifestVariantResolver.ResolveFiles(manifest)
                         .Where(file => file.InstallTarget == ContentInstallTarget.Workspace))
            {
                reservations.Add(Normalize(file.RelativePath));
            }
        }

        return reservations;
    }

    private static string Normalize(string relativePath) =>
        relativePath.Replace('\\', '/').TrimStart('/');

    private static HashSet<string>? CollectCurrentLivePaths(
        WorkspaceConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var manifest in configuration.Manifests.Where(manifest => manifest.SourceMode == ContentSourceMode.Live))
        {
            var root = ResolveLiveRoot(configuration, manifest);
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return null;
            }

            foreach (var file in LiveSourceScanner.Enumerate(root, cancellationToken))
            {
                paths.Add(Normalize(file.RelativePath));
            }
        }

        return paths;
    }

    private bool CanRemoveEntriesWithoutHoles(
        WorkspaceInfo workspaceInfo,
        WorkspaceConfiguration snapshotConfiguration,
        IReadOnlySet<string> currentPaths)
    {
        var previousState = stateStore.Load(workspaceInfo.Id);
        if (previousState.Entries.Count == 0)
        {
            return true;
        }

        var snapshotPaths = BuildReservationPaths(snapshotConfiguration);
        foreach (var entry in previousState.Entries)
        {
            var relativePath = Normalize(entry.RelativePath);
            if (!currentPaths.Contains(relativePath) && snapshotPaths.Contains(relativePath))
            {
                logger.LogInformation(
                    "[Live] Removing {RelativePath} would leave a snapshot path empty; full rebuild required",
                    relativePath);
                return false;
            }
        }

        return true;
    }

    private void RemoveForeignTargetsForCopies(
        string workspacePath,
        IReadOnlyList<LivePlanEntry> entries,
        IReadOnlyDictionary<string, string> previousCopySources,
        bool replaceSnapshotTargets)
    {
        foreach (var entry in entries)
        {
            if (!entry.IsCopy)
            {
                continue;
            }

            if (!replaceSnapshotTargets &&
                previousCopySources.TryGetValue(entry.RelativePath, out var previousSource) &&
                string.Equals(
                    Path.GetFullPath(previousSource),
                    Path.GetFullPath(entry.SourcePath),
                    StringComparison.OrdinalIgnoreCase))
            {
                // The same live source still owns this copy; keep a possible game write.
                continue;
            }

            var targetPath = Path.Combine(workspacePath, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var existing = new FileInfo(targetPath);
            if (existing.LinkTarget != null)
            {
                try
                {
                    File.Delete(targetPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "[Live] Could not replace snapshot link {RelativePath}", entry.RelativePath);
                }

                continue;
            }

            if (!File.Exists(targetPath))
            {
                continue;
            }

            // The snapshot strategy materialized a conflicting path first. Live content
            // won conflict resolution, so the snapshot entry must not survive the copy.
            try
            {
                File.SetAttributes(targetPath, FileAttributes.Normal);
                File.Delete(targetPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "[Live] Could not replace snapshot entry {RelativePath}", entry.RelativePath);
            }
        }
    }

    private void RemoveStaleStateEntries(
        string workspacePath,
        LiveWorkspaceState previousState,
        IReadOnlyDictionary<string, LivePlanEntry> plan)
    {
        foreach (var entry in previousState.Entries)
        {
            var relativePath = Normalize(entry.RelativePath);
            if (plan.ContainsKey(relativePath))
            {
                continue;
            }

            var targetPath = Path.Combine(workspacePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var existing = new FileInfo(targetPath);

            // Entries recorded as links are deleted only when the entry really is a link.
            // A regular file at the same path may be game-written data and is left alone.
            if (existing.LinkTarget != null)
            {
                try
                {
                    File.Delete(targetPath);
                    logger.LogDebug("[Live] Removed stale live link {RelativePath}", relativePath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "[Live] Could not remove stale live link {RelativePath}", relativePath);
                }

                continue;
            }

            if (!entry.IsCopy || !File.Exists(targetPath))
            {
                continue;
            }

            try
            {
                File.SetAttributes(targetPath, FileAttributes.Normal);
                File.Delete(targetPath);
                logger.LogDebug("[Live] Removed stale live copy {RelativePath}", relativePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "[Live] Could not remove stale live copy {RelativePath}", relativePath);
            }
        }
    }

    private async Task LinkEntriesAsync(
        string workspacePath,
        IReadOnlyList<LivePlanEntry> entries,
        IProgress<WorkspacePreparationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var failures = new ConcurrentBag<string>();
        var processed = 0;
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(
            entries,
            parallelOptions,
            (entry, _) =>
            {
                var result = entry.IsCopy
                    ? linker.EnsureCopy(workspacePath, entry.RelativePath, entry.SourcePath)
                    : linker.EnsureLink(workspacePath, entry.RelativePath, entry.SourcePath);
                if (!result.Success)
                {
                    failures.Add($"'{entry.RelativePath}': {result.FirstError}");
                }

                var current = Interlocked.Increment(ref processed);
                progress?.Report(new WorkspacePreparationProgress
                {
                    FilesProcessed = current,
                    TotalFiles = entries.Count,
                    CurrentOperation = "Linking live content",
                    CurrentFile = entry.RelativePath,
                });

                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);

        if (!failures.IsEmpty)
        {
            throw new InvalidOperationException(
                GetLiveError(
                    "Workspace.Live.Error.Mount",
                    "Failed to mount live content: {0}",
                    string.Join("; ", failures.Take(5))));
        }
    }

    private void RemoveStaleLinks(
        string workspacePath,
        IReadOnlySet<string> expectedPaths,
        IReadOnlyList<string> liveRoots)
    {
        if (liveRoots.Count == 0)
        {
            return;
        }

        foreach (var file in EnumerateWorkspaceFilesNoFollow(workspacePath))
        {
            var relativePath = Normalize(Path.GetRelativePath(workspacePath, file));
            if (expectedPaths.Contains(relativePath))
            {
                continue;
            }

            if (linker.TryDeleteLiveLink(file, liveRoots))
            {
                logger.LogDebug("[Live] Removed stale live link {RelativePath}", relativePath);
            }
        }
    }

    private sealed record LivePlanEntry(string RelativePath, string SourcePath, int Priority, int Index, bool IsCopy)
    {
        public bool Beats(LivePlanEntry other) =>
            Priority > other.Priority || (Priority == other.Priority && Index > other.Index);
    }

    private sealed record LiveRootBinding(string ManifestId, string Root, int Priority, int Index);

    private string GetLiveError(string key, string fallback, params object?[] arguments)
    {
        if (localizationService == null)
        {
            return string.Format(CultureInfo.InvariantCulture, fallback, arguments);
        }

        var message = localizationService.GetString(key, arguments);
        return string.IsNullOrWhiteSpace(message) || string.Equals(message, key, StringComparison.Ordinal)
            ? string.Format(CultureInfo.InvariantCulture, fallback, arguments)
            : message;
    }
}
