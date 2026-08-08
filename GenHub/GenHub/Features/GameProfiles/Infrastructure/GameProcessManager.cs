using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
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
    ILogger<GameProcessManager> logger) : IGameProcessManager, IDisposable
{
    private const int CleanupIntervalMs = ProcessConstants.ProcessCleanupIntervalMs;
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
                logger.LogError("GameLaunchConfiguration is null");
                return OperationResult<GameProcessInfo>.CreateFailure("Configuration cannot be null");
            }

            if (string.IsNullOrEmpty(configuration.ExecutablePath))
            {
                logger.LogError("ExecutablePath is null or empty in configuration");
                return OperationResult<GameProcessInfo>.CreateFailure("ExecutablePath cannot be null or empty");
            }

            if (!File.Exists(configuration.ExecutablePath))
            {
                logger.LogError("Executable not found at path: {ExecutablePath}", configuration.ExecutablePath);
                return OperationResult<GameProcessInfo>.CreateFailure($"Executable not found: {configuration.ExecutablePath}");
            }

            // Verify, never fix. Setting the bit here could mutate a shared content-store
            // blob under the hard-link workspace strategy; the workspace build already
            // applies it to a copy it owns. A failure here means that step did not run.
            if (!OperatingSystem.IsWindows() && !HasExecutePermission(configuration.ExecutablePath))
            {
                logger.LogError("[Process] Executable is not marked executable: {ExecutablePath}", configuration.ExecutablePath);
                return OperationResult<GameProcessInfo>.CreateFailure(
                    $"'{configuration.ExecutablePath}' does not have the execute permission set, so it cannot be launched.");
            }

            logger.LogInformation("[Process] Starting process for executable: {ExecutablePath}", configuration.ExecutablePath);

            var workingDirectory = configuration.WorkingDirectory
                ?? Path.GetDirectoryName(configuration.ExecutablePath)
                ?? Environment.CurrentDirectory;

            logger.LogDebug("[Process] Working directory: {WorkingDirectory}", workingDirectory);

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

                // Captured so that a process which dies during startup can say why.
                // A dynamic-loader failure prints "library not loaded" here and then exits;
                // without this the user sees no window and no error, and the exit code
                // alone does not identify the missing library.
                RedirectStandardError = true,
            };

            // Add arguments using Arguments string
            if (configuration.Arguments != null && configuration.Arguments.Count > 0)
            {
                logger.LogDebug("[Process] Adding {ArgumentCount} arguments to process", configuration.Arguments.Count);
                var argList = new List<string>();

                foreach (var arg in configuration.Arguments)
                {
                    // If the key starts with - or --, treat it as a flag/option
                    if (arg.Key.StartsWith('-'))
                    {
                        argList.Add(arg.Key);
                        if (!string.IsNullOrEmpty(arg.Value))
                        {
                            // Quote the value if it contains spaces
                            var quotedValue = arg.Value.Contains(' ') ? $"\"{arg.Value}\"" : arg.Value;
                            argList.Add(quotedValue);
                        }

                        logger.LogDebug("Added flag argument: {Key} {Value}", arg.Key, arg.Value);
                    }
                    else if (arg.Key.StartsWith("_pos"))
                    {
                        // Positional argument with index - quote if contains spaces
                        var quotedValue = arg.Value.Contains(' ') ? $"\"{arg.Value}\"" : arg.Value;
                        argList.Add(quotedValue);
                        logger.LogDebug("Added positional argument: {Value}", quotedValue);
                    }
                    else if (string.IsNullOrEmpty(arg.Key))
                    {
                        // Legacy positional argument - quote if contains spaces
                        var quotedValue = arg.Value.Contains(' ') ? $"\"{arg.Value}\"" : arg.Value;
                        argList.Add(quotedValue);
                        logger.LogDebug("Added positional argument: {Value}", quotedValue);
                    }
                    else
                    {
                        // Key=value format - quote the value if it contains spaces
                        var quotedValue = arg.Value.Contains(' ') ? $"\"{arg.Value}\"" : arg.Value;
                        argList.Add($"{arg.Key}={quotedValue}");
                        logger.LogDebug("Added key-value argument: {Key}={Value}", arg.Key, quotedValue);
                    }
                }

                processStartInfo.Arguments = string.Join(" ", argList);
            }

            // With UseShellExecute = false, we can set environment variables
            if (configuration.EnvironmentVariables != null && configuration.EnvironmentVariables.Count > 0)
            {
                logger.LogDebug(
                    "[Process] Setting {Count} environment variables",
                    configuration.EnvironmentVariables.Count);

                foreach (var envVar in configuration.EnvironmentVariables)
                {
                    processStartInfo.EnvironmentVariables[envVar.Key] = envVar.Value;
                    logger.LogDebug("[Process] Set environment variable: {Key}={Value}", envVar.Key, envVar.Value);
                }
            }

            logger.LogInformation(
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
                logger.LogError(
                    win32Ex,
                    "Win32Exception starting process {ExecutablePath}: {ErrorCode} - {Message}",
                    configuration.ExecutablePath,
                    win32Ex.NativeErrorCode,
                    win32Ex.Message);
                return OperationResult<GameProcessInfo>.CreateFailure($"Failed to start process (Win32 Error {win32Ex.NativeErrorCode}): {win32Ex.Message}");
            }
            catch (InvalidOperationException invOpEx)
            {
                logger.LogError(
                    invOpEx,
                    "InvalidOperationException starting process {ExecutablePath}: {Message}",
                    configuration.ExecutablePath,
                    invOpEx.Message);
                return OperationResult<GameProcessInfo>.CreateFailure($"Failed to start process (Invalid Operation): {invOpEx.Message}");
            }

            if (process == null)
            {
                logger.LogError("[Process] Process.Start returned null for executable: {ExecutablePath}", configuration.ExecutablePath);
                return OperationResult<GameProcessInfo>.CreateFailure("Failed to start process - Process.Start returned null");
            }

            logger.LogDebug("[Process] Process {ProcessId} started successfully", process.Id);

            // Drained continuously via the event handler rather than read at exit, so a
            // chatty process cannot fill the pipe buffer and deadlock. Only a bounded tail
            // is retained.
            var capturedErrors = new BoundedErrorBuffer();
            process.ErrorDataReceived += (_, e) => capturedErrors.Append(e.Data);
            try
            {
                process.BeginErrorReadLine();
            }
            catch (InvalidOperationException ex)
            {
                logger.LogDebug(ex, "[Process] Could not capture stderr for process {ProcessId}", process.Id);
            }

            // A launcher that hands the session to another binary is tracked through that binary.
            // Poll for it independently of the launcher's own lifetime: the Easy Anti-Cheat
            // bootstrapper outlives the game's startup by about a minute, so waiting for it to
            // exit first never finds the game.
            if (!string.IsNullOrWhiteSpace(configuration.ExpectedChildProcessName))
            {
                return await AdoptExpectedChildProcessAsync(process, configuration, workingDirectory, capturedErrors, cancellationToken);
            }

            // Check if process exited immediately (launcher pattern)
            // Only apply delay if we need to detect a spawned process
            if (!isBatchFile)
            {
                // Quick check to see if process exited immediately (launcher pattern)
                await Task.Delay(ProcessConstants.LauncherDetectionDelayMs, cancellationToken); // Reduced from 2000ms - only for launcher detection

                if (process.HasExited)
                {
                    var exitCode = process.ExitCode;

                    // Windows-only launcher-stub heuristic. Some Windows game clients are
                    // stubs that spawn the real process and exit 0, so the PID that was
                    // started is not the one to track. Finding the replacement matches on
                    // the executable's base name.
                    //
                    // Not applied on Unix, for two reasons. Native clients exec directly and
                    // never fork-and-exit, so there is nothing to find. And base-name
                    // matching strips the extension, which makes the native "generalszh"
                    // indistinguishable from TheSuperHackers' "generalszh.exe" — so a
                    // running Windows client under Wine could be adopted as if it were this
                    // launch, and later terminated on its owner's behalf.
                    if (exitCode == 0 && OperatingSystem.IsWindows())
                    {
                        logger.LogInformation(
                            "[Process] Launcher process {ProcessId} exited with code 0 - attempting to find spawned game process",
                            process.Id);

                        // A bootstrapper hands the session to a differently-named binary, so the
                        // name to adopt comes from the caller — never from the path we started.
                        var executableName = configuration.ExpectedChildProcessName
                            ?? Path.GetFileNameWithoutExtension(configuration.ExecutablePath);
                        var spawnedProcess = FindSpawnedGameProcess(executableName, configuration.WorkingDirectory ?? Path.GetDirectoryName(configuration.ExecutablePath)!);

                        if (spawnedProcess != null)
                        {
                            logger.LogInformation(
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
                                logger.LogWarning(ex, "Failed to enable raising events for spawned process {ProcessId}", spawnedProcess.Id);
                            }

                            var spawnedProcessInfo = BuildProcessInfo(spawnedProcess, configuration.ExecutablePath);

                            logger.LogInformation("Started game process {ProcessId} for executable {ExecutablePath}", spawnedProcess.Id, configuration.ExecutablePath);
                            return OperationResult<GameProcessInfo>.CreateSuccess(spawnedProcessInfo);
                        }
                    }

                    logger.LogWarning("Process {ProcessId} exited immediately with code {ExitCode}", process.Id, exitCode);

                    // The process has exited, but the async stderr handlers may not have
                    // delivered their final lines yet — and this is exactly the path where
                    // that output explains the failure. The parameterless overload is the
                    // documented way to wait for those handlers; the timed overloads
                    // return as soon as the process is gone and leave the capture short.
                    DrainStandardError(process, capturedErrors);

                    process.Dispose();

                    // If it exits immediately with a non-zero code (like a crash or missing DLL), this is a genuine failure
                    if (exitCode != 0)
                    {
                        var stderrTail = capturedErrors.ToString();
                        var detail = string.IsNullOrWhiteSpace(stderrTail)
                            ? "No output was captured."
                            : stderrTail;

                        logger.LogError(
                            "[Process] Process exited immediately with code {ExitCode}. Output: {Output}",
                            exitCode,
                            detail);

                        return OperationResult<GameProcessInfo>.CreateFailure(
                            $"Process exited immediately with code {exitCode}. {detail}");
                    }
                    else
                    {
                        // A clean exit with no spawned child is still an unexplained failure, and
                        // stderr was already drained above — reporting it only on a non-zero exit
                        // discarded the one diagnostic available for this case.
                        var stderrTail = capturedErrors.ToString();
                        var suffix = string.IsNullOrWhiteSpace(stderrTail) ? string.Empty : $" {stderrTail}";

                        logger.LogError(
                            "[Process] Process exited immediately with code 0 and no spawned process was found. Output: {Output}",
                            string.IsNullOrWhiteSpace(stderrTail) ? "No output was captured." : stderrTail);

                        // Still a failure: without a process to track the UI would sit in 'running'.
                        return OperationResult<GameProcessInfo>.CreateFailure(
                            $"Process exited immediately after launch.{suffix}");
                    }
                }
            }

            _managedProcesses[process.Id] = process;

            if (configuration.WaitForExit)
            {
                var timeoutMs = configuration.Timeout.HasValue ? (int)configuration.Timeout.Value.TotalMilliseconds : Timeout.Infinite;
                if (process.WaitForExit(timeoutMs))
                {
                    // Only once the process is known to have exited: the timed overload
                    // does not wait for the redirected-output handlers, so the capture
                    // would otherwise be read while lines are still in flight.
                    DrainStandardError(process, capturedErrors);
                }
            }

            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += OnProcessExited;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to enable raising events for process {ProcessId}, process cleanup may not work properly", process.Id);
            }

            var processInfo = BuildProcessInfo(process, configuration.ExecutablePath);

            logger.LogInformation("Started game process {ProcessId} for executable {ExecutablePath}", process.Id, configuration.ExecutablePath);
            return OperationResult<GameProcessInfo>.CreateSuccess(processInfo);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A cancelled launch is not a start failure. Reporting it as one hides the reason from
            // the caller and bypasses GameLauncher.LaunchProfileAsync's cancellation handling.
            logger.LogInformation("Start of {ExecutablePath} was cancelled", configuration.ExecutablePath);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start process for executable {ExecutablePath}", configuration.ExecutablePath);
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
            logger.LogInformation("[Terminate] Starting termination of process {ProcessId}", processId);

            // Try to get from managed processes first
            if (!_managedProcesses.TryRemove(processId, out Process? process))
            {
                logger.LogDebug("[Terminate] Process {ProcessId} not in managed processes, trying system lookup", processId);

                // Try to get from system processes
                try
                {
                    process = Process.GetProcessById(processId);
                    logger.LogDebug("[Terminate] Found process {ProcessId} via system lookup", processId);
                }
                catch (ArgumentException)
                {
                    // Process not found - it may have already exited
                    logger.LogInformation("[Terminate] Process {ProcessId} not found - already exited", processId);
                    return OperationResult<bool>.CreateSuccess(true);
                }
                catch (InvalidOperationException)
                {
                    // Process access denied or already exited
                    logger.LogInformation("[Terminate] Process {ProcessId} is no longer accessible - access denied or already exited", processId);
                    return OperationResult<bool>.CreateSuccess(true);
                }
            }
            else
            {
                logger.LogDebug("[Terminate] Found process {ProcessId} in managed processes", processId);
            }

            if (process == null)
            {
                logger.LogInformation("[Terminate] Process {ProcessId} is null - already exited", processId);
                return OperationResult<bool>.CreateSuccess(true);
            }

            // Force kill immediately - run on background thread to avoid blocking UI
            // process.Kill(entireProcessTree: true) is a synchronous blocking operation
            // that can take several seconds when terminating a process tree
            try
            {
                logger.LogInformation("[Terminate] Force killing process {ProcessId} and its process tree", processId);

                // Run Kill() on a background thread to prevent UI freeze
                await Task.Run(() => process.Kill(entireProcessTree: true), cancellationToken);

                logger.LogInformation("[Terminate] Process {ProcessId} terminated successfully", processId);
            }
            catch (InvalidOperationException ex)
            {
                // Process already exited
                logger.LogInformation("[Terminate] Process {ProcessId} already exited: {Message}", processId, ex.Message);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                logger.LogError(ex, "[Terminate] Win32 error killing process {ProcessId}: {ErrorCode}", processId, ex.NativeErrorCode);
                process.Dispose();
                return OperationResult<bool>.CreateFailure($"Failed to terminate process: {ex.Message}");
            }

            process.Dispose();
            logger.LogInformation("Terminated process {ProcessId}", processId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Process {ProcessId} termination was cancelled", processId);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to terminate process {ProcessId}", processId);
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
            if (_managedProcesses.TryGetValue(processId, out Process? process))
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
                    IsRunning = IsStillRunning(process),
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
                    IsRunning = IsStillRunning(process),
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
            logger.LogError(ex, "Failed to get process info for {ProcessId}", processId);
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
                            IsRunning = IsStillRunning(process),
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
                    logger.LogWarning(ex, "Failed to get info for managed process {ProcessId}", kvp.Key);
                    _managedProcesses.TryRemove(kvp.Key, out _);
                }
            }

            return Task.FromResult(OperationResult<IReadOnlyList<GameProcessInfo>>.CreateSuccess(activeProcesses.AsReadOnly()));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get active processes");
            return Task.FromResult(OperationResult<IReadOnlyList<GameProcessInfo>>.CreateFailure($"Failed to get active processes: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public void TrackProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (process.HasExited)
        {
            logger.LogWarning("[Process] Attempted to track already exited process {ProcessId}", process.Id);
            return;
        }

        logger.LogInformation("[Process] Registering existing process for tracking: {ProcessId} ({ProcessName})", process.Id, process.ProcessName);

        _managedProcesses[process.Id] = process;

        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += OnProcessExited;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Process] Failed to enable raising events for tracked process {ProcessId}", process.Id);
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<GameProcessInfo>> DiscoverAndTrackProcessAsync(string processName, string workingDirectory, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[Discover] Attempting to discover and track process: {Name} in {Directory}", processName, workingDirectory);

        // Poll for up to 45 seconds since Steam might need to start first, then launch the game
        // If Steam isn't running, steam:// URL will launch Steam (5-10s), then Steam launches the game (5-10s)
        const int MaxAttempts = ProcessConstants.SteamProcessDiscoveryMaxAttempts;
        const int DelayMs = ProcessConstants.SteamProcessDiscoveryDelayMs;

        for (int i = 0; i < MaxAttempts; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return OperationResult<GameProcessInfo>.CreateFailure("Discovery cancelled");
            }

            var process = FindSpawnedGameProcess(processName, workingDirectory);
            if (process != null)
            {
                logger.LogInformation("[Discover] Successfully discovered and tracked process {ProcessId}", process.Id);

                // Track it
                _managedProcesses[process.Id] = process;

                try
                {
                    process.EnableRaisingEvents = true;
                    process.Exited += OnProcessExited;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to enable raising events for discovered process {ProcessId}", process.Id);
                }

                // BuildProcessInfo assigns the fallback to GameProcessInfo.ExecutablePath, which
                // GameLauncher persists. Passing the directory alone would store a folder where a
                // file path is expected, so rebuild the executable path from what we were given.
                var fallbackExecutable = Path.Combine(
                    workingDirectory,
                    OperatingSystem.IsWindows() ? processName + ".exe" : processName);

                return OperationResult<GameProcessInfo>.CreateSuccess(BuildProcessInfo(process, fallbackExecutable));
            }

            await Task.Delay(DelayMs, cancellationToken);
        }

        logger.LogWarning("[Discover] Failed to discover process {Name} after {Attempts} attempts", processName, MaxAttempts);
        return OperationResult<GameProcessInfo>.CreateFailure($"Could not find process {processName} within the timeout period.");
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
            logger.LogTrace("Cleaned up dead process {ProcessId} from managed processes", processId);
        }

        if (deadProcessIds.Count > 0)
        {
            logger.LogDebug("Cleaned up {Count} dead processes from managed processes dictionary", deadProcessIds.Count);
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

        logger.LogDebug("Disposing GameProcessManager with {Count} managed processes", _managedProcesses.Count);

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
                logger.LogWarning(ex, "Error disposing process {ProcessId}", kvp.Key);
            }
        }

        _managedProcesses.Clear();
        _terminationSemaphore.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);

        logger.LogInformation("GameProcessManager disposed");
    }

    /// <summary>
    /// Reports whether a process is still running, treating an unreadable process as not running.
    /// </summary>
    /// <param name="process">The process to check.</param>
    /// <returns><see langword="true"/> when the process is known to be running.</returns>
    private static bool IsStillRunning(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static string GetProcessExecutablePath(Process process)
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

    /// <summary>
    /// Determines whether a file carries the Unix execute bit for the current user.
    /// </summary>
    /// <param name="path">The executable path.</param>
    /// <returns><c>true</c> on Windows, or when any execute bit is set.</returns>
    private static bool HasExecutePermission(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            return mode.HasFlag(UnixFileMode.UserExecute)
                || mode.HasFlag(UnixFileMode.GroupExecute)
                || mode.HasFlag(UnixFileMode.OtherExecute);
        }
        catch (Exception)
        {
            // Unreadable metadata should not block a launch that might otherwise work.
            return true;
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

        logger.LogInformation("Process {ProcessId} exited with code {ExitCode}", processId, exitCode);
    }

    /// <summary>
    /// Waits for a launcher to spawn the process named by
    /// <see cref="GameLaunchConfiguration.ExpectedChildProcessName"/> and tracks that process
    /// instead of the launcher. The launcher's own exit is never treated as the game exiting.
    /// </summary>
    /// <param name="launcher">The process that was started.</param>
    /// <param name="configuration">The launch configuration.</param>
    /// <param name="workingDirectory">The directory the game must run from.</param>
    /// <param name="capturedErrors">
    /// The launcher's captured stderr, quoted in the failure messages so a bootstrapper
    /// that refuses to start the game can say why.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The adopted child process, or a failure describing why none was adopted.</returns>
    private async Task<OperationResult<GameProcessInfo>> AdoptExpectedChildProcessAsync(
        Process launcher,
        GameLaunchConfiguration configuration,
        string workingDirectory,
        BoundedErrorBuffer capturedErrors,
        CancellationToken cancellationToken)
    {
        var expectedName = configuration.ExpectedChildProcessName!;
        var timeout = configuration.ExpectedChildDiscoveryTimeout
            ?? TimeSpan.FromMilliseconds(ProcessConstants.SpawnedChildDiscoveryTimeoutMs);
        var deadline = DateTime.UtcNow + timeout;
        var gracePeriod = TimeSpan.FromMilliseconds(ProcessConstants.LauncherExitGracePeriodMs);
        DateTime? launcherExitedAt = null;

        logger.LogInformation(
            "[Process] Waiting up to {TimeoutMs}ms for launcher {LauncherId} to start {ExpectedName}",
            (int)timeout.TotalMilliseconds,
            launcher.Id,
            expectedName);

        try
        {
            while (true)
            {
                var child = FindSpawnedGameProcess(expectedName, workingDirectory);
                if (child != null)
                {
                    _managedProcesses[child.Id] = child;

                    try
                    {
                        child.EnableRaisingEvents = true;
                        child.Exited += OnProcessExited;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to enable raising events for adopted process {ProcessId}", child.Id);
                    }

                    logger.LogInformation(
                        "[Process] Adopted game process {ProcessId} ({ExpectedName}); launcher {LauncherId} is no longer tracked and its exit is ignored",
                        child.Id,
                        expectedName,
                        launcher.Id);

                    return OperationResult<GameProcessInfo>.CreateSuccess(BuildProcessInfo(child, configuration.ExecutablePath));
                }

                // A launcher that fails outright will never produce a child - do not wait it out.
                if (launcher.HasExited && launcher.ExitCode != ProcessConstants.ExitCodeSuccess)
                {
                    logger.LogError(
                        "[Process] Launcher {LauncherId} exited with code {ExitCode} before starting {ExpectedName}",
                        launcher.Id,
                        launcher.ExitCode,
                        expectedName);
                    return OperationResult<GameProcessInfo>.CreateFailure(
                        AppendLauncherErrors(
                            $"Launcher exited with code {launcher.ExitCode} before starting {expectedName}.",
                            launcher,
                            capturedErrors));
                }

                // A clean exit with no child is still a failure - the bootstrapper bailing without
                // launching the game looks identical to success from the exit code alone. Allow a
                // short grace period for the spawn-then-enumerate race, then stop: once the
                // launcher is gone a child will not appear, and waiting out the full discovery
                // timeout only delays the failure and reports a misleading timeout as the cause.
                if (launcher.HasExited)
                {
                    launcherExitedAt ??= DateTime.UtcNow;

                    if (DateTime.UtcNow - launcherExitedAt.Value >= gracePeriod)
                    {
                        logger.LogError(
                            "[Process] Launcher {LauncherId} exited cleanly without starting {ExpectedName}",
                            launcher.Id,
                            expectedName);
                        // The launcher has provably exited here, so the drain is safe and this
                        // message carries the complete stderr rather than a partial snapshot.
                        return OperationResult<GameProcessInfo>.CreateFailure(
                            AppendLauncherErrors(
                                $"Launcher exited without starting {expectedName}.",
                                launcher,
                                capturedErrors));
                    }
                }

                if (DateTime.UtcNow >= deadline)
                {
                    logger.LogError(
                        "[Process] Launcher {LauncherId} did not start {ExpectedName} within {TimeoutMs}ms",
                        launcher.Id,
                        expectedName,
                        (int)timeout.TotalMilliseconds);
                    return OperationResult<GameProcessInfo>.CreateFailure(
                        AppendLauncherErrors(
                            $"Launcher did not start {expectedName} within {timeout.TotalSeconds:0.#}s.",
                            launcher,
                            capturedErrors));
                }

                await Task.Delay(ProcessConstants.SpawnedChildPollIntervalMs, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Matches TerminateProcessAsync, and lets GameLauncher.LaunchProfileAsync reach its
            // own cancellation branch instead of reporting a generic start failure.
            logger.LogInformation(
                "[Process] Adoption of {ExpectedName} was cancelled; terminating launcher {LauncherId}",
                expectedName,
                launcher.Id);

            TerminateAbandonedLauncher(launcher);
            throw;
        }
        finally
        {
            // Releases our handle only; the launcher keeps running and owns its own lifetime.
            launcher.Dispose();
        }
    }

    /// <summary>
    /// Kills a launcher whose child was never adopted. Without this a cancelled launch leaves the
    /// bootstrapper running with no tracked process and no handle for the caller to reach it.
    /// </summary>
    /// <param name="launcher">The launcher to terminate.</param>
    private void TerminateAbandonedLauncher(Process launcher)
    {
        try
        {
            if (launcher.HasExited)
            {
                return;
            }

            // The child may already exist but not yet be discoverable, so take the tree with it.
            launcher.Kill(entireProcessTree: true);
            launcher.WaitForExit(ProcessConstants.AbandonedLauncherKillWaitMs);
        }
        catch (Exception ex)
        {
            // The caller is already unwinding a cancellation; cleanup failure must not mask it.
            logger.LogWarning(ex, "[Process] Failed to terminate abandoned launcher {LauncherId}", launcher.Id);
        }
    }

    /// <summary>
    /// Builds process information, falling back to minimal details when the process cannot be read.
    /// </summary>
    /// <param name="process">The process to describe.</param>
    /// <param name="fallbackExecutablePath">Path to report when the process cannot be inspected.</param>
    /// <returns>The process information.</returns>
    private GameProcessInfo BuildProcessInfo(Process process, string fallbackExecutablePath)
    {
        try
        {
            return new GameProcessInfo
            {
                ProcessId = process.Id,
                ProcessName = process.ProcessName,
                StartTime = process.StartTime,
                ExecutablePath = GetProcessExecutablePath(process),
                IsRunning = IsStillRunning(process),
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get process information for {ProcessId}, using minimal info", process.Id);
            return new GameProcessInfo
            {
                ProcessId = process.Id,
                ProcessName = GameClientConstants.UnknownVersion,
                StartTime = DateTime.Now,
                ExecutablePath = fallbackExecutablePath,
                IsRunning = IsStillRunning(process),
            };
        }
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
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(executableName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to find spawned game process for {ExecutableName}", executableName);
            return null;
        }

        try
        {
            var candidates = new List<GameProcessCandidate>();
            foreach (var process in processes)
            {
                try
                {
                    var executablePath = GetProcessExecutablePath(process);
                    candidates.Add(new GameProcessCandidate(
                        process.Id,
                        process.ProcessName,
                        process.StartTime,
                        string.IsNullOrEmpty(executablePath) ? null : executablePath));
                }
                catch (Exception ex)
                {
                    // A process that cannot be inspected cannot be shown to be ours.
                    logger.LogDebug(ex, "Skipping uninspectable process {ProcessId}", process.Id);
                }
            }

            var selected = GameProcessSelector.SelectSpawnedGameProcess(
                candidates, executableName, workingDirectory, DateTime.Now);

            if (selected == null)
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }

                return null;
            }

            var match = processes.First(process => process.Id == selected.ProcessId);
            foreach (var other in processes.Where(process => process.Id != selected.ProcessId))
            {
                other.Dispose();
            }

            return match;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to find spawned game process for {ExecutableName}", executableName);
            foreach (var process in processes)
            {
                process.Dispose();
            }

            return null;
        }
    }

    /// <summary>
    /// Appends whatever the launcher wrote to stderr to a failure message.
    /// </summary>
    /// <param name="message">The failure message describing what was expected.</param>
    /// <param name="launcher">The launcher process whose stderr was captured.</param>
    /// <param name="capturedErrors">The buffer receiving the launcher's stderr lines.</param>
    /// <returns>The message, with the captured tail appended when there is one.</returns>
    /// <remarks>
    /// Without this the adoption failures say only that the game never appeared, which is
    /// the symptom rather than the cause. A bootstrapper that refuses to start the game —
    /// a missing Easy Anti-Cheat installation being the expected case — explains itself on
    /// stderr, and that explanation is the only thing that makes the failure actionable.
    /// </remarks>
    private string AppendLauncherErrors(string message, Process launcher, BoundedErrorBuffer capturedErrors)
    {
        // Only drain once the launcher has exited. Draining waits on the stderr handlers,
        // which requires the untimed WaitForExit — and on the discovery-timeout path the
        // bootstrapper is still running and outlives game startup by about a minute, so
        // waiting there would stall the failure long past the timeout it is reporting.
        // A live launcher contributes whatever has already arrived instead.
        // Broad by intent, matching DrainStandardError below. HasExited throws
        // InvalidOperationException with no handle and Win32Exception when the exit code
        // cannot be read — the latter being a plausible result for the hard-crashing
        // launcher this method exists to report on. Letting either escape would turn a
        // failure result into a thrown exception on the path describing that failure.
        var launcherExited = false;
        try
        {
            launcherExited = launcher.HasExited;
        }
        catch (Exception ex)
        {
            // No launcher property is read here: Id throws once the process is disposed,
            // which is one of the states that lands in this catch to begin with.
            logger.LogDebug(ex, "[Process] Could not determine whether the launcher had exited");
        }

        if (launcherExited)
        {
            DrainStandardError(launcher, capturedErrors);
        }

        var detail = capturedErrors.ToString();

        return string.IsNullOrWhiteSpace(detail) ? message : $"{message} {detail}";
    }

    /// <summary>
    /// Waits for the asynchronous stderr handlers to finish before the capture is read.
    /// </summary>
    /// <remarks>
    /// <see cref="Process.WaitForExit()"/> without a timeout additionally waits for
    /// redirected-output handlers to complete; the timed overloads do not, so reading the
    /// buffer straight after the process exits can miss the final lines. Only stderr is
    /// redirected, so there is no stdout stream to drain.
    /// </remarks>
    /// <param name="process">The exited process.</param>
    /// <param name="capturedErrors">The buffer receiving stderr lines.</param>
    private void DrainStandardError(Process process, BoundedErrorBuffer capturedErrors)
    {
        try
        {
            process.WaitForExit();
        }
        catch (Exception ex)
        {
            // The process may already be disposed or inaccessible; the capture is then
            // whatever arrived, which is better than propagating from a diagnostics path.
            logger.LogDebug(ex, "[Process] Could not wait for stderr handlers to complete");
        }

        if (!capturedErrors.EndOfStreamReached)
        {
            logger.LogDebug(
                "[Process] stderr did not signal end of stream; the captured output may be incomplete");
        }
    }

    /// <summary>
    /// Retains a bounded excerpt of a process's stderr for diagnostics.
    /// </summary>
    /// <remarks>
    /// Keeps the first lines as well as the last. A tail-only buffer loses the startup
    /// context — the missing library, the rejected argument — which is usually where the
    /// cause is, while the tail holds the symptom. Both are bounded by line count, by
    /// individual line length and by total size, so a process writing a pathological
    /// volume cannot exhaust memory.
    /// </remarks>
    private sealed class BoundedErrorBuffer
    {
        private const int MaxHeadLines = 10;
        private const int MaxTailLines = 20;
        private const int MaxLineLength = 2000;
        private const int MaxTotalChars = 64 * 1024;

        private readonly List<string> _head = [];
        private readonly Queue<string> _tail = new();
        private readonly object _gate = new();
        private int _retainedChars;
        private int _droppedLines;
        private bool _endOfStream;

        /// <inheritdoc/>
        public override string ToString()
        {
            lock (_gate)
            {
                var parts = new List<string>(_head);

                if (_droppedLines > 0)
                {
                    parts.Add($"…[{_droppedLines} line(s) omitted]");
                }

                parts.AddRange(_tail);

                return string.Join(" | ", parts);
            }
        }

        /// <summary>
        /// Gets a value indicating whether the stream signalled end of output.
        /// </summary>
        /// <remarks>
        /// The framework raises the handler once with a null <c>Data</c> when the stream
        /// closes. That, not process exit, is the point at which the capture is known to
        /// be complete.
        /// </remarks>
        internal bool EndOfStreamReached
        {
            get
            {
                lock (_gate)
                {
                    return _endOfStream;
                }
            }
        }

        /// <summary>
        /// Appends a line, or records end of stream when <paramref name="line"/> is null.
        /// </summary>
        /// <param name="line">The line received, or null at end of stream.</param>
        internal void Append(string? line)
        {
            lock (_gate)
            {
                if (line is null)
                {
                    _endOfStream = true;
                    return;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    return;
                }

                var trimmed = line.Length > MaxLineLength
                    ? string.Concat(line.AsSpan(0, MaxLineLength), "…[line truncated]")
                    : line;

                if (_head.Count < MaxHeadLines)
                {
                    _head.Add(trimmed);
                    _retainedChars += trimmed.Length;
                    return;
                }

                _tail.Enqueue(trimmed);
                _retainedChars += trimmed.Length;

                while (_tail.Count > MaxTailLines || (_retainedChars > MaxTotalChars && _tail.Count > 0))
                {
                    _retainedChars -= _tail.Dequeue().Length;
                    _droppedLines++;
                }
            }
        }
    }
}
