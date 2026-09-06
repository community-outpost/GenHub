using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Models.GameProfile;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Launching;

/// <summary>
/// In-memory implementation of the launch registry.
/// </summary>
public class LaunchRegistry : ILaunchRegistry
{
    private const int MaxInspectionFailures = 5;
    private readonly ILogger<LaunchRegistry> _logger;
    private readonly IGameProcessManager? _processManager;
    private readonly ConcurrentDictionary<string, GameLaunchInfo> _activeLaunches = new();
    private readonly ConcurrentDictionary<string, int> _inspectionFailureCounts = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LaunchRegistry"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="processManager">Optional process manager for tracking game processes.</param>
    public LaunchRegistry(
        ILogger<LaunchRegistry> logger,
        IGameProcessManager? processManager = null)
    {
        _logger = logger;
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
            _inspectionFailureCounts.TryRemove(launchId, out _);
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
            _inspectionFailureCounts.TryRemove(launch.LaunchId, out _);
            _logger.LogInformation("[LaunchRegistry] Updating launch {LaunchId} as terminated", launch.LaunchId);

            // e.ExitTime might be non-nullable DateTime
            launch.TerminatedAt = e.ExitTime != default ? e.ExitTime : DateTime.UtcNow;
            launch.ProcessInfo.IsRunning = false;
        }
    }

    /// <summary>
    /// Attempts to update the process status for a launch.
    /// </summary>
    /// <param name="launchInfo">The launch information to update.</param>
    /// <param name="launchId">The launch ID.</param>
    private void TryUpdateProcessStatus(GameLaunchInfo launchInfo, string launchId)
    {
        try
        {
            // Use ProcessInfo to check if process is still running
            var runningProcess = Process.GetProcesses()
                .FirstOrDefault(p => p.Id == launchInfo.ProcessInfo.ProcessId);

            if (runningProcess == null)
            {
                _logger.LogDebug("Process {ProcessId} for launch {LaunchId} no longer exists", launchInfo.ProcessInfo.ProcessId, launchId);
                _inspectionFailureCounts.TryRemove(launchId, out _);
                launchInfo.TerminatedAt = DateTime.UtcNow;
                launchInfo.ProcessInfo.IsRunning = false;
            }
            else
            {
                // Verify process hasn't exited (Handle might throw if process has exited)
                if (runningProcess.HasExited)
                {
                    _logger.LogDebug("Process {ProcessId} for launch {LaunchId} has exited", launchInfo.ProcessInfo.ProcessId, launchId);

                    try
                    {
                        launchInfo.TerminatedAt = runningProcess.ExitTime;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                    {
                        // Fallback to current time if ExitTime throws
                        launchInfo.TerminatedAt = DateTime.UtcNow;
                    }

                    _inspectionFailureCounts.TryRemove(launchId, out _);
                    launchInfo.ProcessInfo.IsRunning = false;
                }
                else
                {
                    // Process is actively running and inspected successfully
                    _inspectionFailureCounts.TryRemove(launchId, out _);
                }
            }
        }
        catch (Exception ex)
        {
            var failures = _inspectionFailureCounts.AddOrUpdate(launchId, 1, (_, count) => count + 1);
            if (failures >= MaxInspectionFailures)
            {
                _logger.LogWarning(ex, "[LaunchRegistry] Process inspection failed {Failures} consecutive times for launch {LaunchId}. Marking as terminated.", failures, launchId);
                launchInfo.TerminatedAt = DateTime.UtcNow;
                launchInfo.ProcessInfo.IsRunning = false;
                _inspectionFailureCounts.TryRemove(new KeyValuePair<string, int>(launchId, failures));
            }
            else
            {
                // Do not mark process terminated on transient inspection error; preserve it as active so safe teardown guards hold
                _logger.LogWarning(ex, "Failed to check process status for launch {LaunchId} (attempt {Failures}/{MaxFailures})", launchId, failures, MaxInspectionFailures);
            }
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
