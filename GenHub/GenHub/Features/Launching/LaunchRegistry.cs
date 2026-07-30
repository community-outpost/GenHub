using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.GameProfile;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Launching;

/// <summary>
/// In-memory implementation of the launch registry.
/// Automatically cleans up workspaces when game processes exit.
/// </summary>
public class LaunchRegistry : ILaunchRegistry
{
    /// <summary>
    /// How long an exit event that matched no launch is kept for a late registration.
    /// </summary>
    /// <remarks>
    /// Generous against the actual gap, which is the time between a start operation
    /// returning and the launcher updating the registry — milliseconds. Bounded because
    /// PIDs are recycled: an event held indefinitely could be applied to an unrelated
    /// later launch that happened to receive the same PID.
    /// </remarks>
    private static readonly TimeSpan PendingExitRetention = TimeSpan.FromSeconds(30);

    private readonly ILogger<LaunchRegistry> _logger;
    private readonly IWorkspaceManager? _workspaceManager;
    private readonly IGameProcessManager? _processManager;
    private readonly ConcurrentDictionary<string, GameLaunchInfo> _activeLaunches = new();

    /// <summary>
    /// Exit events whose PID matched no registered launch when they arrived, keyed by PID.
    /// </summary>
    /// <remarks>
    /// The launcher registers a placeholder entry (PID -1) before spawning and records
    /// the real PID only after the start operation returns. A process that dies inside
    /// that gap raises its exit event while the registry still cannot match it, so the
    /// event — including the late-failure evidence — would be silently lost. It is kept
    /// here briefly instead and applied when a launch is registered with that PID.
    /// </remarks>
    private readonly ConcurrentDictionary<int, Core.Models.Events.GameProcessExitedEventArgs> _pendingExits = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LaunchRegistry"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="workspaceManager">Optional workspace manager for cleanup.</param>
    /// <param name="processManager">Optional process manager for tracking game processes.</param>
    public LaunchRegistry(
        ILogger<LaunchRegistry> logger,
        IWorkspaceManager? workspaceManager = null,
        IGameProcessManager? processManager = null)
    {
        _logger = logger;
        _workspaceManager = workspaceManager;
        _processManager = processManager;

        if (_processManager != null)
        {
            _processManager.ProcessExited += OnProcessExited;
        }
    }

