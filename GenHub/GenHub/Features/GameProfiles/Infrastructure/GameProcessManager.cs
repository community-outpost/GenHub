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
    ILogger<GameProcessManager> logger) : IGameProcessManager, IDisposable
{
    private const int ProcessStartDelayMs = 100;
    private const int CleanupIntervalMs = 300000; // 5 minutes

    private readonly IConfigurationProviderService _configProvider = configProvider;
    private readonly ILogger<GameProcessManager> _logger = logger;
    private readonly ConcurrentDictionary<int, Process> _managedProcesses = new();
    private readonly SemaphoreSlim _terminationSemaphore = new(1, 1);

    /// <summary>
    /// Periodic timer to clean up dead processes and prevent memory leaks.
    /// </summary>
    private readonly Timer _cleanupTimer = new(
        _ => { /* Cleanup will be called through CleanupDeadProcesses */ },
        null,
        TimeSpan.FromMilliseconds(CleanupIntervalMs),
        TimeSpan.FromMilliseconds(CleanupIntervalMs));

    private bool _disposed;

    /// <summary>
    /// Occurs when a managed game process has exited.
    /// Subscribers can use this event to react to process termination and perform cleanup.
    /// </summary>
    public event EventHandler<GameProcessExitedEventArgs>? ProcessExited;

    /// <inheritdoc/>
    public async Task<OperationResult<GameProcessInfo>> StartProcessAsync(GameLaunchConfiguration configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate configuration
            if (configuration == null)
            {
                _logger.LogError("GameLaunchConfiguration is null");
                return OperationResult<GameProcessInfo>.CreateFailure("Configuration cannot be null");
            }

            if (string.IsNullOrEmpty(configuration.ExecutablePath))
            {
                _logger.LogError("ExecutablePath is null or empty in configuration");
                return OperationResult<GameProcessInfo>.CreateFailure("ExecutablePath cannot be null or empty");
            }

            if (!File.Exists(configuration.ExecutablePath))
            {
                _logger.LogError("Executable not found at path: {ExecutablePath}", configuration.ExecutablePath);
                return OperationResult<GameProcessInfo>.CreateFailure($"Executable not found: {configuration.ExecutablePath}");
            }

            _logger.LogInformation("[Process] Starting process for executable: {ExecutablePath}", configuration.ExecutablePath);

            var workingDirectory = configuration.WorkingDirectory
                ?? Path.GetDirectoryName(configuration.ExecutablePath)
                ?? Environment.CurrentDirectory;

            _logger.LogDebug("[Process] Working directory: {WorkingDirectory}", workingDirectory);

            var extension = Path.GetExtension(configuration.ExecutablePath).ToLowerInvariant();
            var isBatchFile = Environment.OSVersion.Platform == PlatformID.Win32NT && (extension == ".bat" || extension == ".cmd");

            // UseShellExecute = false is required for symlinks to CAS blobs (extensionless files).
            // When UseShellExecute = true, Windows follows the symlink and fails to recognize
            // the target as executable because it has no extension.
            // UseShellExecute = false launches the process directly using the symlink path,
            var processStartInfo = new ProcessStartInfo
            {
                WorkingDirectory = workingDirectory,
                FileName = configuration.ExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = false,
            };

            // Add arguments using Arguments string
            if (configuration.Arguments != null && configuration.Arguments.Count > 0)
            {
                _logger.LogDebug("[Process] Adding {ArgumentCount} arguments to process", configuration.Arguments.Count);
                var argList = new List<string>();

                foreach (var arg in configuration.Arguments)
                {
                    // If the key starts with - or --, treat it as a flag/option
                    if (arg.Key.StartsWith("-"))
                    {
                        argList.Add(arg.Key);
                        if (!string.IsNullOrEmpty(arg.Value))
                        {
                            // Quote the value if it contains spaces
                            var quotedValue = arg.Value.Contains(' ') ? $"\"{arg.Value}\"" : arg.Value;
                            argList.Add(quotedValue);
                        }

                        _logger.LogDebug("Added flag argument: {Key} {Value}", arg.Key, arg.Value);
                    }
                    else if (arg.Key.StartsWith("_pos"))
                    {
                        // Positional argument with index - quote if contains spaces
                        var quotedValue = arg.Value.Contains(' ') ? $"\"{arg.Value}\"" : arg.Value;
                        argList.Add(quotedValue);
                        _logger.LogDebug("Added positional argument: {Value}", quotedValue);
                    }
                    else if (string.IsNullOrEmpty(arg.Key))
                    {
                        // Legacy positional argument - quote if contains spaces
                        var quotedValue = arg.Value.Contains(' ') ? $"\"{arg.Value}\"" : arg.Value;
                        argList.Add(quotedValue);
                        _logger.LogDebug("Added positional argument: {Value}", quotedValue);
                    }
                    else
                    {
                        // Key=value format - quote the value if it contains spaces
                        var quotedValue = arg.Value.Contains(' ') ? $"\"{arg.Value}\"" : arg.Value;
                        argList.Add($"{arg.Key}={quotedValue}");
                        _logger.LogDebug("Added key-value argument: {Key}={Value}", arg.Key, quotedValue);
                    }
                }

                processStartInfo.Arguments = string.Join(" ", argList);
            }

            // With UseShellExecute = false, we can set environment variables
            if (configuration.EnvironmentVariables != null && configuration.EnvironmentVariables.Count > 0)
            {
                _logger.LogDebug(
                    "[Process] Setting {Count} environment variables",
                    configuration.EnvironmentVariables.Count);

                foreach (var envVar in configuration.EnvironmentVariables)
                {
                    processStartInfo.EnvironmentVariables[envVar.Key] = envVar.Value;
                    _logger.LogDebug("[Process] Set environment variable: {Key}={Value}", envVar.Key, envVar.Value);
                }
            }

            _logger.LogInformation(
                "[Process] Attempting to start process: {FileName} in {WorkingDirectory}",
                processStartInfo.FileName,
                processStartInfo.WorkingDirectory);

            Process? process = null;
            try
            {
                process = Process.Start(processStartInfo);
            }
            catch (Win32Exception win32Ex)
            {
                _logger.LogError(
                    win32Ex,
                    "Win32Exception starting process {ExecutablePath}: {ErrorCode} - {Message}",
                    configuration.ExecutablePath,
                    win32Ex.NativeErrorCode,
                    win32Ex.Message);
                return OperationResult<GameProcessInfo>.CreateFailure($"Failed to start process (Win32 Error {win32Ex.NativeErrorCode}): {win32Ex.Message}");
            }
            catch (InvalidOperationException invOpEx)
            {
                _logger.LogError(
                    invOpEx,
                    "InvalidOperationException starting process {ExecutablePath}: {Message}",
                    configuration.ExecutablePath,
                    invOpEx.Message);
                return OperationResult<GameProcessInfo>.CreateFailure($"Failed to start process (Invalid Operation): {invOpEx.Message}");
            }

            if (process == null)
            {
                _logger.LogError("[Process] Process.Start returned null for executable: {ExecutablePath}", configuration.ExecutablePath);
                return OperationResult<GameProcessInfo>.CreateFailure("Failed to start process - Process.Start returned null");
            }

            _logger.LogDebug("[Process] Process {ProcessId} started successfully", process.Id);

            if (!isBatchFile)
            {
                await Task.Delay(ProcessStartDelayMs, cancellationToken);
                if (process.HasExited)
                {
                    var exitCode = process.ExitCode;

                    // For Generals/Zero Hour, exit code 0 indicates the launcher spawned the actual game and exited
                    // Try to find the actual game process by executable name
                    if (exitCode == 0)
                    {
                        _logger.LogInformation(
                            "[Process] Launcher process {ProcessId} exited with code 0 - attempting to find spawned game process",
                            process.Id);

                        var executableName = Path.GetFileNameWithoutExtension(configuration.ExecutablePath);
                        var spawnedProcess = FindSpawnedGameProcess(executableName, configuration.WorkingDirectory ?? Path.GetDirectoryName(configuration.ExecutablePath)!);

                        if (spawnedProcess != null)
                        {
                            _logger.LogInformation(
                                "[Process] Found spawned game process {ProcessId} for executable {ExecutableName}",
                                spawnedProcess.Id,
                                executableName);

                            process.Dispose();

                            // Track the spawned process instead
                            _managedProcesses[spawnedProcess.Id] = spawnedProcess;

                            try
                            {
                                spawnedProcess.EnableRaisingEvents = true;
                                spawnedProcess.Exited += OnProcessExited;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to enable raising events for spawned process {ProcessId}", spawnedProcess.Id);
                            }

                            GameProcessInfo spawnedProcessInfo;
                            try
                            {
                                spawnedProcessInfo = new GameProcessInfo
                                {
                                    ProcessId = spawnedProcess.Id,
                                    ProcessName = spawnedProcess.ProcessName,
                                    StartTime = spawnedProcess.StartTime,
                                    ExecutablePath = GetProcessExecutablePath(spawnedProcess),
                                };
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to get process information for {ProcessId}, using minimal info", spawnedProcess.Id);
                                spawnedProcessInfo = new GameProcessInfo
                                {
                                    ProcessId = spawnedProcess.Id,
                                    ProcessName = "Unknown",
                                    StartTime = DateTime.Now,
                                    ExecutablePath = configuration.ExecutablePath,
                                };
                            }

                            _logger.LogInformation("Started game process {ProcessId} for executable {ExecutablePath}", spawnedProcess.Id, configuration.ExecutablePath);
                            return OperationResult<GameProcessInfo>.CreateSuccess(spawnedProcessInfo);
                        }
                    }

                    _logger.LogWarning("Process {ProcessId} exited immediately with code {ExitCode}", process.Id, exitCode);

                    var exitCodeMessage = exitCode switch
                    {
                        -1073741515 => "Missing DLL or dependency (STATUS_DLL_NOT_FOUND)",
                        -1073741502 => "Bad image format (STATUS_INVALID_IMAGE_FORMAT)",
                        -1073741790 => "Access denied (STATUS_ACCESS_DENIED)",
                        -1073741781 => "Application error (STATUS_APPLICATION_ERROR)",
                        _ => $"Unknown error code {exitCode}"
                    };
                    process.Dispose();
                    return OperationResult<GameProcessInfo>.CreateFailure($"Process exited immediately with code {exitCode}: {exitCodeMessage}");
                }
            }

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

            GameProcessInfo processInfo;
            try
            {
                processInfo = new GameProcessInfo
                {
                    ProcessId = process.Id,
                    ProcessName = process.ProcessName,
                    StartTime = process.StartTime,
                    ExecutablePath = GetProcessExecutablePath(process),
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get process information for {ProcessId}, using minimal info", process.Id);
                processInfo = new GameProcessInfo
                {
                    ProcessId = process.Id,
                    ProcessName = "Unknown",
                    StartTime = DateTime.Now,
                    ExecutablePath = configuration.ExecutablePath,
                };
            }

            _logger.LogInformation("Started game process {ProcessId} for executable {ExecutablePath}", process.Id, configuration.ExecutablePath);
            return OperationResult<GameProcessInfo>.CreateSuccess(processInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start process for executable {ExecutablePath}", configuration.ExecutablePath);
            return OperationResult<GameProcessInfo>.CreateFailure($"Failed to start process: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> TerminateProcessAsync(int processId, CancellationToken cancellationToken = default)
    {
        // Use semaphore to prevent concurrent termination attempts on the same or different processes
        // This prevents race conditions and ensures clean process state management
        await _terminationSemaphore.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("[Terminate] Starting termination of process {ProcessId}", processId);

            Process? process = null;

            // Try to get from managed processes first
            if (!_managedProcesses.TryRemove(processId, out process))
            {
                _logger.LogDebug("[Terminate] Process {ProcessId} not in managed processes, trying system lookup", processId);

                // Try to get from system processes
                try
                {
                    process = Process.GetProcessById(processId);
                    _logger.LogDebug("[Terminate] Found process {ProcessId} via system lookup", processId);
                }
                catch (ArgumentException)
                {
                    // Process not found - it may have already exited
                    _logger.LogInformation("[Terminate] Process {ProcessId} not found - already exited", processId);
                    return OperationResult<bool>.CreateSuccess(true);
                }
                catch (InvalidOperationException)
                {
                    // Process access denied or already exited
                    _logger.LogInformation("[Terminate] Process {ProcessId} is no longer accessible - access denied or already exited", processId);
                    return OperationResult<bool>.CreateSuccess(true);
                }
            }
            else
            {
                _logger.LogDebug("[Terminate] Found process {ProcessId} in managed processes", processId);
            }

            if (process == null)
            {
                _logger.LogInformation("[Terminate] Process {ProcessId} is null - already exited", processId);
                return OperationResult<bool>.CreateSuccess(true);
            }

            // Force kill immediately
            try
            {
                _logger.LogInformation("[Terminate] Force killing process {ProcessId} and its process tree", processId);
                process.Kill(entireProcessTree: true);

                // Wait for the process to actually exit
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));

                try
                {
                    await process.WaitForExitAsync(cts.Token);
                    _logger.LogInformation("[Terminate] Process {ProcessId} terminated successfully", processId);
                }
                catch (TaskCanceledException)
                {
                    _logger.LogWarning("[Terminate] Process {ProcessId} did not exit within 5 seconds after Kill()", processId);
                }
            }
            catch (InvalidOperationException ex)
            {
                // Process already exited
                _logger.LogInformation("[Terminate] Process {ProcessId} already exited: {Message}", processId, ex.Message);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                _logger.LogError(ex, "[Terminate] Win32 error killing process {ProcessId}: {ErrorCode}", processId, ex.NativeErrorCode);
                process.Dispose();
                return OperationResult<bool>.CreateFailure($"Failed to terminate process: {ex.Message}");
            }

            process.Dispose();
            _logger.LogInformation("Terminated process {ProcessId}", processId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Process {ProcessId} termination was cancelled", processId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to terminate process {ProcessId}", processId);
            return OperationResult<bool>.CreateFailure($"Failed to terminate process: {ex.Message}");
        }
        finally
        {
            _terminationSemaphore.Release();
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

    /// <summary>
    /// Cleans up dead processes from the managed processes dictionary.
    /// This prevents memory leaks from processes that exited without triggering the Exited event.
    /// Can be called periodically or on-demand.
    /// </summary>
    public void CleanupDeadProcesses()
    {
        var deadProcessIds = new List<int>();

        foreach (var kvp in _managedProcesses)
        {
            try
            {
                // Check if the process has exited
                if (kvp.Value.HasExited)
                {
                    deadProcessIds.Add(kvp.Key);
                    kvp.Value.Dispose();
                }
            }
            catch (InvalidOperationException)
            {
                // Process already disposed or inaccessible
                deadProcessIds.Add(kvp.Key);
            }
        }

        // Remove dead processes from the dictionary
        foreach (var processId in deadProcessIds)
        {
            _managedProcesses.TryRemove(processId, out _);
            _logger.LogTrace("Cleaned up dead process {ProcessId} from managed processes", processId);
        }

        if (deadProcessIds.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} dead processes from managed processes dictionary", deadProcessIds.Count);
        }
    }

    /// <summary>
    /// Disposes all managed resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogDebug("Disposing GameProcessManager with {Count} managed processes", _managedProcesses.Count);

        // Dispose cleanup timer first
        _cleanupTimer?.Dispose();

        // Clean up all managed processes
        foreach (var kvp in _managedProcesses)
        {
            try
            {
                kvp.Value.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing process {ProcessId}", kvp.Key);
            }
        }

        _managedProcesses.Clear();
        _terminationSemaphore.Dispose();
        _disposed = true;

        _logger.LogInformation("GameProcessManager disposed");
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

        var processId = process.Id;
        int? exitCode = null;
        try
        {
            exitCode = process.ExitCode;
        }
        catch
        {
            // Process may have already been disposed
        }

        // Remove from managed processes
        _managedProcesses.TryRemove(processId, out _);

        // Raise the event
        var args = new GameProcessExitedEventArgs
        {
            ProcessId = processId,
            ExitCode = exitCode,
            ExitTime = DateTime.UtcNow,
        };

        ProcessExited?.Invoke(this, args);

        _logger.LogInformation("Process {ProcessId} exited with code {ExitCode}", processId, exitCode);
    }

    /// <summary>
    /// Finds a spawned game process by executable name and working directory.
    /// Used when a launcher executable spawns the actual game and exits.
    /// </summary>
    /// <param name="executableName">The base executable name without extension.</param>
    /// <param name="workingDirectory">The expected working directory.</param>
    /// <returns>The spawned process if found, null otherwise.</returns>
    private Process? FindSpawnedGameProcess(string executableName, string workingDirectory)
    {
        try
        {
            var processes = Process.GetProcessesByName(executableName)
                .Where(p =>
                {
                    try
                    {
                        // Verify process was started within last 10 seconds
                        return (DateTime.Now - p.StartTime).TotalSeconds < 10;
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToArray();
            if (processes.Length == 0)
            {
                return null;
            }

            // If multiple processes exist, try to find one with matching working directory
            if (processes.Length > 1 && !string.IsNullOrEmpty(workingDirectory))
            {
                foreach (var proc in processes)
                {
                    try
                    {
                        var procPath = proc.MainModule?.FileName;
                        if (procPath != null && Path.GetDirectoryName(procPath)?.Equals(workingDirectory, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            // Dispose other processes we're not using
                            foreach (var otherProc in processes.Where(p => p.Id != proc.Id))
                            {
                                otherProc.Dispose();
                            }

                            return proc;
                        }
                    }
                    catch
                    {
                        // Cannot access process info, continue
                    }
                }
            }

            // Return the first (or only) process found
            var result = processes.First();

            // Dispose other processes
            foreach (var proc in processes.Skip(1))
            {
                proc.Dispose();
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to find spawned game process for {ExecutableName}", executableName);
            return null;
        }
    }
}