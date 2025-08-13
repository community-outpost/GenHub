using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.Infrastructure
{
    /// <summary>
    /// Manages game processes and their lifecycle.
    /// </summary>
    public class GameProcessManager : IGameProcessManager
    {
        private readonly IConfigurationProviderService _configProvider;
        private readonly ILogger<GameProcessManager> _logger;
        private readonly Dictionary<int, Process> _managedProcesses = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="GameProcessManager"/> class.
        /// </summary>
        /// <param name="configProvider">The configuration provider service.</param>
        /// <param name="logger">The logger.</param>
        public GameProcessManager(IConfigurationProviderService configProvider, ILogger<GameProcessManager> logger)
        {
            _configProvider = configProvider;
            _logger = logger;
        }

        /// <inheritdoc/>
        public Task<ProcessOperationResult<GameProcessInfo>> StartProcessAsync(GameLaunchConfiguration configuration, CancellationToken cancellationToken = default)
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = configuration.ExecutablePath,
                    WorkingDirectory = configuration.WorkingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                };

                // Add arguments
                if (configuration.Arguments != null)
                {
                    foreach (var arg in configuration.Arguments)
                    {
                        processStartInfo.ArgumentList.Add($"{arg.Key}={arg.Value}");
                    }
                }

                // Add environment variables
                foreach (var envVar in configuration.EnvironmentVariables)
                {
                    processStartInfo.Environment[envVar.Key] = envVar.Value;
                }

                var process = Process.Start(processStartInfo);
                if (process == null)
                {
                    return Task.FromResult(ProcessOperationResult<GameProcessInfo>.CreateFailure("Failed to start process"));
                }

                // Track the process
                _managedProcesses[process.Id] = process;

                var processInfo = new GameProcessInfo
                {
                    ProcessId = process.Id,
                    ProcessName = process.ProcessName,
                    StartTime = process.StartTime,
                    ExecutablePath = configuration.ExecutablePath,
                };

                _logger.LogInformation("Started game process {ProcessId} for executable {ExecutablePath}", process.Id, configuration.ExecutablePath);
                return Task.FromResult(ProcessOperationResult<GameProcessInfo>.CreateSuccess(processInfo));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start process for executable {ExecutablePath}", configuration.ExecutablePath);
                return Task.FromResult(ProcessOperationResult<GameProcessInfo>.CreateFailure($"Failed to start process: {ex.Message}"));
            }
        }

        /// <inheritdoc/>
        public Task<ProcessOperationResult<bool>> TerminateProcessAsync(int processId, CancellationToken cancellationToken = default)
        {
            try
            {
                Process? process = null;

                // Try to get from managed processes first
                if (_managedProcesses.TryGetValue(processId, out process))
                {
                    _managedProcesses.Remove(processId);
                }
                else
                {
                    // Try to get from system processes
                    try
                    {
                        process = Process.GetProcessById(processId);
                    }
                    catch (ArgumentException)
                    {
                        return Task.FromResult(ProcessOperationResult<bool>.CreateFailure("Process not found"));
                    }
                }

                if (process == null)
                {
                    return Task.FromResult(ProcessOperationResult<bool>.CreateFailure("Process not found"));
                }

                // Try graceful termination first
                if (!process.CloseMainWindow())
                {
                    // Force termination if graceful fails
                    process.Kill();
                }

                // Wait for exit with timeout
                var timeout = TimeSpan.FromSeconds(10);
                if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                {
                    process.Kill();
                    process.WaitForExit();
                }

                _logger.LogInformation("Terminated process {ProcessId}", processId);
                return Task.FromResult(ProcessOperationResult<bool>.CreateSuccess(true));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to terminate process {ProcessId}", processId);
                return Task.FromResult(ProcessOperationResult<bool>.CreateFailure($"Failed to terminate process: {ex.Message}"));
            }
        }

        /// <inheritdoc/>
        public Task<ProcessOperationResult<GameProcessInfo>> GetProcessInfoAsync(int processId, CancellationToken cancellationToken = default)
        {
            try
            {
                Process? process = null;

                if (_managedProcesses.TryGetValue(processId, out process))
                {
                    if (process.HasExited)
                    {
                        _managedProcesses.Remove(processId);
                        return Task.FromResult(ProcessOperationResult<GameProcessInfo>.CreateFailure("Process not found"));
                    }

                    var processInfo = new GameProcessInfo
                    {
                        ProcessId = process.Id,
                        ProcessName = process.ProcessName,
                        StartTime = process.StartTime,
                        ExecutablePath = process.MainModule?.FileName ?? string.Empty,
                    };

                    return Task.FromResult(ProcessOperationResult<GameProcessInfo>.CreateSuccess(processInfo));
                }

                // Try to get from system processes
                try
                {
                    process = Process.GetProcessById(processId);
                    if (process == null || process.HasExited)
                    {
                        return Task.FromResult(ProcessOperationResult<GameProcessInfo>.CreateFailure("Process not found"));
                    }

                    var processInfo = new GameProcessInfo
                    {
                        ProcessId = process.Id,
                        ProcessName = process.ProcessName,
                        StartTime = process.StartTime,
                        ExecutablePath = process.MainModule?.FileName ?? string.Empty,
                    };

                    return Task.FromResult(ProcessOperationResult<GameProcessInfo>.CreateSuccess(processInfo));
                }
                catch (ArgumentException)
                {
                    return Task.FromResult(ProcessOperationResult<GameProcessInfo>.CreateFailure("Process not found"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get process info for {ProcessId}", processId);
                return Task.FromResult(ProcessOperationResult<GameProcessInfo>.CreateFailure("Process not found"));
            }
        }

        /// <inheritdoc/>
        public Task<ProcessOperationResult<IReadOnlyList<GameProcessInfo>>> GetActiveProcessesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var activeProcesses = new List<GameProcessInfo>();

                foreach (var kvp in _managedProcesses.ToList())
                {
                    try
                    {
                        var process = kvp.Value;
                        if (!process.HasExited)
                        {
                            var processInfo = new GameProcessInfo
                            {
                                ProcessId = process.Id,
                                ProcessName = process.ProcessName,
                                StartTime = process.StartTime,
                                ExecutablePath = process.MainModule?.FileName ?? string.Empty,
                            };
                            activeProcesses.Add(processInfo);
                        }
                        else
                        {
                            // Remove exited processes from tracking
                            _managedProcesses.Remove(kvp.Key);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to get info for managed process {ProcessId}", kvp.Key);
                        _managedProcesses.Remove(kvp.Key);
                    }
                }

                return Task.FromResult(ProcessOperationResult<IReadOnlyList<GameProcessInfo>>.CreateSuccess(activeProcesses.AsReadOnly()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get active processes");
                return Task.FromResult(ProcessOperationResult<IReadOnlyList<GameProcessInfo>>.CreateFailure($"Failed to get active processes: {ex.Message}"));
            }
        }
    }
}
