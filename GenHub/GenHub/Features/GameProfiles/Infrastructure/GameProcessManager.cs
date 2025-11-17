using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.Events;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.Infrastructure;

/// <summary>
/// Manages game processes and their lifecycle.
/// </summary>
public class GameProcessManager(
    IConfigurationProviderService configProvider,
    ILogger<GameProcessManager> logger) : IGameProcessManager
{
    private readonly IConfigurationProviderService _configProvider = configProvider;
    private readonly ILogger<GameProcessManager> _logger = logger;
    private readonly ConcurrentDictionary<int, Process> _managedProcesses = new();

    /// <summary>
    /// Occurs when a managed game process has exited.
    /// Subscribers can use this event to react to process termination and perform cleanup.
    /// </summary>
    public event EventHandler<GameProcessExitedEventArgs>? ProcessExited;

    /// <inheritdoc/>
    public Task<OperationResult<GameProcessInfo>> StartProcessAsync(GameLaunchConfiguration configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                WorkingDirectory = configuration.WorkingDirectory
                    ?? Path.GetDirectoryName(configuration.ExecutablePath)
                    ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                CreateNoWindow = false,
            };

            // Handle .bat/.cmd files on Windows
            var extension = Path.GetExtension(configuration.ExecutablePath).ToLowerInvariant();
            if (Environment.OSVersion.Platform == PlatformID.Win32NT && (extension == ".bat" || extension == ".cmd"))
            {
                processStartInfo.FileName = "cmd.exe";
                processStartInfo.ArgumentList.Add("/c");
                processStartInfo.ArgumentList.Add(configuration.ExecutablePath);
            }
            else
            {
                processStartInfo.FileName = configuration.ExecutablePath;
            }

            // Add arguments
            if (configuration.Arguments != null)
            {
                foreach (var arg in configuration.Arguments)
                {
                    // If the key starts with - or --, treat it as a flag/option
                    if (arg.Key.StartsWith("-"))
                    {
                        processStartInfo.ArgumentList.Add(arg.Key);
                        if (!string.IsNullOrEmpty(arg.Value))
                        {
                            // Quote the value if it contains spaces
                            var quotedValue = arg.Value.Contains(' ') ? $"\"{arg.Value}\"" : arg.Value;
                            processStartInfo.ArgumentList.Add(quotedValue);
                        }
                    }
                    else if (string.IsNullOrEmpty(arg.Key))
                    {
                        // Positional argument - quote if contains spaces
                        var quotedValue = arg.Value.Contains(' ') ? $"\"{arg.Value}\"" : arg.Value;
                        processStartInfo.ArgumentList.Add(quotedValue);
                    }
                    else
                    {
                        // Key=value format - quote the value if it contains spaces
                        var quotedValue = arg.Value.Contains(' ') ? $"\"{arg.Value}\"" : arg.Value;
                        processStartInfo.ArgumentList.Add($"{arg.Key}={quotedValue}");
                    }
                }
            }

            // Add environment variables
            if (configuration.EnvironmentVariables != null)
            {
                foreach (var envVar in configuration.EnvironmentVariables)
                {
                    processStartInfo.Environment[envVar.Key] = envVar.Value;
                }
            }

            var process = Process.Start(processStartInfo);
            if (process == null)
            {
                return Task.FromResult(OperationResult<GameProcessInfo>.CreateFailure("Failed to start process"));
            }

            // Track the process
            _managedProcesses[process.Id] = process;

            if (configuration.WaitForExit)
            {
                var timeoutMs = configuration.Timeout.HasValue ? (int)configuration.Timeout.Value.TotalMilliseconds : Timeout.Infinite;
                process.WaitForExit(timeoutMs);
            }

            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += OnProcessExited;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enable raising events for process {ProcessId}, process cleanup may not work properly", process.Id);
            }

            var processInfo = new GameProcessInfo
            {
                ProcessId = process.Id,
                ProcessName = process.ProcessName,
                StartTime = process.StartTime,
                ExecutablePath = GetProcessExecutablePath(process),
            };

            _logger.LogInformation("Started game process {ProcessId} for executable {ExecutablePath}", process.Id, configuration.ExecutablePath);
            return Task.FromResult(OperationResult<GameProcessInfo>.CreateSuccess(processInfo));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start process for executable {ExecutablePath}", configuration.ExecutablePath);
            return Task.FromResult(OperationResult<GameProcessInfo>.CreateFailure($"Failed to start process: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> TerminateProcessAsync(int processId, CancellationToken cancellationToken = default)
    {
        try
        {
            Process? process = null;

            // Try to get from managed processes first
            if (!_managedProcesses.TryRemove(processId, out process))
            {
                // Try to get from system processes
                try
                {
                    process = Process.GetProcessById(processId);
                }
                catch (ArgumentException)
                {
                    return Task.FromResult(OperationResult<bool>.CreateFailure("Process not found"));
                }
            }

            if (process == null)
            {
                return Task.FromResult(OperationResult<bool>.CreateFailure("Process not found"));
            }

            // Try graceful termination first (only for processes with UI)
            bool hasExited = false;
            var timeout = TimeSpan.FromSeconds(10);

            if (process.MainWindowHandle != IntPtr.Zero)
            {
                process.CloseMainWindow();
                hasExited = process.WaitForExit((int)(timeout.TotalMilliseconds / 2));
            }

            // Force termination if graceful fails or no UI
            if (!hasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    hasExited = process.WaitForExit((int)(timeout.TotalMilliseconds / 2));
                }
                catch (InvalidOperationException)
                {
                    // Process already exited
                    hasExited = true;
                }
            }

            if (!hasExited)
            {
                return Task.FromResult(OperationResult<bool>.CreateFailure("Failed to terminate process within timeout"));
            }

            process.Dispose();

            _logger.LogInformation("Terminated process {ProcessId}", processId);
            return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to terminate process {ProcessId}", processId);
            return Task.FromResult(OperationResult<bool>.CreateFailure($"Failed to terminate process: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public Task<OperationResult<GameProcessInfo>> GetProcessInfoAsync(int processId, CancellationToken cancellationToken = default)
    {
        try
        {
            Process? process = null;

            if (_managedProcesses.TryGetValue(processId, out process))
            {
                if (process.HasExited)
                {
                    _managedProcesses.TryRemove(processId, out _);
                    return Task.FromResult(OperationResult<GameProcessInfo>.CreateFailure("Process not found"));
                }

                var processInfo = new GameProcessInfo
                {
                    ProcessId = process.Id,
                    ProcessName = process.ProcessName,
                    StartTime = process.StartTime,
                    ExecutablePath = GetProcessExecutablePath(process),
                };

                return Task.FromResult(OperationResult<GameProcessInfo>.CreateSuccess(processInfo));
            }

            // Try to get from system processes
            try
            {
                process = Process.GetProcessById(processId);
                if (process == null || process.HasExited)
                {
                    return Task.FromResult(OperationResult<GameProcessInfo>.CreateFailure("Process not found"));
                }

                var processInfo = new GameProcessInfo
                {
                    ProcessId = process.Id,
                    ProcessName = process.ProcessName,
                    StartTime = process.StartTime,
                    ExecutablePath = GetProcessExecutablePath(process),
                };

                return Task.FromResult(OperationResult<GameProcessInfo>.CreateSuccess(processInfo));
            }
            catch (ArgumentException)
            {
                return Task.FromResult(OperationResult<GameProcessInfo>.CreateFailure("Process not found"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get process info for {ProcessId}", processId);
            return Task.FromResult(OperationResult<GameProcessInfo>.CreateFailure("Process not found"));
        }
    }

    /// <inheritdoc/>
    public Task<OperationResult<IReadOnlyList<GameProcessInfo>>> GetActiveProcessesAsync(CancellationToken cancellationToken = default)
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
                            ExecutablePath = GetProcessExecutablePath(process),
                        };
                        activeProcesses.Add(processInfo);
                    }
                    else
                    {
                        // Remove exited processes from tracking
                        _managedProcesses.TryRemove(kvp.Key, out _);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get info for managed process {ProcessId}", kvp.Key);
                    _managedProcesses.TryRemove(kvp.Key, out _);
                }
            }

            return Task.FromResult(OperationResult<IReadOnlyList<GameProcessInfo>>.CreateSuccess(activeProcesses.AsReadOnly()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active processes");
            return Task.FromResult(OperationResult<IReadOnlyList<GameProcessInfo>>.CreateFailure($"Failed to get active processes: {ex.Message}"));
        }
    }

    private string GetProcessExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? string.Empty;
        }
        catch (Win32Exception)
        {
            // Cannot access MainModule due to security restrictions
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            // Process has exited
            return string.Empty;
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is not Process process)
            return;

        // Remove from managed processes
        _managedProcesses.TryRemove(process.Id, out _);

        // Raise the event
        var args = new GameProcessExitedEventArgs
        {
            ProcessId = process.Id,
            ExitCode = process.ExitCode,
            ExitTime = DateTime.UtcNow,
        };

        ProcessExited?.Invoke(this, args);
    }
}