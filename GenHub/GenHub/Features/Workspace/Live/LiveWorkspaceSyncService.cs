using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Workspace;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace GenHub.Features.Workspace.Live;

/// <summary>
/// Watches live content roots and triggers a debounced full resync. The rescan is the
/// source of truth; watcher events are only an accelerator, so missing or coalesced
/// events cannot leave the workspace stale.
/// </summary>
public sealed class LiveWorkspaceSyncService(
    LiveWorkspaceMaterializer materializer,
    ILogger<LiveWorkspaceSyncService> logger,
    ILocalizationService? localizationService = null) : ILiveWorkspaceSyncService
{
    private readonly ConcurrentDictionary<string, LiveSession> _sessions = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public OperationResult Attach(WorkspaceInfo workspace, WorkspaceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(configuration);

        // Wait for the old worker before replacing its session so two applies cannot
        // mutate the same workspace at once.
        DetachAsync(workspace.Id).GetAwaiter().GetResult();

        var liveManifests = configuration.Manifests
            .Where(manifest => manifest.SourceMode == ContentSourceMode.Live)
            .ToList();

        if (liveManifests.Count == 0)
        {
            return OperationResult.CreateSuccess();
        }

        var roots = new List<string>();
        foreach (var manifest in liveManifests)
        {
            var root = ResolveLiveRoot(configuration, manifest);
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return OperationResult.CreateFailure(
                    GetLiveError(
                        "Workspace.Live.Error.MissingSource",
                        "The live content folder for '{0}' is missing: {1}",
                        manifest.Name,
                        root ?? manifest.SourcePath));
            }

            roots.Add(Path.GetFullPath(root!));
        }

        var snapshotConfiguration = configuration.WithManifests(
            configuration.Manifests.Where(manifest => manifest.SourceMode != ContentSourceMode.Live));

        var session = new LiveSession(
            workspace,
            configuration,
            snapshotConfiguration,
            new CancellationTokenSource());

        try
        {
            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var watcher = CreateWatcher(root, session.SignalWriter);
                session.Watchers.Add(watcher);
            }

            foreach (var watcher in session.Watchers)
            {
                watcher.EnableRaisingEvents = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            session.Dispose();
            logger.LogError(ex, "[Live] Failed to watch a live content folder for workspace {WorkspaceId}", workspace.Id);
            return OperationResult.CreateFailure(
                GetLiveError(
                    "Workspace.Live.Error.Watch",
                    "Failed to watch live content: {0}",
                    ex.Message));
        }

        _sessions[workspace.Id] = session;
        session.Worker = Task.Run(() => RunAsync(session), CancellationToken.None);

        logger.LogInformation(
            "[Live] Watching {RootCount} live folder(s) for workspace {WorkspaceId}",
            roots.Count,
            workspace.Id);

        return OperationResult.CreateSuccess();
    }

    /// <inheritdoc/>
    public async Task DetachAsync(string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return;
        }

        if (!_sessions.TryRemove(workspaceId, out var session))
        {
            return;
        }

        session.Cancellation.Cancel();
        if (session.Worker != null)
        {
            try
            {
                await session.Worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }

        session.Dispose();
        logger.LogInformation("[Live] Stopped watching workspace {WorkspaceId}", workspaceId);
    }

    /// <inheritdoc/>
    public bool IsAttached(string workspaceId) =>
        !string.IsNullOrWhiteSpace(workspaceId) && _sessions.ContainsKey(workspaceId);

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> SyncNowAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceId) ||
            !_sessions.TryGetValue(workspaceId, out var session))
        {
            return OperationResult<bool>.CreateFailure(
                $"Workspace '{workspaceId}' has no live watcher session.");
        }

        try
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                session.Cancellation.Token);

            await session.ApplyLock.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
            try
            {
                var result = await materializer.ResyncAsync(
                    session.Workspace,
                    session.Configuration,
                    session.SnapshotConfiguration,
                    linkedCancellation.Token).ConfigureAwait(false);

                return result.Success
                    ? OperationResult<bool>.CreateSuccess(true)
                    : OperationResult<bool>.CreateFailure(
                        result.FirstError ?? GetLiveError(
                            "Workspace.Live.Error.Resync",
                            "Live content resync failed: {0}",
                            "unknown"));
            }
            finally
            {
                session.ApplyLock.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || session.Cancellation.IsCancellationRequested)
        {
            return OperationResult<bool>.CreateFailure("Live content resync was cancelled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(ex, "[Live] Resync failed for workspace {WorkspaceId}", workspaceId);
            return OperationResult<bool>.CreateFailure(
                GetLiveError(
                    "Workspace.Live.Error.Resync",
                    "Live content resync failed: {0}",
                    ex.Message));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var workspaceId in _sessions.Keys.ToList())
        {
            DetachAsync(workspaceId).GetAwaiter().GetResult();
        }
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

    private async Task RunAsync(LiveSession session)
    {
        var cancellationToken = session.Cancellation.Token;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await session.SignalReader.ReadAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(LiveContentConstants.DebounceMilliseconds, cancellationToken).ConfigureAwait(false);

                while (session.SignalReader.TryRead(out _))
                {
                    // Drain coalesced events; one rescan covers them all.
                }

                try
                {
                    await session.ApplyLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        var result = await materializer.ResyncAsync(
                            session.Workspace,
                            session.Configuration,
                            session.SnapshotConfiguration,
                            cancellationToken).ConfigureAwait(false);

                        if (!result.Success)
                        {
                            logger.LogWarning(
                                "[Live] Resync for workspace {WorkspaceId} failed: {Error}",
                                session.Workspace.Id,
                                result.FirstError);
                        }
                    }
                    finally
                    {
                        session.ApplyLock.Release();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    logger.LogError(ex, "[Live] Resync for workspace {WorkspaceId} failed", session.Workspace.Id);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (ObjectDisposedException)
        {
            // The session was detached while the worker was waiting.
        }
    }

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

    private FileSystemWatcher CreateWatcher(string root, ChannelWriter<byte> signal)
    {
        var watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName |
                           NotifyFilters.DirectoryName |
                           NotifyFilters.LastWrite |
                           NotifyFilters.Size,
            InternalBufferSize = LiveContentConstants.WatcherBufferSize,
            EnableRaisingEvents = false,
        };

        watcher.Created += (_, _) => signal.TryWrite(0);
        watcher.Changed += (_, _) => signal.TryWrite(0);
        watcher.Deleted += (_, _) => signal.TryWrite(0);
        watcher.Renamed += (_, _) => signal.TryWrite(0);
        watcher.Error += (_, args) =>
        {
            logger.LogWarning(args.GetException(), "[Live] Watcher error for {Root}; forcing a rescan", root);
            signal.TryWrite(0);
        };

        return watcher;
    }

    private sealed class LiveSession(
        WorkspaceInfo workspace,
        WorkspaceConfiguration configuration,
        WorkspaceConfiguration snapshotConfiguration,
        CancellationTokenSource cancellation) : IDisposable
    {
        private readonly Channel<byte> _signal = Channel.CreateBounded<byte>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false,
            });

        public WorkspaceInfo Workspace { get; } = workspace;

        public WorkspaceConfiguration Configuration { get; } = configuration;

        public WorkspaceConfiguration SnapshotConfiguration { get; } = snapshotConfiguration;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public SemaphoreSlim ApplyLock { get; } = new(1, 1);

        public ChannelReader<byte> SignalReader => _signal.Reader;

        public ChannelWriter<byte> SignalWriter => _signal.Writer;

        public List<FileSystemWatcher> Watchers { get; } = [];

        public Task? Worker { get; set; }

        public void Dispose()
        {
            try
            {
                Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed.
            }

            foreach (var watcher in Watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }

            Watchers.Clear();
            Cancellation.Dispose();
        }
    }
}
