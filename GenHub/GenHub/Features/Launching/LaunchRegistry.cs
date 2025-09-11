using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        return Task.FromResult(launchInfo);
    }

    /// <inheritdoc/>
    public Task<IEnumerable<GameLaunchInfo>> GetAllActiveLaunchesAsync()
    {
        return Task.FromResult(_activeLaunches.Values.AsEnumerable());
    }
}
