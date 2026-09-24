using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Events;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using GenHub.Core.Utilities;
using GenHub.Features.Launching;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GameProfiles.Infrastructure;

/// <summary>
/// Manages game processes and their lifecycle.
/// </summary>
public class GameProcessManager(
    ILogger<GameProcessManager> logger,
    IGameLaunchRunner launchRunner,
    ILocalizationService localizationService,
    IFlatpakProvisioner flatpakProvisioner) : IGameProcessManager, IDisposable
{
    private sealed class ExitFinalizationState
    {
        private int _finalized;

        public Guid InstanceId { get; } = Guid.NewGuid();

        public bool TryFinalize() => Interlocked.Exchange(ref _finalized, 1) == 0;
    }

    private readonly ConditionalWeakTable<Process, ExitFinalizationState> _exitFinalizations = new();
    private readonly ConcurrentDictionary<int, Process> _managedProcesses = new();

    /// <summary>
    /// Stderr captures for processes this manager started itself, keyed by process instance.
    /// </summary>
    /// <remarks>
    /// The late-failure channel: an initialisation abort slow enough to outlive the
    /// post-spawn detection window exits after the launch was reported as started, and
    /// its stderr — the only explanation of the failure — would otherwise be dropped with
    /// the start operation's locals. Kept per process instance so <see cref="OnProcessExited"/> can
    /// attach it to the exit event.
    /// </remarks>
    private readonly ConcurrentDictionary<Process, BoundedErrorBuffer> _stderrBuffers = new();

    /// <summary>
    /// Process instances whose termination was requested through <see cref="TerminateProcessAsync"/>,
    /// marked before the kill is attempted.
    /// </summary>
    /// <remarks>
    /// A deliberate stop kills the process, and a killed process exits non-zero — which
    /// is exactly the signature the late-failure channel treats as a crash. Every stop
    /// path in the application funnels through <see cref="TerminateProcessAsync"/>, so
    /// marking here lets the exit event distinguish "the user stopped it" from "it
    /// died", and downstream consumers suppress the failure classification.
    /// </remarks>
    private readonly ConcurrentDictionary<Process, byte> _requestedTerminations = new();
    private readonly SemaphoreSlim _terminationSemaphore = new(1, 1);

    private bool _disposed;

    /// <summary>Gets or sets the process lookup used by termination; tests can supply a lookup that never accesses the OS.</summary>
    internal Func<int, Process> TerminationProcessLookup { get; set; } = Process.GetProcessById;

    /// <summary>
    /// Occurs when a managed game process has exited.
    /// Subscribers can use this event to react to process termination and perform cleanup.
    /// </summary>
    /// <remarks>
    /// Handlers run synchronously and must not block waiting for another termination.
    /// A termination may hold the manager's semaphore while publishing this event;
    /// schedule follow-up asynchronous work and return promptly.
    /// </remarks>
    public event EventHandler<GameProcessExitedEventArgs>? ProcessExited;

    /// <inheritdoc/>
    public async Task<OperationResult<GameProcessInfo>> StartProcessAsync(GameLaunchConfiguration configuration, CancellationToken cancellationToken = default)
    {
        Process? process = null;
        try
        {
            var validationResult = ValidateLaunchConfiguration(configuration);
            if (!validationResult.Success)
            {
                return OperationResult<GameProcessInfo>.CreateFailure(validationResult.FirstError ?? "Invalid configuration");
            }

            logger.LogInformation("[Process] Starting process for executable: {ExecutablePath}", configuration.ExecutablePath);

            var isFlatpak = IsFlatpakLaunch(configuration);
            var runnerResult = isFlatpak
                ? await ResolveFlatpakCommandAsync(configuration, cancellationToken)
                : launchRunner.ResolveCommand(configuration);
            if (!runnerResult.Success || runnerResult.Data is null)
            {
                if (isFlatpak)
                {
                    logger.LogWarning("[Process] Flatpak provisioning failed: {Error}", runnerResult.FirstError);
                    return OperationResult<GameProcessInfo>.CreateFailure(runnerResult.FirstError!);
                }

                logger.LogWarning("[Process] Compatibility runner could not resolve a launch command: {Error}", runnerResult.FirstError);
                var missingRunnerMessage = GetMissingRunnerMessage();
                var errorMessage = string.IsNullOrWhiteSpace(runnerResult.FirstError)
                    ? missingRunnerMessage
                    : $"{missingRunnerMessage} ({runnerResult.FirstError})";
                return OperationResult<GameProcessInfo>.CreateFailure(errorMessage);
            }

            var workingDirectory = configuration.WorkingDirectory
                ?? Path.GetDirectoryName(configuration.ExecutablePath)
                ?? Environment.CurrentDirectory;

            logger.LogDebug("[Process] Working directory: {WorkingDirectory}", workingDirectory);

            var extension = Path.GetExtension(configuration.ExecutablePath).ToLowerInvariant();
            var isBatchFile = Environment.OSVersion.Platform == PlatformID.Win32NT && (extension == ".bat" || extension == ".cmd");

            var processStartInfo = ConfigureProcessStartInfo(configuration, workingDirectory, runnerResult.Data);

            logger.LogInformation(
                "[Process] Attempting to start process: {FileName} in {WorkingDirectory}",
                processStartInfo.FileName,
                processStartInfo.WorkingDirectory);

            var startResult = StartNativeProcess(processStartInfo, configuration.ExecutablePath);
            if (!startResult.Success || startResult.Data == null)
            {
                return OperationResult<GameProcessInfo>.CreateFailure(startResult.FirstError ?? "Failed to start process");
            }

            var launchTimeFallback = DateTime.UtcNow;
            process = startResult.Data;
            logger.LogDebug("[Process] Process {ProcessId} started successfully", process.Id);

            // Read while the launcher is still alive: a Unix process that has exited can no longer
            // report its start time, and that time is the only thing separating the child this
            // launch spawned from an instance of the same game the user already had running.
            var launcherStartTime = ReadStartTime(process) ?? launchTimeFallback;

            var capturedErrors = SetupErrorRedirection(process);
            _stderrBuffers[process] = capturedErrors;

            var isWine = IsWineLaunch(launchRunner, runnerResult.Data);
            if (isWine && !string.IsNullOrWhiteSpace(configuration.ExpectedChildProcessName))
            {
                logger.LogDebug(
                    "[Process] Skipping child process adoption for {ExpectedName} because target is running under Wine ({RunnerBinary})",
                    configuration.ExpectedChildProcessName,
                    runnerResult.Data.FileName);
            }

            if (!string.IsNullOrWhiteSpace(configuration.ExpectedChildProcessName) && !isWine)
            {
                return await AdoptExpectedChildProcessAsync(process, configuration, workingDirectory, launcherStartTime, capturedErrors, cancellationToken);
            }

            // Observe initialization failures before reporting a running process.
            if (!isBatchFile && await WaitForExitWithinWindowAsync(process, cancellationToken))
            {
                return await HandleImmediateProcessExitAsync(process, configuration, launcherStartTime, capturedErrors, cancellationToken);
            }

            _managedProcesses[process.Id] = process;

            if (configuration.WaitForExit)
            {
                var timeoutMs = configuration.Timeout.HasValue ? (int)configuration.Timeout.Value.TotalMilliseconds : Timeout.Infinite;
                if (process.WaitForExit(timeoutMs))
                {
                    DrainStandardError(capturedErrors);
                }
            }

            // Enabling exit events can synchronously finalize and dispose an exited process.
            // This can wait up to StderrDrainTimeoutMs for stderr; subscribers must return promptly.
            var processInfo = BuildProcessInfo(process, configuration.ExecutablePath);
            RegisterProcessEventHandlers(process);

            logger.LogInformation("Started game process {ProcessId} for executable {ExecutablePath}", processInfo.ProcessId, configuration.ExecutablePath);
            return OperationResult<GameProcessInfo>.CreateSuccess(processInfo);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryRemoveProcessErrors(process);

            await HandleProcessCancellationAsync(process, configuration?.ExecutablePath ?? "unknown");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start process for executable {ExecutablePath}", configuration?.ExecutablePath);
            if (process != null)
            {
                TryRemoveProcessErrors(process);
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception killEx)
                {
                    logger.LogDebug(killEx, "[Process] Ignored exception while terminating untracked process for {ExecutablePath}", configuration?.ExecutablePath);
                }
                finally
                {
                    process.Dispose();
                }
            }

            return OperationResult<GameProcessInfo>.CreateFailure($"Failed to start process: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> TerminateProcessAsync(int processId, CancellationToken cancellationToken = default)
    {
        // Unix treats zero and negative PIDs as process groups or broadcast targets.
        // Reject them before any process lookup, subscription, or operating-system call.
        if (processId <= 0)
        {
            return OperationResult<bool>.CreateFailure(ProcessConstants.InvalidProcessIdError);
        }

        await _terminationSemaphore.WaitAsync(cancellationToken);
        Process? process = null;
        var ownsProcess = false;
        var terminated = false;
        var exitObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void ObserveExit(object? sender, GameProcessExitedEventArgs args)
        {
            if (process != null && args.ProcessId == processId
                && args.ProcessInstanceId == _exitFinalizations.GetValue(process, _ => new ExitFinalizationState()).InstanceId)
            {
                exitObserved.TrySetResult();
            }
        }

        // Subscribe before looking up the process: an exit before the lookup removes
        // it from managed state, while a later managed exit is observed by this handler.
        ProcessExited += ObserveExit;
        try
        {
            // Keep a managed process tracked until its exit is observed. A failed or
            // cancelled Stop must leave it available for later monitoring and retries.
            if (!_managedProcesses.TryGetValue(processId, out process))
            {
                try
                {
                    process = TerminationProcessLookup(processId);
                    ownsProcess = true;
                }
                catch (ArgumentException)
                {
                    return OperationResult<bool>.CreateSuccess(true);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!ownsProcess)
            {
                _requestedTerminations[process] = 1;
            }

            logger.LogInformation("[Terminate] Force killing process {ProcessId} and its process tree", processId);
            await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    process.Kill(entireProcessTree: true);
                },
                cancellationToken);

            // Once Kill has been issued, finish observing the exit even if the caller
            // cancels. Disposal before the exit callback can suppress its notification.
            await process.WaitForExitAsync(CancellationToken.None);
            if (!ownsProcess)
            {
                await WaitForExitNotificationAsync(exitObserved.Task, processId);
                FinalizeProcessExit(process, processId);
            }

            terminated = true;
            logger.LogInformation("Terminated process {ProcessId}", processId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Process {ProcessId} termination was cancelled", processId);
            throw;
        }
        catch (InvalidOperationException ex)
        {
            logger.LogInformation(ex, "Process {ProcessId} already exited", processId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to terminate process {ProcessId}", processId);
            return OperationResult<bool>.CreateFailure($"Failed to terminate process: {ex.Message}");
        }
        finally
        {
            ProcessExited -= ObserveExit;
            CleanupTerminationProcess(process, ownsProcess, terminated);

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
                    FinalizeProcessExit(process, processId);
                    return Task.FromResult(OperationResult<GameProcessInfo>.CreateFailure(ProcessConstants.ProcessNotFoundErrorMessage));
                }

                var processInfo = new GameProcessInfo
                {
                    ProcessId = process.Id,
                    ProcessInstanceId = _exitFinalizations.GetValue(process, _ => new ExitFinalizationState()).InstanceId,
                    ProcessName = process.ProcessName,
                    StartTime = process.StartTime.ToUniversalTime(),
                    HasVerifiedStartTime = true,
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
                    return Task.FromResult(OperationResult<GameProcessInfo>.CreateFailure(ProcessConstants.ProcessNotFoundErrorMessage));
                }

                var processInfo = new GameProcessInfo
                {
                    ProcessId = process.Id,
                    ProcessName = process.ProcessName,
                    StartTime = process.StartTime.ToUniversalTime(),
                    HasVerifiedStartTime = true,
                    ExecutablePath = GetProcessExecutablePath(process),
                    IsRunning = IsStillRunning(process),
                };

                return Task.FromResult(OperationResult<GameProcessInfo>.CreateSuccess(processInfo));
            }
            catch (ArgumentException)
            {
                return Task.FromResult(OperationResult<GameProcessInfo>.CreateFailure(ProcessConstants.ProcessNotFoundErrorMessage));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get process info for {ProcessId}", processId);
            return Task.FromResult(OperationResult<GameProcessInfo>.CreateFailure($"Failed to get process info for {processId}: {ex.Message}"));
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
                            ProcessInstanceId = _exitFinalizations.GetValue(process, _ => new ExitFinalizationState()).InstanceId,
                            ProcessName = process.ProcessName,
                            StartTime = process.StartTime.ToUniversalTime(),
                            HasVerifiedStartTime = true,
                            ExecutablePath = GetProcessExecutablePath(process),
                            IsRunning = IsStillRunning(process),
                        };
                        activeProcesses.Add(processInfo);
                    }
                    else
                    {
                        FinalizeProcessExit(process, kvp.Key);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to get info for managed process {ProcessId}", kvp.Key);

                    // A transient inspection failure does not transfer ownership. Keep the
                    // subscribed instance and its diagnostics available for Stop and later polls.
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
    public GameProcessInfo? TrackProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (process.HasExited)
        {
            logger.LogWarning("[Process] Attempted to track already exited process {ProcessId}", process.Id);
            return null;
        }

        logger.LogInformation("[Process] Registering existing process for tracking: {ProcessId} ({ProcessName})", process.Id, process.ProcessName);

        var processInfo = BuildProcessInfo(process, GetProcessExecutablePath(process));
        _managedProcesses[process.Id] = process;

        RegisterProcessEventHandlers(process);
        return processInfo;
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

                // BuildProcessInfo assigns the fallback to GameProcessInfo.ExecutablePath, which
                // GameLauncher persists. Passing the directory alone would store a folder where a
                // file path is expected, so rebuild the executable path from what we were given.
                var fallbackExecutable = Path.Combine(
                    workingDirectory,
                    OperatingSystem.IsWindows() ? processName + ".exe" : processName);

                var processInfo = BuildProcessInfo(process, fallbackExecutable);
                RegisterProcessEventHandlers(process);
                return OperationResult<GameProcessInfo>.CreateSuccess(processInfo);
            }

            await Task.Delay(DelayMs, cancellationToken);
        }

        logger.LogWarning("[Discover] Failed to discover process {Name} after {Attempts} attempts", processName, MaxAttempts);
        return OperationResult<GameProcessInfo>.CreateFailure($"Could not find process {processName} within the timeout period.");
    }

    /// <summary>
    /// Cleans up dead processes from the managed processes dictionary.
    /// This prevents memory leaks from processes that exited without triggering the Exited event.
    /// Explicit maintenance hook only; no production timer currently invokes it.
    /// Normal cleanup is performed by the exit callback and termination fallback.
    /// </summary>
    public void CleanupDeadProcesses()
    {
        var deadProcesses = new List<KeyValuePair<int, Process>>();

        foreach (var kvp in _managedProcesses)
        {
            try
            {
                // Check if the process has exited
                if (kvp.Value.HasExited)
                {
                    deadProcesses.Add(kvp);
                }
            }
            catch (InvalidOperationException)
            {
                // Process already disposed or inaccessible
                deadProcesses.Add(kvp);
            }
        }

        // Remove dead processes from the dictionary
        foreach (var entry in deadProcesses)
        {
            FinalizeProcessExit(entry.Value, entry.Key);
            entry.Value.Dispose();
            logger.LogTrace("Cleaned up dead process {ProcessId} from managed processes", entry.Key);
        }

        if (deadProcesses.Count > 0)
        {
            logger.LogDebug("Cleaned up {Count} dead processes from managed processes dictionary", deadProcesses.Count);
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
        _stderrBuffers.Clear();
        _requestedTerminations.Clear();
        _terminationSemaphore.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);

        logger.LogInformation("GameProcessManager disposed");
    }

    /// <summary>
    /// Resolves the host directories a Flatpak client needs to see inside its sandbox:
    /// the retail archive roots, the working directory / workspace, and the native options directory.
    /// Flatpak hides the host filesystem by default, so without these binds a client
    /// resolving game data from its environment or running from a workspace fails on paths
    /// that exist on the host.
    /// </summary>
    /// <param name="environment">The launch environment carrying the install-path variables.</param>
    /// <param name="workingDirectory">The working directory (e.g. workspace directory) for the launch.</param>
    /// <param name="optionsIniPath">The path to Options.ini on the host, if configured.</param>
    /// <returns>The existing bind roots, with nested duplicates removed.</returns>
    internal static IReadOnlyList<string> ResolveFlatpakFilesystemBinds(
        IReadOnlyDictionary<string, string>? environment,
        string? workingDirectory = null,
        string? optionsIniPath = null)
    {
        var binds = new List<string>();

        if (environment is not null)
        {
            foreach (var variable in RetailArchiveConstants.InstallPathVariables)
            {
                if (environment.TryGetValue(variable, out var root))
                {
                    AddValidBindRoot(binds, root);
                }
            }
        }

        AddValidBindRoot(binds, workingDirectory);

        if (!string.IsNullOrWhiteSpace(optionsIniPath))
        {
            AddValidBindRoot(binds, Path.GetDirectoryName(optionsIniPath));
        }

        return binds;
    }

    /// <summary>
    /// Builds the list of <c>--env=NAME=VALUE</c> arguments to forward environment variables
    /// into the Flatpak sandbox.
    /// </summary>
    /// <param name="appId">The Flatpak application identifier.</param>
    /// <param name="environment">The environment variables configured for launch.</param>
    /// <param name="workingDirectory">The working directory (e.g. GenHub workspace) prepared for launch.</param>
    /// <returns>A list of formatted <c>--env=KEY=VALUE</c> strings.</returns>
    internal static IReadOnlyList<string> ResolveFlatpakEnvironmentArguments(
        string appId,
        IReadOnlyDictionary<string, string>? environment,
        string? workingDirectory = null)
    {
        var result = new List<string>();
        if (environment is null || environment.Count == 0)
        {
            return result;
        }

        var isZeroHourFlatpak = ContentFormatConstants.IsZeroHourFlatpakAppId(appId);
        var targetZhPath = ResolveZeroHourTargetPath(workingDirectory, environment);

        foreach (var (key, value) in environment)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var envValue = ResolveFlatpakEnvValue(key, value, isZeroHourFlatpak, workingDirectory, targetZhPath);
            if (envValue is not null)
            {
                result.Add($"{ContentFormatConstants.FlatpakEnvOptionPrefix}{key}={envValue}");
            }
        }

        return result;
    }

    /// <summary>Bounds notification delivery so a missing callback cannot block future stops.</summary>
    /// <param name="notification">Completion of the managed exit notification.</param>
    /// <param name="processId">The process that has exited.</param>
    /// <param name="timeProvider">The clock for the timeout; defaults to the system clock.</param>
    /// <returns>A task completing after notification delivery or its timeout.</returns>
    internal async Task WaitForExitNotificationAsync(Task notification, int processId, TimeProvider? timeProvider = null)
    {
        try
        {
            await notification.WaitAsync(TimeSpan.FromMilliseconds(ProcessConstants.TerminationExitNotificationTimeoutMs), timeProvider ?? TimeProvider.System);
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(ex, "Timed out waiting for exit notification for process {ProcessId}; continuing termination cleanup", processId);
        }
    }

    /// <summary>Handles a process exit, including a callback delayed past disposal.</summary>
    /// <param name="sender">The process instance.</param>
    /// <param name="e">The event arguments.</param>
    internal void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is not Process process)
            return;

        var processId = 0;
        try
        {
            processId = process.Id;
        }
        catch (InvalidOperationException)
        {
            // A delayed callback can arrive after termination disposed the process.
            processId = _managedProcesses.FirstOrDefault(entry => ReferenceEquals(entry.Value, process)).Key;
            if (processId == 0)
            {
                return;
            }
        }

        FinalizeProcessExit(process, processId);
    }

    /// <summary>Publishes one exit and clears only state belonging to this process instance.</summary>
    /// <param name="process">The exited process; no signal is sent by this method.</param>
    /// <param name="processId">Its positive process ID, captured before disposal.</param>
    internal void FinalizeProcessExit(Process process, int processId)
    {
        if (processId <= 0 || !_exitFinalizations.GetValue(process, _ => new ExitFinalizationState()).TryFinalize())
        {
            return;
        }

        var exitTime = DateTime.UtcNow;
        int? exitCode = null;
        try
        {
            exitTime = process.ExitTime.ToUniversalTime();
            exitCode = process.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Process may have already been disposed or its metadata may be inaccessible.
        }

        // A delayed callback must not remove a new process that reused the same PID.
        _managedProcesses.TryRemove(new KeyValuePair<int, Process>(processId, process));

        var terminationRequested = _requestedTerminations.TryRemove(process, out _);

        // Attach the stderr capture, when this manager started the process itself. This
        // is what makes an abort that outlived the detection window explicable: the exit
        // is already after "launched", so the event is the only place the evidence fits.
        string? stderrTail = null;
        IReadOnlyList<string> unmountableArchives = [];
        if (_stderrBuffers.TryRemove(process, out var capturedErrors))
        {
            DrainStandardError(capturedErrors);

            var tail = capturedErrors.ToString();
            stderrTail = string.IsNullOrWhiteSpace(tail) ? null : tail;
            unmountableArchives = ExtractUnmountableArchives(capturedErrors.Snapshot());
        }

        if (!terminationRequested && exitCode is int code && code != ProcessConstants.ExitCodeSuccess)
        {
            logger.LogWarning(
                "Process {ProcessId} exited with non-zero code {ExitCode} after the launch was reported as started. Archives: {Archives}. Output: {Output}",
                processId,
                code,
                unmountableArchives.Count > 0 ? string.Join(", ", unmountableArchives) : "none named",
                stderrTail ?? "No output was captured.");
        }

        // Raise the event
        var args = new GameProcessExitedEventArgs
        {
            ProcessId = processId,
            ProcessInstanceId = _exitFinalizations.GetValue(process, _ => new ExitFinalizationState()).InstanceId,
            ExitCode = exitCode,
            ExitTime = exitTime,
            StandardErrorTail = stderrTail,
            UnmountableArchives = unmountableArchives,
            TerminationRequested = terminationRequested,
        };

        // This is a process-event boundary: one subscriber must not prevent the
        // remaining subscribers (including termination completion) from observing exit.
        foreach (var subscriber in (ProcessExited?.GetInvocationList() ?? []).Cast<EventHandler<GameProcessExitedEventArgs>>())
        {
            try
            {
                subscriber(this, args);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Process-exit subscriber failed for process {ProcessId}", processId);
            }
        }

        // Explicit termination owns disposal until its wait and notification cleanup finish.
        // Natural exits have no remaining owner after removal from _managedProcesses.
        if (!terminationRequested)
        {
            SafeDisposeProcess(process, processId);
        }

        logger.LogInformation("Process {ProcessId} exited with code {ExitCode}", processId, exitCode);
    }

    /// <summary>
    /// Extracts the archive paths named by the engine's mount-failure stderr sentinels.
    /// </summary>
    /// <remarks>
    /// Strictly advisory. The sentinels are an external contract with the fork engine
    /// (see <see cref="RetailArchiveConstants.ArchiveIdentifierMismatchStderrPrefix"/>)
    /// and no other build emits them, so an empty result must never influence whether the
    /// launch is judged to have failed — it only leaves the generic stderr tail in place.
    /// </remarks>
    /// <param name="stderrLines">The captured stderr lines.</param>
    /// <returns>The distinct archives named, in order of first appearance.</returns>
    private static IReadOnlyList<string> ExtractUnmountableArchives(IReadOnlyList<string> stderrLines)
    {
        string[] sentinelPrefixes =
        [
            RetailArchiveConstants.ArchiveMountFailedStderrPrefix,
            RetailArchiveConstants.ArchiveIdentifierMismatchStderrPrefix,
        ];

        var archives = new List<string>();
        foreach (var line in stderrLines)
        {
            foreach (var prefix in sentinelPrefixes)
            {
                if (!line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var archive = line[prefix.Length..].Trim();
                if (archive.Length > 0 && !archives.Contains(archive))
                {
                    archives.Add(archive);
                }
            }
        }

        return archives;
    }

    /// <summary>
    /// Waits for the process to exit, up to the post-spawn detection window.
    /// </summary>
    /// <remarks>
    /// The window bounds how long a launch report can be delayed, not how long a failure
    /// can be detected: a process that outlives it is treated as launched, and any later
    /// abort surfaces through <see cref="ProcessExited"/>. Cancellation requested by the
    /// caller propagates; the window elapsing does not.
    /// </remarks>
    /// <param name="process">The just-started process.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns><c>true</c> when the process exited within the window.</returns>
    private static async Task<bool> WaitForExitWithinWindowAsync(Process process, CancellationToken cancellationToken)
    {
        using var windowCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        windowCts.CancelAfter(ProcessConstants.PostSpawnExitDetectionWindowMs);

        try
        {
            await process.WaitForExitAsync(windowCts.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Only the detection window elapsed; the launch itself is not cancelled.
        }
        finally
        {
            // WaitForExitAsync enables exit events internally. Disable them before the
            // final check so an exit after this check is delivered when the long-lived
            // handler is attached and events are re-enabled with tracking state ready.
            process.EnableRaisingEvents = false;
        }

        return process.HasExited;
    }

    private static void AddValidBindRoot(List<string> binds, string? dirPath)
    {
        if (string.IsNullOrWhiteSpace(dirPath))
        {
            return;
        }

        string fullRoot;
        try
        {
            fullRoot = Path.GetFullPath(dirPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return;
        }

        if (!Directory.Exists(fullRoot) || binds.Any(kept => kept.Equals(fullRoot, PathHelper.PathComparison) || PathHelper.IsPathWithinDirectory(kept, fullRoot)))
        {
            return;
        }

        binds.RemoveAll(kept => kept.Equals(fullRoot, PathHelper.PathComparison) || PathHelper.IsPathWithinDirectory(fullRoot, kept));
        binds.Add(fullRoot);
    }

    private static string? ResolveZeroHourTargetPath(
        string? workingDirectory,
        IReadOnlyDictionary<string, string> environment)
    {
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            return workingDirectory;
        }

        if (environment.TryGetValue(RetailArchiveConstants.GeneralsXZeroHourInstallPathVariable, out var zhPath)
            && !string.IsNullOrWhiteSpace(zhPath))
        {
            return zhPath;
        }

        if (environment.TryGetValue(RetailArchiveConstants.ZeroHourInstallPathVariable, out var zhInstallPath)
            && !string.IsNullOrWhiteSpace(zhInstallPath))
        {
            return zhInstallPath;
        }

        return null;
    }

    private static string? ResolveFlatpakEnvValue(
        string key,
        string defaultValue,
        bool isZeroHourFlatpak,
        string? workingDirectory,
        string? targetZhPath)
    {
        if (!isZeroHourFlatpak)
        {
            return defaultValue;
        }

        if (string.Equals(key, RetailArchiveConstants.GeneralsInstallPathVariable, StringComparison.Ordinal))
        {
            return !string.IsNullOrWhiteSpace(targetZhPath) ? targetZhPath : null;
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory) &&
            (string.Equals(key, RetailArchiveConstants.GeneralsXZeroHourInstallPathVariable, StringComparison.Ordinal) ||
             string.Equals(key, RetailArchiveConstants.GeneralsXGeneralsInstallPathVariable, StringComparison.Ordinal)))
        {
            return workingDirectory;
        }

        return defaultValue;
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
    /// <returns><c>true</c> on Windows, for Windows binaries, or when any execute bit is set.</returns>
    private static bool HasExecutePermission(string path)
    {
        if (OperatingSystem.IsWindows() ||
            CommandLineHelper.IsWindowsExecutable(path))
        {
            return true;
        }

        // Wine reads the file instead of executing it, so a Windows binary without
        // the .exe extension needs no execute bit either. Only on-disk files with
        // PE headers qualify: by name alone nothing but .exe reads as Windows.
        if (!OperatingSystem.IsWindows()
            && ExecutableFileClassifier.DetectPlatform(path) == ExecutablePlatform.Windows)
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
        catch (IOException)
        {
            // Unreadable metadata should not block a launch that might otherwise work.
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Unreadable metadata should not block a launch that might otherwise work.
            return true;
        }
        catch (PlatformNotSupportedException)
        {
            // Unreadable metadata should not block a launch that might otherwise work.
            return true;
        }
    }

    private static bool IsFlatpakLaunch(GameLaunchConfiguration configuration)
    {
        return OperatingSystem.IsLinux()
            && configuration.ExecutablePath.EndsWith(ContentFormatConstants.FlatpakExtension, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWineLaunch(IGameLaunchRunner runner, RunnerCommand runnerCommand)
    {
        if (runner is WineRunner)
        {
            return true;
        }

        if (runnerCommand.EnvironmentVariables != null &&
            runnerCommand.EnvironmentVariables.ContainsKey(WineConstants.PrefixEnvironmentVariable))
        {
            return true;
        }

        var fileName = Path.GetFileName(runnerCommand.FileName);
        return fileName.Equals(WineConstants.WineBinaryName, StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals(WineConstants.Wine64BinaryName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads a process's start time in UTC, or reports that it could not be read.
    /// </summary>
    /// <param name="process">The process to inspect.</param>
    /// <returns>The start time, or <see langword="null"/> when the platform will not report it.</returns>
    private DateTime? ReadStartTime(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[Process] Unable to inspect start time for process {ProcessId}", process.Id);
            return null;
        }
    }

    private async Task<OperationResult<RunnerCommand>> ResolveFlatpakCommandAsync(
        GameLaunchConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var provision = await flatpakProvisioner.EnsureInstalledAsync(configuration.ExecutablePath, cancellationToken);
        if (!provision.Success || string.IsNullOrWhiteSpace(provision.Data))
        {
            return OperationResult<RunnerCommand>.CreateFailure(provision.FirstError!);
        }

        var appId = provision.Data;
        logger.LogInformation("[Process] Launching Flatpak app {AppId} from {BundlePath}", appId, configuration.ExecutablePath);
        var binds = ResolveFlatpakFilesystemBinds(configuration.EnvironmentVariables, configuration.WorkingDirectory, configuration.NativeOptionsIniPath);
        if (binds.Count > 0)
        {
            logger.LogInformation("[Process] Exposing {Count} game data directories to Flatpak sandbox: {Directories}", binds.Count, string.Join(", ", binds));
        }

        var envArgs = ResolveFlatpakEnvironmentArguments(appId, configuration.EnvironmentVariables, configuration.WorkingDirectory);
        if (envArgs.Count > 0)
        {
            logger.LogDebug("[Process] Forwarding {Count} environment variables to Flatpak sandbox", envArgs.Count);
        }

        var filesystemArguments = string.Join(" ", binds.Select(bind => CommandLineHelper.QuoteArgument($"{ContentFormatConstants.FlatpakFilesystemOptionPrefix}{bind}")));
        var envArguments = string.Join(" ", envArgs.Select(CommandLineHelper.QuoteArgument));

        var prefixParts = new List<string>
        {
            ContentFormatConstants.FlatpakRunCommand,
            ContentFormatConstants.FlatpakUserFlag,
        };

        if (!string.IsNullOrEmpty(filesystemArguments))
        {
            prefixParts.Add(filesystemArguments);
        }

        if (!string.IsNullOrEmpty(envArguments))
        {
            prefixParts.Add(envArguments);
        }

        prefixParts.Add(appId);

        var argumentPrefix = string.Join(" ", prefixParts);
        return OperationResult<RunnerCommand>.CreateSuccess(
            new RunnerCommand(
                ContentFormatConstants.FlatpakBinaryName,
                argumentPrefix,
                new Dictionary<string, string>()));
    }

    private OperationResult<bool> ValidateLaunchConfiguration(GameLaunchConfiguration? configuration)
    {
        if (configuration == null)
        {
            logger.LogError("GameLaunchConfiguration is null");
            return OperationResult<bool>.CreateFailure("Configuration cannot be null");
        }

        if (string.IsNullOrEmpty(configuration.ExecutablePath))
        {
            logger.LogError("ExecutablePath is null or empty in configuration");
            return OperationResult<bool>.CreateFailure("ExecutablePath cannot be null or empty");
        }

        if (!File.Exists(configuration.ExecutablePath))
        {
            logger.LogError("Executable not found at path: {ExecutablePath}", configuration.ExecutablePath);
            return OperationResult<bool>.CreateFailure($"Executable not found: {configuration.ExecutablePath}");
        }

        // Flatpak bundles are read by the Flatpak CLI during provisioning, never executed
        // directly, so the execute bit is not required for them.
        if (!OperatingSystem.IsWindows()
            && !HasExecutePermission(configuration.ExecutablePath)
            && !configuration.ExecutablePath.EndsWith(ContentFormatConstants.FlatpakExtension, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("[Process] Executable is not marked executable: {ExecutablePath}", configuration.ExecutablePath);
            return OperationResult<bool>.CreateFailure(
                $"'{configuration.ExecutablePath}' does not have the execute permission set, so it cannot be launched.");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private OperationResult<Process> StartNativeProcess(ProcessStartInfo processStartInfo, string executablePath)
    {
        try
        {
            var process = Process.Start(processStartInfo);
            if (process == null)
            {
                logger.LogError("[Process] Process.Start returned null for executable: {ExecutablePath}", executablePath);
                return OperationResult<Process>.CreateFailure("Failed to start process - Process.Start returned null");
            }

            return OperationResult<Process>.CreateSuccess(process);
        }
        catch (Win32Exception win32Ex)
        {
            logger.LogError(
                win32Ex,
                "Win32Exception starting process {ExecutablePath}: {ErrorCode} - {Message}",
                executablePath,
                win32Ex.NativeErrorCode,
                win32Ex.Message);
            return OperationResult<Process>.CreateFailure($"Failed to start process (Win32 Error {win32Ex.NativeErrorCode}): {win32Ex.Message}");
        }
        catch (InvalidOperationException invOpEx)
        {
            logger.LogError(
                invOpEx,
                "InvalidOperationException starting process {ExecutablePath}: {Message}",
                executablePath,
                invOpEx.Message);
            return OperationResult<Process>.CreateFailure($"Failed to start process (Invalid Operation): {invOpEx.Message}");
        }
    }

    private BoundedErrorBuffer SetupErrorRedirection(Process process)
    {
        var capturedErrors = new BoundedErrorBuffer();
        process.ErrorDataReceived += (_, e) => capturedErrors.Append(e.Data);
        try
        {
            process.BeginErrorReadLine();
        }
        catch (InvalidOperationException ex)
        {
            capturedErrors.Append(null);
            logger.LogDebug(ex, "[Process] Could not capture stderr for process {ProcessId}", process.Id);
        }

        return capturedErrors;
    }

    private void RegisterProcessEventHandlers(Process process)
    {
        var processId = process.Id;
        try
        {
            process.Exited += OnProcessExited;
            process.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enable raising events for process {ProcessId}, process cleanup may not work properly", processId);
        }
    }

    private async Task HandleProcessCancellationAsync(Process? process, string executablePath)
    {
        logger.LogInformation("Start of {ExecutablePath} was cancelled", executablePath);
        if (process != null)
        {
            try
            {
                await Task.Run(
                    () =>
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill(entireProcessTree: true);
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            // Process already exited or was disposed
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to terminate process on cancellation");
                        }
                        finally
                        {
                            try
                            {
                                process.Dispose();
                            }
                            catch
                            {
                                // Ignore disposal errors
                            }
                        }
                    },
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to complete process cancellation task");
            }
        }
    }

    private string GetMissingRunnerMessage()
    {
        return localizationService.TryGetString(ProfileValidationConstants.MissingCompatibilityRunnerKey, out var localized)
            ? localized
            : ProfileValidationConstants.MissingCompatibilityRunner;
    }

    private void AppendFormattedArgument(List<string> argList, KeyValuePair<string, string> arg)
    {
        if (arg.Key.StartsWith('-'))
        {
            argList.Add(arg.Key);
            if (!string.IsNullOrEmpty(arg.Value))
            {
                argList.Add(CommandLineHelper.QuoteArgument(arg.Value));
            }

            logger.LogDebug("Added flag argument: {Key} {Value}", arg.Key, arg.Value);
        }
        else if (arg.Key.StartsWith("_pos", StringComparison.Ordinal) || string.IsNullOrEmpty(arg.Key))
        {
            var quotedValue = CommandLineHelper.QuoteArgument(arg.Value);
            argList.Add(quotedValue);
            logger.LogDebug("Added positional argument: {Value}", quotedValue);
        }
        else
        {
            var quotedValue = CommandLineHelper.QuoteArgument(arg.Value);
            argList.Add($"{arg.Key}={quotedValue}");
            logger.LogDebug("Added key-value argument: {Key}={Value}", arg.Key, quotedValue);
        }
    }

    private void ApplyEnvironmentVariables(
        ProcessStartInfo processStartInfo,
        IEnumerable<KeyValuePair<string, string>> environmentVariables,
        string sourceLabel)
    {
        foreach (var (key, value) in environmentVariables)
        {
            processStartInfo.EnvironmentVariables[key] = value;
            logger.LogDebug("[Process] Set {Source} environment variable: {Key}={Value}", sourceLabel, key, value);
        }
    }

    private void AppendConfigurationArguments(List<string> argList, IReadOnlyDictionary<string, string>? arguments)
    {
        if (arguments is not { Count: > 0 })
        {
            return;
        }

        logger.LogDebug("[Process] Adding {ArgumentCount} arguments to process", arguments.Count);
        foreach (var arg in arguments)
        {
            AppendFormattedArgument(argList, arg);
        }
    }

    private ProcessStartInfo ConfigureProcessStartInfo(GameLaunchConfiguration configuration, string workingDirectory, RunnerCommand runnerCommand)
    {
        var processStartInfo = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            FileName = runnerCommand.FileName,
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardError = true,
        };

        ApplyEnvironmentVariables(processStartInfo, runnerCommand.EnvironmentVariables, "runner");

        var argList = new List<string>();
        if (!string.IsNullOrEmpty(runnerCommand.ArgumentPrefix))
        {
            argList.Add(runnerCommand.ArgumentPrefix);
        }

        AppendConfigurationArguments(argList, configuration.Arguments);

        if (argList.Count > 0)
        {
            processStartInfo.Arguments = string.Join(" ", argList);
        }

        if (configuration.EnvironmentVariables is { Count: > 0 } envVars)
        {
            logger.LogDebug("[Process] Setting {Count} environment variables", envVars.Count);
            ApplyEnvironmentVariables(processStartInfo, envVars, "configuration");
        }

        return processStartInfo;
    }

    private async Task<OperationResult<GameProcessInfo>> HandleImmediateProcessExitAsync(
        Process process,
        GameLaunchConfiguration configuration,
        DateTime? launcherStartTime,
        BoundedErrorBuffer capturedErrors,
        CancellationToken cancellationToken)
    {
        // Adoption is not gated on Windows: a Wine or Proton wrapper forks and exits the same way,
        // and adoption only accepts a candidate that carries the name, started at or after this
        // launcher, is inside the recency window, and runs from the workspace directory. If the
        // engine really did exit, nothing satisfies that and the launch still fails loudly.
        if (process.ExitCode == ProcessConstants.ExitCodeSuccess)
        {
            logger.LogInformation(
                "[Process] Launcher process {ProcessId} exited with code 0 - attempting to find spawned game process",
                process.Id);

            var executableName = !string.IsNullOrWhiteSpace(configuration.ExpectedChildProcessName)
                ? configuration.ExpectedChildProcessName
                : Path.GetFileNameWithoutExtension(configuration.ExecutablePath);

            var spawnedProcess = await PollForSpawnedGameProcessAsync(configuration, executableName, launcherStartTime, cancellationToken);
            if (spawnedProcess != null)
            {
                var spawnedProcessInfo = AdoptSpawnedProcess(process, spawnedProcess, configuration, executableName);
                return OperationResult<GameProcessInfo>.CreateSuccess(spawnedProcessInfo);
            }
        }

        return HandleFailedProcessExit(process, capturedErrors);
    }

    private async Task<Process?> PollForSpawnedGameProcessAsync(
        GameLaunchConfiguration configuration,
        string executableName,
        DateTime? launcherStartTime,
        CancellationToken cancellationToken)
    {
        var workingDir = configuration.WorkingDirectory ?? Path.GetDirectoryName(configuration.ExecutablePath) ?? string.Empty;
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(ProcessConstants.LauncherExitGracePeriodMs);

        Process? spawnedProcess = null;
        while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            spawnedProcess = FindAdoptableGameProcess(executableName, workingDir, launcherStartTime);
            if (spawnedProcess != null)
            {
                break;
            }

            await Task.Delay(ProcessConstants.SpawnedChildPollIntervalMs, cancellationToken);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            if (spawnedProcess != null)
            {
                CleanupSpawnedProcessUponCancellation(spawnedProcess);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        return spawnedProcess;
    }

    private void CleanupSpawnedProcessUponCancellation(Process spawnedProcess)
    {
        _ = Task.Run(() =>
        {
            try
            {
                if (!spawnedProcess.HasExited)
                {
                    spawnedProcess.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "[Process] Ignored exception while terminating adopted process upon cancellation");
            }
            finally
            {
                spawnedProcess.Dispose();
            }
        });
    }

    private GameProcessInfo AdoptSpawnedProcess(
        Process launcherProcess,
        Process spawnedProcess,
        GameLaunchConfiguration configuration,
        string executableName)
    {
        logger.LogInformation(
            "[Process] Found spawned game process {ProcessId} for executable {ExecutableName}",
            spawnedProcess.Id,
            executableName);

        _stderrBuffers.TryRemove(launcherProcess, out _);
        launcherProcess.Dispose();
        _managedProcesses[spawnedProcess.Id] = spawnedProcess;

        var spawnedProcessInfo = BuildProcessInfo(spawnedProcess, configuration.ExecutablePath);
        RegisterProcessEventHandlers(spawnedProcess);
        logger.LogInformation("Started game process {ProcessId} for executable {ExecutablePath}", spawnedProcessInfo.ProcessId, configuration.ExecutablePath);
        return spawnedProcessInfo;
    }

    private void TryRemoveProcessErrors(Process? process)
    {
        if (process == null)
        {
            return;
        }

        try
        {
            _stderrBuffers.TryRemove(process, out _);
        }
        catch (InvalidOperationException)
        {
            // Process was never started or has no associated system process
        }
    }

    private OperationResult<GameProcessInfo> HandleFailedProcessExit(
        Process process,
        BoundedErrorBuffer capturedErrors)
    {
        var exitCode = process.ExitCode;
        logger.LogWarning("Process {ProcessId} exited immediately with code {ExitCode}", process.Id, exitCode);

        _stderrBuffers.TryRemove(process, out _);
        DrainStandardError(capturedErrors);
        process.Dispose();

        var stderrTail = capturedErrors.ToString();
        if (exitCode != 0)
        {
            var unmountableArchives = ExtractUnmountableArchives(capturedErrors.Snapshot());
            if (unmountableArchives.Count > 0)
            {
                var archiveNames = string.Join(", ", unmountableArchives);
                logger.LogError(
                    "[Process] Process exited during startup with code {ExitCode} after failing to mount archive(s): {Archives}",
                    exitCode,
                    archiveNames);
                return OperationResult<GameProcessInfo>.CreateFailure(
                    localizationService.GetString("GameProfiles.Notification.UnexpectedExit.Archives", archiveNames, exitCode));
            }

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

        var suffix = string.IsNullOrWhiteSpace(stderrTail) ? string.Empty : $" {stderrTail}";
        logger.LogError(
            "[Process] Process exited immediately with code 0 and no spawned process was found. Output: {Output}",
            string.IsNullOrWhiteSpace(stderrTail) ? "No output was captured." : stderrTail);

        return OperationResult<GameProcessInfo>.CreateFailure(
            $"Process exited immediately after launch.{suffix}");
    }

    /// <summary>
    /// Waits for a launcher to spawn the process named by
    /// <see cref="GameLaunchConfiguration.ExpectedChildProcessName"/> and tracks that process
    /// instead of the launcher. The launcher's own exit is never treated as the game exiting.
    /// </summary>
    /// <param name="launcher">The process that was started.</param>
    /// <param name="configuration">The launch configuration.</param>
    /// <param name="workingDirectory">The directory the game must run from.</param>
    /// <param name="launcherStartTime">The launcher's start time, read while it was still running.</param>
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
        DateTime? launcherStartTime,
        BoundedErrorBuffer capturedErrors,
        CancellationToken cancellationToken)
    {
        var expectedName = configuration.ExpectedChildProcessName;
        if (string.IsNullOrWhiteSpace(expectedName))
        {
            return OperationResult<GameProcessInfo>.CreateFailure("Expected child process name is not specified.");
        }

        var timeout = configuration.ExpectedChildDiscoveryTimeout
            ?? TimeSpan.FromMilliseconds(ProcessConstants.SpawnedChildDiscoveryTimeoutMs);
        var deadline = DateTime.UtcNow + timeout;
        var gracePeriod = TimeSpan.FromMilliseconds(ProcessConstants.LauncherExitGracePeriodMs);
        DateTime? launcherExitedAt = null;

        try
        {
            // Adoption requires the launcher's start time to rule out an instance of the game the
            // user already had running, so without it no candidate can ever qualify. Polling that
            // out would repeat the refusal once per interval and then report a discovery timeout,
            // which describes a launcher that was never given the chance to fail.
            if (!launcherStartTime.HasValue)
            {
                logger.LogError(
                    "[Process] Not waiting for {ExpectedName}: the launcher's start time is unknown, so a process that predates this launch cannot be ruled out",
                    expectedName);

                await TerminateAbandonedLauncherAsync(launcher);

                // Terminated first, so the launcher has exited and its stderr drains in full.
                return OperationResult<GameProcessInfo>.CreateFailure(
                    AppendLauncherErrors(
                        $"Launcher exited without starting {expectedName}: the launcher's start time could not be read.",
                        launcher,
                        capturedErrors));
            }

            logger.LogInformation(
                "[Process] Waiting up to {TimeoutMs}ms for launcher {LauncherId} to start {ExpectedName}",
                (int)timeout.TotalMilliseconds,
                launcher.Id,
                expectedName);

            while (true)
            {
                var child = FindAdoptableGameProcess(expectedName, workingDirectory, launcherStartTime);
                if (child != null)
                {
                    _managedProcesses[child.Id] = child;

                    var childInfo = BuildProcessInfo(child, configuration.ExecutablePath);
                    RegisterProcessEventHandlers(child);

                    logger.LogInformation(
                        "[Process] Adopted game process {ProcessId} ({ExpectedName}); launcher {LauncherId} is no longer tracked and its exit is ignored",
                        childInfo.ProcessId,
                        expectedName,
                        launcher.Id);

                    return OperationResult<GameProcessInfo>.CreateSuccess(childInfo);
                }

                var (launcherExited, launcherExitCode) = ReadLauncherExit(launcher);

                // A launcher that fails outright will never produce a child - do not wait it out.
                if (launcherExited && launcherExitCode is int exitCode && exitCode != ProcessConstants.ExitCodeSuccess)
                {
                    logger.LogError(
                        "[Process] Launcher {LauncherId} exited with code {ExitCode} before starting {ExpectedName}",
                        launcher.Id,
                        exitCode,
                        expectedName);
                    return OperationResult<GameProcessInfo>.CreateFailure(
                        AppendLauncherErrors(
                            $"Launcher exited with code {exitCode} before starting {expectedName}.",
                            launcher,
                            capturedErrors));
                }

                // A clean exit with no child is still a failure - the bootstrapper bailing without
                // launching the game looks identical to success from the exit code alone. Allow a
                // short grace period for the spawn-then-enumerate race, then stop: once the
                // launcher is gone a child will not appear, and waiting out the full discovery
                // timeout only delays the failure and reports a misleading timeout as the cause.
                if (launcherExited)
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
                    await TerminateAbandonedLauncherAsync(launcher);
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

            await TerminateAbandonedLauncherAsync(launcher);
            throw;
        }
        finally
        {
            TryRemoveProcessErrors(launcher);

            // Releases our handle only; the launcher keeps running and owns its own lifetime.
            launcher.Dispose();
        }
    }

    /// <summary>
    /// Kills a launcher whose child was never adopted. Without this a cancelled launch leaves the
    /// bootstrapper running with no tracked process and no handle for the caller to reach it.
    /// </summary>
    /// <param name="launcher">The launcher to terminate.</param>
    private async Task TerminateAbandonedLauncherAsync(Process launcher)
    {
        try
        {
            if (launcher.HasExited)
            {
                return;
            }

            await Task.Run(
                () =>
                {
                    try
                    {
                        if (!launcher.HasExited)
                        {
                            launcher.Kill(entireProcessTree: true);
                            launcher.WaitForExit(ProcessConstants.AbandonedLauncherKillWaitMs);
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Process already exited or disposed
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "[Process] Failed to terminate abandoned launcher {LauncherId}", launcher.Id);
                    }
                },
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Process] Failed to dispatch termination for abandoned launcher {LauncherId}", launcher.Id);
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
        var instanceId = _exitFinalizations.GetValue(process, _ => new ExitFinalizationState()).InstanceId;
        var processId = 0;
        try
        {
            processId = process.Id;
            var inspectedPath = GetProcessExecutablePath(process);
            return new GameProcessInfo
            {
                ProcessId = processId,
                ProcessInstanceId = instanceId,
                ProcessName = process.ProcessName,
                StartTime = process.StartTime.ToUniversalTime(),
                HasVerifiedStartTime = true,
                ExecutablePath = string.IsNullOrEmpty(inspectedPath) ? fallbackExecutablePath : inspectedPath,
                IsRunning = IsStillRunning(process),
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get process information for {ProcessId}, using minimal info", processId);
            return new GameProcessInfo
            {
                ProcessId = processId,
                ProcessInstanceId = instanceId,
                ProcessName = GameClientConstants.UnknownVersion,
                StartTime = DateTime.UtcNow,
                ExecutablePath = fallbackExecutablePath,
                IsRunning = IsStillRunning(process),
            };
        }
    }

    /// <summary>
    /// Finds a game process by executable name and working directory, without a launcher to bound
    /// the search. Used when discovering a game a storefront started on our behalf.
    /// </summary>
    /// <param name="executableName">The base executable name without extension.</param>
    /// <param name="workingDirectory">The expected working directory.</param>
    /// <returns>The discovered process if found, null otherwise.</returns>
    private Process? FindSpawnedGameProcess(string executableName, string workingDirectory) =>
        FindGameProcess(
            executableName,
            candidates => GameProcessSelector.SelectSpawnedGameProcess(
                candidates, executableName, workingDirectory, DateTime.UtcNow));

    /// <summary>
    /// Finds the process a launcher spawned, to be tracked and terminated in the launcher's place.
    /// </summary>
    /// <param name="executableName">The base executable name without extension.</param>
    /// <param name="workingDirectory">The expected working directory.</param>
    /// <param name="launcherStartTime">The start time of the launcher process, if known.</param>
    /// <returns>The process to adopt if one qualifies, null otherwise.</returns>
    private Process? FindAdoptableGameProcess(string executableName, string workingDirectory, DateTime? launcherStartTime)
    {
        if (!launcherStartTime.HasValue)
        {
            logger.LogWarning(
                "[Process] Not adopting a running {ExecutableName}: the launcher's start time is unknown, so a process that predates this launch cannot be ruled out",
                executableName);
            return null;
        }

        return FindGameProcess(
            executableName,
            candidates => GameProcessSelector.SelectAdoptableGameProcess(
                candidates, executableName, workingDirectory, launcherStartTime.Value.ToUniversalTime()));
    }

    /// <summary>
    /// Enumerates the processes that could carry <paramref name="executableName"/> and hands them
    /// to a selection policy.
    /// </summary>
    /// <param name="executableName">The base executable name without extension.</param>
    /// <param name="select">The policy deciding which candidate, if any, is ours.</param>
    /// <returns>The selected process if found, null otherwise.</returns>
    private Process? FindGameProcess(string executableName, Func<List<GameProcessCandidate>, GameProcessCandidate?> select)
    {
        Process[] processes = [];
        try
        {
            processes = Process.GetProcessesByName(GameProcessSelector.GetDiscoveryName(executableName));
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
                        process.StartTime.ToUniversalTime(),
                        string.IsNullOrEmpty(executablePath) ? null : executablePath));
                }
                catch (Exception ex)
                {
                    // A process that cannot be inspected cannot be shown to be ours.
                    logger.LogDebug(ex, "Skipping uninspectable process {ProcessId}", process.Id);
                }
            }

            var selected = select(candidates);

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
    /// Reads a launcher's exit state without throwing.
    /// </summary>
    /// <param name="launcher">The launcher to inspect.</param>
    /// <returns>
    /// Whether the launcher has exited, and its exit code when that could be read.
    /// </returns>
    /// <remarks>
    /// <see cref="Process.HasExited"/> and <see cref="Process.ExitCode"/> throw
    /// <see cref="InvalidOperationException"/> with no handle and
    /// <see cref="System.ComponentModel.Win32Exception"/> when the code cannot be read — both
    /// plausible for the hard-crashing launcher this loop exists to report on. Letting either
    /// escape would replace the launcher diagnosis with a generic start failure.
    /// An unreadable state is reported as still running, so the loop keeps polling to its
    /// deadline rather than concluding anything from a failed probe.
    /// </remarks>
    private (bool Exited, int? ExitCode) ReadLauncherExit(Process launcher)
    {
        try
        {
            launcher.Refresh();
            if (!launcher.HasExited)
            {
                return (false, null);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[Process] Could not determine whether the launcher had exited");
            return (false, null);
        }

        try
        {
            return (true, launcher.ExitCode);
        }
        catch (Exception ex)
        {
            // Exited, but the code is unavailable. The clean-exit path still applies.
            logger.LogDebug(ex, "[Process] Could not read the launcher's exit code");
            return (true, null);
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
            DrainStandardError(capturedErrors);
        }

        var detail = capturedErrors.ToString();

        return string.IsNullOrWhiteSpace(detail) ? message : $"{message} {detail}";
    }

    /// <summary>Releases termination-owned state while retaining processes whose stop failed.</summary>
    /// <param name="process">The process inspected during termination.</param>
    /// <param name="ownsProcess">Whether termination opened the process handle.</param>
    /// <param name="terminated">Whether termination completed successfully.</param>
    private void CleanupTerminationProcess(Process? process, bool ownsProcess, bool terminated)
    {
        if (process is not null && (!terminated || ownsProcess))
        {
            _requestedTerminations.TryRemove(process, out _);
        }

        if (ownsProcess || terminated)
        {
            process?.Dispose();
        }
    }

    private void SafeDisposeProcess(Process process, int processId)
    {
        // Broad by intent: failure in event cleanup must not skip handle disposal,
        // and neither cleanup operation may escape a process-exit callback.
        try
        {
            process.Exited -= OnProcessExited;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to detach exit handler for process {ProcessId}", processId);
        }

        try
        {
            process.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to dispose process {ProcessId}", processId);
        }
    }

    /// <summary>
    /// Waits for the asynchronous stderr handlers to finish before the capture is read.
    /// </summary>
    /// <remarks>
    /// Process exit does not guarantee delivery of the final redirected lines. Wait for
    /// the buffer's end-of-stream signal, bounded because descendants may retain the pipe.
    /// </remarks>
    /// <param name="capturedErrors">The buffer receiving stderr lines.</param>
    private void DrainStandardError(BoundedErrorBuffer capturedErrors)
    {
        try
        {
            capturedErrors.WaitForEndOfStream(ProcessConstants.StderrDrainTimeoutMs);
        }
        catch (Exception ex)
        {
            // Preserve the available capture if waiting is interrupted; diagnostics
            // must not prevent process-exit cleanup.
            logger.LogDebug(ex, "[Process] Could not wait for stderr handlers to complete");
        }

        if (!capturedErrors.EndOfStreamReached)
        {
            logger.LogDebug(
                "[Process] Standard error did not signal end of stream; the captured output may be incomplete");
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
        /// Returns the retained lines, head first, for line-oriented matching.
        /// </summary>
        /// <remarks>
        /// <see cref="ToString"/> joins lines for display; matching against that joined
        /// form would let one line's content bleed into the next. Lines dropped by the
        /// bounds are gone from here too, which is acceptable for an advisory match.
        /// </remarks>
        /// <returns>A snapshot of the retained lines.</returns>
        internal IReadOnlyList<string> Snapshot()
        {
            lock (_gate)
            {
                return [.. _head, .. _tail];
            }
        }

        /// <summary>Waits for redirected output completion without an unbounded pipe wait.</summary>
        /// <param name="timeoutMs">The maximum wait in milliseconds.</param>
        internal void WaitForEndOfStream(int timeoutMs)
        {
            var elapsed = Stopwatch.StartNew();
            lock (_gate)
            {
                while (!_endOfStream)
                {
                    var remaining = timeoutMs - (int)elapsed.ElapsedMilliseconds;
                    if (remaining <= 0)
                    {
                        break;
                    }

                    Monitor.Wait(_gate, remaining);
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
                    Monitor.PulseAll(_gate);
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
