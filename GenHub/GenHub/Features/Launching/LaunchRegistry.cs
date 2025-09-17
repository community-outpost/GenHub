using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Models.GameProfile;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Launching;

/// <summary>
/// In-memory implementation of the launch registry.
/// </summary>
public class LaunchRegistry(ILogger<LaunchRegistry> logger) : ILaunchRegistry
{
    private readonly ConcurrentDictionary<string, GameLaunchInfo> _activeLaunches = new();
    private readonly ILogger<LaunchRegistry> _logger = logger;

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
            try
            {
                using var process = Process.GetProcessById(launchInfo.ProcessInfo.ProcessId);
                if (process.HasExited)
                {
                    launchInfo.TerminatedAt = process.ExitTime;
                }
            }
            catch (ArgumentException)
            {
                // Process doesn't exist anymore
                launchInfo.TerminatedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check process status for launch {LaunchId}", launchId);
            }
        }

        return Task.FromResult(launchInfo);
    }

    /// <inheritdoc/>
    public Task<IEnumerable<GameLaunchInfo>> GetAllActiveLaunchesAsync()
    {
        // Clean up stale launches before returning
        CleanupStaleLaunches();
        return Task.FromResult(_activeLaunches.Values.AsEnumerable());
    }

    /// <summary>
    /// Cleans up launches for processes that have exited.
    /// </summary>
    private void CleanupStaleLaunches()
    {
        var staleLaunchIds = new List<string>();

        foreach (var kvp in _activeLaunches)
        {
            var launchInfo = kvp.Value;
            if (launchInfo.TerminatedAt.HasValue)
            {
                continue; // Already marked as terminated
            }

            try
            {
                using var process = Process.GetProcessById(launchInfo.ProcessInfo.ProcessId);
                if (process.HasExited)
                {
                    launchInfo.TerminatedAt = process.ExitTime;
                    staleLaunchIds.Add(kvp.Key);
                }
            }
            catch (ArgumentException)
            {
                // Process doesn't exist anymore
                launchInfo.TerminatedAt = DateTime.UtcNow;
                staleLaunchIds.Add(kvp.Key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check process status for launch {LaunchId}", kvp.Key);
            }
        }

        // Note: We don't remove from _activeLaunches here because the terminated launches
        // should remain in the registry for historical purposes, but with TerminatedAt set.
        // The GetAllActiveLaunchesAsync should filter them out if needed.
    }
}