    /// <summary>
    /// Registers a new game launch in the registry.
    /// </summary>
    /// <param name="launchInfo">The launch information to register.</param>
    /// <returns>A completed task.</returns>
    public Task RegisterLaunchAsync(GameLaunchInfo launchInfo)
    {
        ArgumentNullException.ThrowIfNull(launchInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(launchInfo.LaunchId);

        _activeLaunches[launchInfo.LaunchId] = launchInfo;
        _logger.LogInformation("Registered launch {LaunchId} for profile {ProfileId}", launchInfo.LaunchId, launchInfo.ProfileId);

        // The process may have exited before this registration carried its real PID —
        // the placeholder-PID gap. Apply the buffered exit now so the failure is
        // recorded rather than lost to the race.
        var processId = launchInfo.ProcessInfo.ProcessId;
        if (processId > 0
            && _pendingExits.TryRemove(processId, out var pendingExit)
            && DateTime.UtcNow - pendingExit.ExitTime <= PendingExitRetention)
        {
            _logger.LogInformation(
                "[LaunchRegistry] Applying buffered exit event for PID {ProcessId} to newly registered launch {LaunchId}",
                processId,
                launchInfo.LaunchId);
            ApplyProcessExit(launchInfo, pendingExit);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Unregisters a game launch from the registry.
    /// </summary>
    /// <param name="launchId">The launch ID to unregister.</param>
    /// <returns>A completed task.</returns>
    public Task UnregisterLaunchAsync(string launchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launchId);

        if (_activeLaunches.TryRemove(launchId, out var launchInfo))
        {
            launchInfo.TerminatedAt = System.DateTime.UtcNow;
            _logger.LogInformation("Unregistered launch {LaunchId} for profile {ProfileId}", launchId, launchInfo.ProfileId);
        }
        else
        {
            _logger.LogWarning("Attempted to unregister non-existent launch {LaunchId}", launchId);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<GameLaunchInfo?> GetLaunchInfoAsync(string launchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launchId);

        _activeLaunches.TryGetValue(launchId, out var launchInfo);

        // Check if this launch is stale
        if (launchInfo != null && !launchInfo.TerminatedAt.HasValue)
        {
            TryUpdateProcessStatus(launchInfo, launchId);
        }

        return Task.FromResult(launchInfo);
    }

    /// <inheritdoc/>
    public Task<IEnumerable<GameLaunchInfo>> GetAllActiveLaunchesAsync()
    {
        // Clean up stale launches before returning
        CleanupStaleLaunches();

        // Only return launches that haven't been terminated
        // This prevents race conditions where a launch is being terminated but still in the registry
        return Task.FromResult(_activeLaunches.Values.Where(l => !l.TerminatedAt.HasValue).AsEnumerable());
    }

    /// <summary>
    /// Handles the ProcessExited event from the game process manager.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments containing process exit information.</param>
    private void OnProcessExited(object? sender, Core.Models.Events.GameProcessExitedEventArgs e)
    {
        _logger.LogInformation("[LaunchRegistry] Received process exit event for PID {ProcessId}", e.ProcessId);

        // Find launch info by process ID
        var launch = _activeLaunches.Values.FirstOrDefault(l => l.ProcessInfo.ProcessId == e.ProcessId);
        if (launch != null)
        {
            ApplyProcessExit(launch, e);
            return;
        }

        // No launch knows this PID. Registration with the real PID may still be in
        // flight — the launcher only updates the placeholder entry after the start
        // operation returns — so keep the event briefly instead of dropping it.
        if (e.ProcessId > 0)
        {
            PruneExpiredPendingExits();
            _pendingExits[e.ProcessId] = e;
            _logger.LogDebug(
                "[LaunchRegistry] No launch matches PID {ProcessId} yet; buffering the exit event in case a registration is in flight",
                e.ProcessId);
        }
    }

    /// <summary>
    /// Applies an exit event to a launch: termination state, exit code, and — for a
    /// non-zero exit — the retroactive failure record.
    /// </summary>
    /// <remarks>
    /// Idempotent. The placeholder-PID race means the same exit can be seen twice — once
    /// buffered and applied at registration, once delivered against the registered PID —
    /// and the second application must neither duplicate nor contradict the first. An
    /// exit code already recorded means the event was applied; a termination already
    /// stamped by the polling path is only kept when the event carries nothing more.
    /// </remarks>
    /// <param name="launch">The launch the process belonged to.</param>
    /// <param name="e">The exit event.</param>
    private void ApplyProcessExit(GameLaunchInfo launch, Core.Models.Events.GameProcessExitedEventArgs e)
    {
        if (launch.ExitCode.HasValue || (launch.TerminatedAt.HasValue && e.ExitCode is null))
        {
            return;
        }

        _logger.LogInformation("[LaunchRegistry] Updating launch {LaunchId} as terminated", launch.LaunchId);

        // e.ExitTime might be non-nullable DateTime
        launch.TerminatedAt = e.ExitTime != default ? e.ExitTime : DateTime.UtcNow;
        launch.ProcessInfo.IsRunning = false;
        launch.ExitCode = e.ExitCode;

        // The late-failure channel. An initialisation abort slow enough to outlive the
        // post-spawn detection window was reported as a started launch; its non-zero
        // exit arriving here is the first evidence to the contrary, so the failure is
        // recorded retroactively. A clean exit is the user quitting and is never marked
        // as failed.
        var failureReason = e.DescribeFailure();
        if (failureReason != null)
        {
            launch.FailureReason = failureReason;

            _logger.LogWarning(
                "[LaunchRegistry] Launch {LaunchId} (PID {ProcessId}) failed after it was reported as started: exit code {ExitCode}. {Reason}",
                launch.LaunchId,
                e.ProcessId,
                e.ExitCode,
                failureReason);
        }
    }

    /// <summary>
    /// Drops buffered exit events old enough that applying them would risk matching a
    /// recycled PID rather than the process that produced them.
    /// </summary>
    private void PruneExpiredPendingExits()
    {
        var cutoff = DateTime.UtcNow - PendingExitRetention;
        foreach (var kvp in _pendingExits)
        {
            if (kvp.Value.ExitTime < cutoff)
            {
                _pendingExits.TryRemove(kvp.Key, out _);
            }
        }
    }

    /// <summary>
    /// Attempts to update the process status for a launch.
    /// </summary>
    /// <param name="launchInfo">The launch information to update.</param>
    /// <param name="launchId">The launch ID for logging purposes.</param>
    private void TryUpdateProcessStatus(GameLaunchInfo launchInfo, string launchId)
    {
        try
        {
            // GetProcesses() can throw UnauthorizedAccessException on some systems
            var runningProcess = Process.GetProcesses()
                .FirstOrDefault(p => p.Id == launchInfo.ProcessInfo.ProcessId);

            if (runningProcess == null)
            {
                _logger.LogDebug("Process {ProcessId} for launch {LaunchId} no longer exists", launchInfo.ProcessInfo.ProcessId, launchId);
                launchInfo.TerminatedAt = DateTime.UtcNow;
                launchInfo.ProcessInfo.IsRunning = false;

                // NOTE: Workspace is NOT cleaned up automatically - it persists across launches
                // Only clean up workspace when profile is deleted or content changes
                return;
            }

            using (runningProcess)
            {
                if (runningProcess.HasExited)
                {
                    try
                    {
                        launchInfo.TerminatedAt = runningProcess.ExitTime;
                    }
                    catch (InvalidOperationException)
                    {
                        launchInfo.TerminatedAt = DateTime.UtcNow;
                    }

                    launchInfo.ProcessInfo.IsRunning = false;

                    // NOTE: Workspace is NOT cleaned up automatically - it persists across launches
                }
            }
        }
        catch (UnauthorizedAccessException uaex)
        {
            _logger.LogWarning(uaex, "Access denied checking process status for launch {LaunchId}", launchId);
            launchInfo.TerminatedAt = DateTime.UtcNow;
            launchInfo.ProcessInfo.IsRunning = false;

            // NOTE: Workspace is NOT cleaned up on error - it persists
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check process status for launch {LaunchId}", launchId);
            launchInfo.TerminatedAt = DateTime.UtcNow;
            launchInfo.ProcessInfo.IsRunning = false;

            // NOTE: Workspace is NOT cleaned up on error - it persists
        }
    }

    /// <summary>
    /// Cleans up the workspace for a terminated launch.
    /// </summary>
    /// <param name="launchInfo">The launch information.</param>
    /// <param name="launchId">The launch ID.</param>
    private async Task CleanupWorkspaceForLaunchAsync(GameLaunchInfo launchInfo, string launchId)
    {
        if (_workspaceManager == null || string.IsNullOrEmpty(launchInfo.WorkspaceId))
        {
            return;
        }

        try
        {
            _logger.LogInformation(
                "Automatically cleaning up workspace {WorkspaceId} for terminated launch {LaunchId} (Profile: {ProfileId})",
                launchInfo.WorkspaceId,
                launchId,
                launchInfo.ProfileId);

            var cleanupResult = await _workspaceManager.CleanupWorkspaceAsync(launchInfo.WorkspaceId);
            if (cleanupResult.Failed)
            {
                _logger.LogWarning(
                    "Failed to cleanup workspace {WorkspaceId} for launch {LaunchId}: {Error}",
                    launchInfo.WorkspaceId,
                    launchId,
                    cleanupResult.FirstError);
            }
            else
            {
                _logger.LogInformation(
                    "Successfully cleaned up workspace {WorkspaceId} for terminated launch {LaunchId}",
                    launchInfo.WorkspaceId,
                    launchId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Exception during automatic workspace cleanup for launch {LaunchId}, workspace {WorkspaceId}",
                launchId,
                launchInfo.WorkspaceId);
        }
    }

    /// <summary>
    /// Cleans up launches for processes that have exited.
    /// </summary>
    private void CleanupStaleLaunches()
    {
        foreach (var kvp in _activeLaunches)
        {
            var launchInfo = kvp.Value;
            if (launchInfo.TerminatedAt.HasValue)
            {
                continue; // Already marked as terminated
            }

            TryUpdateProcessStatus(launchInfo, kvp.Key);
        }

        // Note: We don't remove from _activeLaunches here because the terminated launches
        // should remain in the registry for historical purposes, but with TerminatedAt set.
        // The GetAllActiveLaunchesAsync should filter them out if needed.
    }
}