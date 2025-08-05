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
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = config.ExecutablePath,
                    WorkingDirectory = config.WorkingDirectory,
                    Arguments = config.Arguments,
                    UseShellExecute = false,
                },
            };
            process.Start();

            await Task.CompletedTask;
            return LaunchResult.CreateSuccess(process.Id, process.StartTime, TimeSpan.Zero);
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
        await Task.CompletedTask;
        try
        {
            var process = Process.GetProcessById(processId);
            return new GameProcessInfo
            {
                ProcessId = process.Id,
                ProcessName = process.ProcessName,
                StartTime = process.StartTime,
                WorkingDirectory = process.StartInfo.WorkingDirectory,
                CommandLine = process.StartInfo.Arguments,
                IsResponding = process.Responding,
            };
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Failed to get game process info");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TerminateGameAsync(int processId, System.Threading.CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        try
        {
            var process = Process.GetProcessById(processId);
            process.Kill();
            return true;
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Failed to terminate game process");
            return false;
        }
    }
}
