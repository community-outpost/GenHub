using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Launching;

/// <summary>
/// Service for launching games from prepared workspaces.
/// </summary>
public class GameLauncher(
    ILogger<GameLauncher> logger) : IGameLauncher
{
    /// <summary>
    /// Launches a game using the provided configuration.
    /// </summary>
    /// <param name="config">The game launch configuration.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="LaunchResult"/> representing the result of the launch operation.</returns>
    public async Task<LaunchResult> LaunchGameAsync(GameLaunchConfiguration config, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(config.ExecutablePath))
            return LaunchResult.CreateFailure("Executable path cannot be null or empty", null);
        try
        {
            return await Task.Run(
                () =>
                {
                    var startTime = DateTime.UtcNow;
                    using var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = config.ExecutablePath,
                            WorkingDirectory = config.WorkingDirectory,
                            Arguments = config.Arguments != null ? string.Join(" ", config.Arguments.Select(kvp => $"{kvp.Key} {kvp.Value}")) : string.Empty,
                            UseShellExecute = false,
                        },
                    };
                    if (!process.Start())
                        return LaunchResult.CreateFailure("Failed to start process", null);
                    var launchTime = DateTime.UtcNow - startTime;
                    return LaunchResult.CreateSuccess(process.Id, process.StartTime, launchTime);
                }, cancellationToken);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Failed to launch game");
            return LaunchResult.CreateFailure(ex.Message, ex);
        }
    }

    /// <summary>
    /// Gets information about a game process by its process ID.
    /// </summary>
    /// <param name="processId">The process ID of the game process.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="GameProcessInfo"/> containing the process information, or null if the process is not found.</returns>
    public async Task<GameProcessInfo?> GetGameProcessInfoAsync(int processId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.Run(
                () =>
                {
                    using var process = Process.GetProcessById(processId);

                    // Note: StartInfo properties are often not available for external processes
                    var workingDirectory = string.Empty;
                    var commandLine = string.Empty;

                    try
                    {
                        workingDirectory = process.StartInfo.WorkingDirectory ?? string.Empty;
                        commandLine = process.StartInfo.Arguments ?? string.Empty;
                    }
                    catch
                    {
                        // StartInfo properties may not be accessible for external processes
                    }

                    return new GameProcessInfo
                    {
                        ProcessId = process.Id,
                        ProcessName = process.ProcessName,
                        StartTime = process.StartTime,
                        WorkingDirectory = workingDirectory,
                        CommandLine = commandLine,
                        IsResponding = process.Responding,
                    };
                }, cancellationToken);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Failed to get game process info for process ID {ProcessId}", processId);
            return null;
        }
    }

    /// <summary>
    /// Terminates a game process by its process ID.
    /// </summary>
    /// <param name="processId">The process ID of the game process to terminate.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>True if the process was terminated successfully, false otherwise.</returns>
    public async Task<bool> TerminateGameAsync(int processId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var process = Process.GetProcessById(processId);

            // Try graceful termination first
            if (!process.CloseMainWindow())
            {
                // If graceful close fails, wait a bit then force kill
                await Task.Delay(2000, cancellationToken);
                if (!process.HasExited)
                    process.Kill();
            }

            return true;
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Failed to terminate game process with ID {ProcessId}", processId);
            return false;
        }
    }

    /// <summary>
    /// Launches a game profile by its ID.
    /// </summary>
    /// <param name="profileId">The ID of the game profile to launch.</param>
    /// <param name="progress">Optional progress reporter for launch progress.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="LaunchOperationResult{GameLaunchInfo}"/> representing the result of the launch operation.</returns>
    public Task<LaunchOperationResult<GameLaunchInfo>> LaunchProfileAsync(string profileId, IProgress<LaunchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Launches a game using the provided game profile object.
    /// </summary>
    /// <param name="profile">The game profile to launch.</param>
    /// <param name="progress">Optional progress reporter for launch progress.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="LaunchOperationResult{GameLaunchInfo}"/> representing the result of the launch operation.</returns>
    public Task<LaunchOperationResult<GameLaunchInfo>> LaunchProfileAsync(GameProfile profile, IProgress<LaunchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Gets a list of all active game processes managed by the launcher.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="LaunchOperationResult{T}"/> containing the list of active game processes.</returns>
    public Task<LaunchOperationResult<IReadOnlyList<GameProcessInfo>>> GetActiveGamesAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Gets information about a specific game process by its launch ID.
    /// </summary>
    /// <param name="launchId">The launch ID of the game process.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="LaunchOperationResult{GameProcessInfo}"/> containing the process information.</returns>
    public Task<LaunchOperationResult<GameProcessInfo>> GetGameProcessInfoAsync(string launchId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Terminates a running game instance by its launch ID.
    /// </summary>
    /// <param name="launchId">The launch ID of the running game instance.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="LaunchOperationResult{GameLaunchInfo}"/> representing the result of the termination operation.</returns>
    public Task<LaunchOperationResult<GameLaunchInfo>> TerminateGameAsync(string launchId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
