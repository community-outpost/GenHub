using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Launching;

/// <summary>
/// Service for launching games from prepared workspaces.
/// </summary>
public class GameLauncher(ILogger<GameLauncher> logger) : IGameLauncher
{
    private readonly ILogger<GameLauncher> _logger = logger;

    /// <inheritdoc/>
    public async Task<LaunchResult> LaunchGameAsync(GameLaunchConfiguration config, System.Threading.CancellationToken cancellationToken = default)
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
                        Arguments = config.Arguments,
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
            _logger.LogError(ex, "Failed to launch game");
            return LaunchResult.CreateFailure(ex.Message, ex);
        }
    }

    /// <inheritdoc/>
    public async Task<GameProcessInfo?> GetGameProcessInfoAsync(int processId, System.Threading.CancellationToken cancellationToken = default)
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
            _logger.LogError(ex, "Failed to get game process info for process ID {ProcessId}", processId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TerminateGameAsync(int processId, System.Threading.CancellationToken cancellationToken = default)
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
            _logger.LogError(ex, "Failed to terminate game process with ID {ProcessId}", processId);
            return false;
        }
    }
}
