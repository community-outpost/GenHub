using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launcher;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameSettings;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Launching;

/// <summary>
/// Service for launching games from prepared workspaces.
/// </summary>
public class GameLauncher(
    ILogger<GameLauncher> logger,
    IGameProfileManager profileManager,
    IWorkspaceManager workspaceManager,
    IGameProcessManager processManager,
    IContentManifestPool manifestPool,
    IDependencyResolver dependencyResolver,
    ILaunchRegistry launchRegistry,
    IGameInstallationService gameInstallationService,
    ICasService casService,
    IStorageLocationService storageLocationService,
    IGameSettingsService gameSettingsService,
    IProfileContentLinker profileContentLinker,
    ISteamLauncher steamLauncher,
    IConfigurationProviderService configurationProvider) : IGameLauncher
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _profileLaunchLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _steamInstallationLaunchLocks =
        new(InstallationPathLockKey.Comparer);

    private static readonly SearchValues<char> InvalidArgChars = SearchValues.Create(";|&\n\r`$%");

    /// <inheritdoc/>
    public async Task<IDisposable> AcquireProfileLockAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var semaphore = _profileLaunchLocks.GetOrAdd(profileId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new SemaphoreReleaser(semaphore);
    }

    private async Task<IDisposable> AcquireSteamInstallationLockAsync(
        string installationPath,
        CancellationToken cancellationToken)
    {
        var normalizedPath = InstallationPathLockKey.Create(installationPath, logger);
        var semaphore = _steamInstallationLaunchLocks.GetOrAdd(
            normalizedPath,
            _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new SemaphoreReleaser(semaphore);
    }

    /// <summary>
    /// Helper class to release semaphore when disposed.
    /// </summary>
    private sealed class SemaphoreReleaser(SemaphoreSlim semaphore) : IDisposable
    {
        private readonly SemaphoreSlim _semaphore = semaphore;
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _semaphore.Release();
                _disposed = true;
            }
        }
    }

    private readonly record struct WorkspaceSetupContext(
        string ActualInstallationPath,
        string DynamicWorkspacePath,
        bool IsSteamLaunch,
        Dictionary<string, string> ManifestSourcePaths);

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
                            Arguments = config.Arguments != null
                                ? string.Join(" ", config.Arguments.Select(kvp =>
                                    string.IsNullOrEmpty(kvp.Value)
                                        ? kvp.Key
                                        : $"{kvp.Key} {(kvp.Value.Contains(' ') ? $"\"{kvp.Value}\"" : kvp.Value)}"))
                                : string.Empty,
                            UseShellExecute = false,
                        },
                    };
                    if (!process.Start())
                        return LaunchResult.CreateFailure("Failed to start process", null);
                    var launchDuration = DateTime.UtcNow - startTime;
                    return LaunchResult.CreateSuccess(process.Id, process.StartTime, launchDuration);
                },
                cancellationToken);
        }
        catch (Exception ex)
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
                },
                cancellationToken);
        }
        catch (Exception ex)
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
            process.CloseMainWindow();

            // Wait for process to exit with polling (max 5 seconds)
            // This prevents blocking the UI thread for the full timeout period
            const int maxWaitMs = 5000;
            const int pollIntervalMs = 100;
            int elapsedMs = 0;

            while (!process.HasExited && elapsedMs < maxWaitMs)
            {
                await Task.Delay(pollIntervalMs, cancellationToken);
                elapsedMs += pollIntervalMs;
            }

            // Force kill if still running after timeout
            if (!process.HasExited)
            {
                logger.LogWarning("Process {ProcessId} did not exit gracefully after {Timeout}ms, forcing termination", processId, maxWaitMs);
                process.Kill();
            }

            return true;
        }
        catch (Exception ex)
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
    /// <param name="skipUserDataCleanup">Whether to skip cleanup of user data files (maps, etc.) from other profiles.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="LaunchOperationResult{T}"/> representing the result of the launch operation.</returns>
    public async Task<LaunchOperationResult<GameLaunchInfo>> LaunchProfileAsync(string profileId, IProgress<LaunchProgress>? progress = null, bool skipUserDataCleanup = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        // Report initial progress
        progress?.Report(new LaunchProgress { Phase = LaunchPhase.ValidatingProfile, PercentComplete = 0 });

        // Get the profile
        var profileResult = await profileManager.GetProfileAsync(profileId, cancellationToken);
        if (!profileResult.Success)
        {
            return LaunchOperationResult<GameLaunchInfo>.CreateFailure(profileResult.FirstError ?? "Unknown error accessing profile", profileId: profileId);
        }

        var profile = profileResult.Data;
        return await LaunchProfileAsync(profile, progress, skipUserDataCleanup, cancellationToken);
    }

    /// <summary>
    /// Launches a game using the provided game profile object.
    /// </summary>
    /// <param name="profile">The game profile to launch.</param>
    /// <param name="progress">Optional progress reporter for launch progress.</param>
    /// <param name="skipUserDataCleanup">Whether to skip cleanup of user data files (maps, etc.) from other profiles.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="LaunchOperationResult{T}"/> representing the result of the launch operation.</returns>
    public async Task<LaunchOperationResult<GameLaunchInfo>> LaunchProfileAsync(GameProfile profile, IProgress<LaunchProgress>? progress = null, bool skipUserDataCleanup = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // Use profile-specific semaphore to prevent race conditions
        var semaphore = _profileLaunchLocks.GetOrAdd(profile.Id, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            // Check if already launching (inside the semaphore to prevent race)
            var existingLaunches = await launchRegistry.GetAllActiveLaunchesAsync();
            var activeLaunch = existingLaunches.FirstOrDefault(l => l.ProfileId == profile.Id && !l.TerminatedAt.HasValue);
            if (activeLaunch != null)
            {
                // Double-check if the process is actually still running
                var processInfo = await processManager.GetProcessInfoAsync(activeLaunch.ProcessInfo.ProcessId, cancellationToken);
                if (processInfo.Success && processInfo.Data != null)
                {
                    // Process is actually running, prevent duplicate launch
                    return LaunchOperationResult<GameLaunchInfo>.CreateFailure($"Profile {profile.Id} is already launching or running");
                }

                // Process is not running but launch record exists - clean it up
                logger.LogWarning(
                    "Launch record {LaunchId} for profile {ProfileId} exists but process {ProcessId} is not running - cleaning up",
                    activeLaunch.LaunchId,
                    profile.Id,
                    activeLaunch.ProcessInfo.ProcessId);
                activeLaunch.TerminatedAt = DateTime.UtcNow;
                await launchRegistry.UnregisterLaunchAsync(activeLaunch.LaunchId);
            }

            // Proceed with launch
            var launchId = Guid.NewGuid().ToString();

            // Register placeholder launch entry before starting to prevent deletion during launch
            // This ensures DeleteProfileAsync will see an active launch and block deletion
            var placeholderLaunchInfo = new GameLaunchInfo
            {
                LaunchId = launchId,
                ProfileId = profile.Id,
                WorkspaceId = string.Empty, // Will be set later when workspace is ready
                ProcessInfo = new GameProcessInfo { ProcessId = -1 }, // Placeholder PID
                LaunchedAt = DateTime.UtcNow,
            };
            await launchRegistry.RegisterLaunchAsync(placeholderLaunchInfo);
            logger.LogDebug("Registered placeholder launch {LaunchId} for profile {ProfileId} to prevent deletion during launch", launchId, profile.Id);
            return await LaunchProfileAsync(profile, skipUserDataCleanup, progress, launchId, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Gets a list of all active game processes managed by the launcher.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="LaunchOperationResult{T}"/> containing the list of active game processes.</returns>
    public async Task<LaunchOperationResult<IReadOnlyList<GameProcessInfo>>> GetActiveGamesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await processManager.GetActiveProcessesAsync(cancellationToken);
            if (!result.Success)
            {
                return LaunchOperationResult<IReadOnlyList<GameProcessInfo>>.CreateFailure(result.FirstError ?? "Unknown error retrieving processes");
            }

            return LaunchOperationResult<IReadOnlyList<GameProcessInfo>>.CreateSuccess(result.Data);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get active games");
            return LaunchOperationResult<IReadOnlyList<GameProcessInfo>>.CreateFailure($"Failed to get active games: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets information about a specific game process by its launch ID.
    /// </summary>
    /// <param name="launchId">The launch ID of the game process.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="LaunchOperationResult{GameProcessInfo}"/> containing the process information.</returns>
    public async Task<LaunchOperationResult<GameProcessInfo>> GetGameProcessInfoAsync(string launchId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launchId);
        try
        {
            var launchInfo = await launchRegistry.GetLaunchInfoAsync(launchId);
            if (launchInfo == null)
            {
                return LaunchOperationResult<GameProcessInfo>.CreateFailure("Launch ID not found", launchId);
            }

            var result = await processManager.GetProcessInfoAsync(launchInfo.ProcessInfo.ProcessId, cancellationToken);
            if (!result.Success)
            {
                return LaunchOperationResult<GameProcessInfo>.CreateFailure(result.FirstError!, launchId, launchInfo.ProfileId);
            }

            return LaunchOperationResult<GameProcessInfo>.CreateSuccess(result.Data!, launchId, launchInfo.ProfileId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get game process info for launch {LaunchId}", launchId);
            return LaunchOperationResult<GameProcessInfo>.CreateFailure($"Failed to get process info: {ex.Message}", launchId);
        }
    }

    /// <summary>
    /// Terminates a running game instance by its launch ID.
    /// </summary>
    /// <param name="launchId">The launch ID of the running game instance.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="LaunchOperationResult{GameLaunchInfo}"/> representing the result of the termination operation.</returns>
    public async Task<LaunchOperationResult<GameLaunchInfo>> TerminateGameAsync(string launchId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launchId);
        try
        {
            var launchInfo = await launchRegistry.GetLaunchInfoAsync(launchId);
            if (launchInfo == null)
            {
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure("Launch ID not found", launchId);
            }

            var result = await processManager.TerminateProcessAsync(launchInfo.ProcessInfo.ProcessId, cancellationToken);
            if (!result.Success)
            {
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(result.FirstError ?? "Unknown error truncating process", launchId, launchInfo.ProfileId);
            }

            // Update launch info with termination time
            launchInfo.TerminatedAt = DateTime.UtcNow;
            await launchRegistry.UnregisterLaunchAsync(launchId);
            return LaunchOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo, launchId, launchInfo.ProfileId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to terminate game for launch {LaunchId}", launchId);
            return LaunchOperationResult<GameLaunchInfo>.CreateFailure($"Failed to terminate game: {ex.Message}", launchId);
        }
    }

    /// <summary>
    /// Validates a command-line argument for security against command injection and path traversal attacks.
    /// </summary>
    /// <param name="arg">The argument to validate.</param>
    /// <returns>True if the argument is valid, false otherwise.</returns>
    private static bool IsValidCommandArgument(string arg)
    {
        // Validate basic constraints
        if (string.IsNullOrWhiteSpace(arg))
            return false;

        // Enforce length limit to prevent buffer overflow attacks
        if (arg.Length > 1024)
            return false;
        if (arg.AsSpan().ContainsAny(InvalidArgChars))
            return false;

        // Block path traversal attempts
        if (arg.Contains("..") || arg.Contains('~'))
            return false;

        // Block suspicious patterns commonly used in injection attacks
        var lowerArg = arg.ToLowerInvariant();
        if (lowerArg.Contains("cmd.exe") || lowerArg.Contains("powershell") ||
            lowerArg.Contains("bash") || lowerArg.Contains("sh.exe"))
            return false;

        // Block environment variable expansion attempts
        if (lowerArg.Contains("%(") || arg.Contains("$("))
            return false;

        // Block quoted strings (can be used to bypass validation)
        if ((arg.StartsWith('"') && arg.EndsWith('"')) ||
            (arg.StartsWith('\'') && arg.EndsWith('\'')))
            return false;

        // Block absolute paths unless they're explicitly whitelisted for game executables
        // Only allow relative paths or filenames
        if (Path.IsPathRooted(arg))
        {
            // Only allow if it's a common game directory like C:\Program Files (x86)\Steam
            // This is still risky; consider removing this allowance entirely
            var drive = Path.GetPathRoot(arg);
            if (string.IsNullOrEmpty(drive))
                return false;

            // Validate path doesn't try to escape common directories
            if (arg.Contains("..") || arg.Contains("windows\\system", StringComparison.OrdinalIgnoreCase) ||
                arg.Contains("windows\\system32", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Builds the child process environment for a game client.
    /// </summary>
    /// <remarks>
    /// No dynamic-loader search path is set. GenHub previously prepended the workspace to
    /// <c>DYLD_LIBRARY_PATH</c> / <c>LD_LIBRARY_PATH</c> as a fallback for a build with an
    /// incomplete rpath. That was measured to be unnecessary — the BGFX build declares
    /// every dependency as <c>@executable_path/…</c> with an <c>@executable_path/</c>
    /// rpath and launches correctly with the variable cleared — and it carried two costs
    /// that outweighed a fallback for a build we do not ship:
    /// <para>
    /// dyld consults <c>DYLD_LIBRARY_PATH</c> before <c>@executable_path</c> for
    /// leaf-name references, so any same-named library elsewhere on that path silently
    /// takes precedence over the one shipped beside the executable.
    /// </para>
    /// <para>
    /// The hardened runtime ignores <c>DYLD_LIBRARY_PATH</c> outright, so the variable
    /// would stop having any effect the moment GenHub is signed and notarized. Keeping it
    /// meant launch behaviour would change silently at signing time rather than now.
    /// </para>
    /// </remarks>
    /// <param name="profileEnvironment">Environment variables configured on the profile.</param>
    /// <param name="installation">The retail installation supplying archive roots.</param>
    /// <returns>The environment to pass to the child process.</returns>
    private static Dictionary<string, string> BuildEnvironmentVariables(
        Dictionary<string, string>? profileEnvironment,
        GameInstallation? installation)
    {
        var environment = profileEnvironment is null
            ? []
            : new Dictionary<string, string>(profileEnvironment);

        if (OperatingSystem.IsWindows())
        {
            return environment;
        }

        AddRetailArchiveRoots(environment, installation);

        return environment;
    }

    /// <summary>
    /// Verifies that every configured retail archive root actually contains archives.
    /// </summary>
    /// <remarks>
    /// Checked before spawn so a misconfigured root fails with the path named, rather than
    /// as a generic engine abort the host has to interpret.
    /// <para>
    /// The engine does report these failures: a root holding no archives aborts during
    /// initialisation with exit code 1 and a <c>ReleaseCrashInfo.txt</c>, and an archive
    /// that fails to mount also writes <c>[ggc] ARCHIVE MOUNT FAILED</c> to stderr. Neither
    /// reaches the main loop. Validating first is still worth it — exit 1 is generic, the
    /// stderr sentinel exists only in the non-Windows filesystem, and on Windows
    /// <c>ReleaseCrash</c> shows a system-modal dialog before exiting, so a host-launched
    /// child hangs rather than dying. An earlier check with an actionable message avoids
    /// depending on any of that.
    /// </para>
    /// <para>
    /// Existence of at least one <c>.big</c> archive is the sentinel rather than a specific
    /// filename, which varies by localisation, version and installed mods. That bounds what
    /// this can catch: a root that is absent, unreadable or archive-free. It cannot tell
    /// whether the archives present are the ones the engine needs.
    /// </para>
    /// </remarks>
    /// <param name="environment">The environment built for the child process.</param>
    /// <param name="installation">The installation whose declared paths were used.</param>
    /// <param name="gameType">The game being launched; only its root is checked.</param>
    /// <returns>An error message naming the offending root, or <c>null</c> when valid.</returns>
    private static string? ValidateRetailArchiveRoots(
        Dictionary<string, string> environment,
        GameInstallation? installation,
        GameType gameType)
    {
        // Windows resolves install paths from the registry and never reads these variables,
        // so a Windows layout without loose top-level archives is not a misconfiguration and
        // must not fail the launch. BuildEnvironmentVariables returns before setting them on
        // Windows; this validates the installation's declared paths, so it needs the same
        // guard rather than inheriting it.
        if (OperatingSystem.IsWindows())
        {
            return null;
        }

        // Validated against the installation's declared paths, not only the variables that
        // survived into the environment. AddArchiveRoot drops a path that does not exist,
        // so validating the environment alone would silently skip the exact case this
        // exists to catch: a stale installation root reaching spawn unnoticed.
        // Which roots matter depends on the game. Generals reads only its own. Zero Hour is
        // an expansion and mounts the base Generals archives as well, so a stale Generals
        // root would leave it running without base content — the same silent failure, one
        // directory over. Launching Generals must not fail over a stale Zero Hour root
        // though: that one has no bearing on it.
        var roots = new List<(string Variable, string? Path)>
        {
            gameType == GameType.Generals
                ? (RetailArchiveConstants.GeneralsInstallPathVariable, installation?.GeneralsPath)
                : (RetailArchiveConstants.ZeroHourInstallPathVariable, installation?.ZeroHourPath),
        };

        if (gameType == GameType.ZeroHour)
        {
            // Checked only when declared, because an absent Generals root is not by itself
            // wrong: the engine mounts archives from the working directory as well, so base
            // content may legitimately sit in the workspace instead of a retail root. That
            // is the arrangement this whole mechanism replaces, but it remains valid.
            //
            // KNOWN GAP: when no Generals root is declared and the workspace does not carry
            // base content either, Zero Hour still starts with nothing to mount and this
            // check cannot tell. Archive filenames are arbitrary — a real install holds mod,
            // hotkey and control-bar archives alongside the retail ones — so presence of
            // "*.big" anywhere proves nothing about base content specifically. Detecting it
            // needs the engine to report a failed mount; see the engine-side work tracked
            // for GeneralsGameCode. A workspace "*.big" check was considered and rejected:
            // a Zero Hour workspace always contains archives, so it would always pass.
            roots.Add((RetailArchiveConstants.GeneralsInstallPathVariable, installation?.GeneralsPath));
        }

        foreach (var (variableName, declaredPath) in roots)
        {
            // A profile override is the root actually used, so it is what gets checked.
            var root = environment.TryGetValue(variableName, out var configured) && !string.IsNullOrWhiteSpace(configured)
                ? configured
                : declaredPath;

            // Nothing configured means that game is simply not installed separately.
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            if (!Directory.Exists(root))
            {
                return $"The retail archive root for {variableName} does not exist: {root}. " +
                       "The engine would abort during initialisation with a generic crash naming nothing, so the launch was stopped.";
            }

            bool hasArchive = false;
            try
            {
                hasArchive = Directory
                    .EnumerateFiles(root, RetailArchiveConstants.ArchiveSearchPattern, RetailArchiveConstants.ArchiveSearch)
                    .Any();
            }
            catch (UnauthorizedAccessException ex)
            {
                return $"The retail archive root for {variableName} could not be read: {root} ({ex.Message}).";
            }
            catch (IOException ex)
            {
                return $"The retail archive root for {variableName} could not be read: {root} ({ex.Message}).";
            }

            if (!hasArchive)
            {
                return $"The retail archive root for {variableName} contains no .big archives: {root}. " +
                       "The engine would abort during initialisation with a generic crash naming nothing, so the launch was stopped.";
            }
        }

        return null;
    }

    /// <summary>
    /// Points the engine at the user's retail archives without copying them.
    /// </summary>
    /// <remarks>
    /// A non-Windows engine build reads <c>InstallPath</c> through
    /// <c>GetStringFromRegistry</c>, which on these platforms checks
    /// <c>$CNC_ZH_INSTALLPATH</c> and <c>$CNC_GENERALS_INSTALLPATH</c> first. The engine
    /// then mounts <c>*.big</c> from those roots in addition to the working directory.
    /// <para>
    /// That matters a great deal for workspace cost. Zero Hour needs both its own and the
    /// base Generals archives — roughly 3 GB — and without this the only way to satisfy it
    /// is to materialise every one of them into each profile's workspace. With it, a
    /// workspace holds the engine and whatever content actually differs per profile, and
    /// the bulk retail data stays where the user already has it.
    /// </para>
    /// <para>
    /// The trailing separator is required, not cosmetic. The engine concatenates this
    /// value with the archive filename directly, so a root without one produces paths like
    /// <c>/path/to/GeneralsZHINIZH.big</c>. Every archive from that root then fails to
    /// open. A workspace carrying its own archives starts without the retail content; one
    /// that does not aborts during initialisation with a generic crash. Neither failure
    /// names the root.
    /// </para>
    /// </remarks>
    /// <param name="environment">The environment being built.</param>
    /// <param name="installation">The installation supplying retail data, if any.</param>
    private static void AddRetailArchiveRoots(
        Dictionary<string, string> environment,
        GameInstallation? installation)
    {
        if (installation is null)
        {
            return;
        }

        AddArchiveRoot(environment, RetailArchiveConstants.ZeroHourInstallPathVariable, installation.ZeroHourPath);
        AddArchiveRoot(environment, RetailArchiveConstants.GeneralsInstallPathVariable, installation.GeneralsPath);
    }

    /// <summary>
    /// Sets one archive-root variable, with the trailing separator the engine requires.
    /// </summary>
    /// <param name="environment">The environment being built.</param>
    /// <param name="variableName">The environment variable to set.</param>
    /// <param name="path">The retail directory, or null/empty to skip.</param>
    private static void AddArchiveRoot(
        Dictionary<string, string> environment,
        string variableName,
        string? path)
    {
        // A profile that sets this explicitly chooses the directory, but not whether the
        // trailing separator is applied: the engine concatenates the value with the archive
        // filename directly, so one without a separator produces paths like
        // "/path/toINIZH.big" and silently mounts nothing.
        if (environment.TryGetValue(variableName, out var configured) && !string.IsNullOrWhiteSpace(configured))
        {
            environment[variableName] = EnsureTrailingSeparator(configured);
            return;
        }

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        environment[variableName] = EnsureTrailingSeparator(path);
    }

    /// <summary>
    /// Appends the directory separator the engine requires, if it is not already present.
    /// </summary>
    /// <param name="path">The retail root.</param>
    /// <returns>The path, guaranteed to end in a directory separator.</returns>
    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private static string NormalizePath(string path)
    {
        try
        {
            path = Path.GetFullPath(path);
        }
        catch
        {
            // Ignored
        }

        return path.Replace('\\', '/');
    }

    private async Task<LaunchOperationResult<GameLaunchInfo>> LaunchProfileAsync(GameProfile profile, bool skipUserDataCleanup, IProgress<LaunchProgress>? progress, string launchId, CancellationToken cancellationToken)
    {
        IDisposable? steamInstallationLock = null;

        try
        {
            logger.LogInformation("[GameLauncher] === Starting launch for profile '{ProfileName}' (ID: {ProfileId}) ===", profile.Name, profile.Id);
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new LaunchProgress { Phase = LaunchPhase.ValidatingProfile, PercentComplete = 0 });
            progress?.Report(new LaunchProgress { Phase = LaunchPhase.ResolvingContent, PercentComplete = 10 });

            var prereqResult = await PrepareLaunchPrerequisitesAsync(profile, cancellationToken);
            if (!prereqResult.Success || prereqResult.Data == null)
            {
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(prereqResult.FirstError ?? "Prerequisite check failed", launchId, profile.Id);
            }

            var manifests = prereqResult.Data;
            progress?.Report(new LaunchProgress { Phase = LaunchPhase.PreparingWorkspace, PercentComplete = 20 });

            var workspaceResult = await SetupWorkspaceAndAcquireLockAsync(profile, manifests, progress, cancellationToken);
            if (!workspaceResult.Success)
            {
                await launchRegistry.UnregisterLaunchAsync(launchId);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(workspaceResult.FirstError ?? "Workspace setup failed", launchId, profile.Id);
            }

            var (installation, _, actualInstallationPath, workspaceInfo, acquiredLock) = workspaceResult.Data;
            steamInstallationLock = acquiredLock;
            var isSteamLaunch = profile.UseSteamLaunch == true && installation.InstallationType == GameInstallationType.Steam;

            if (workspaceInfo == null)
            {
                steamInstallationLock?.Dispose();
                steamInstallationLock = null;
                await launchRegistry.UnregisterLaunchAsync(launchId);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure("Workspace preparation returned null workspace info", launchId, profile.Id);
            }

            progress?.Report(new LaunchProgress { Phase = LaunchPhase.PreparingUserData, PercentComplete = 82 });
            TriggerBackgroundUserDataSwitch(profile, manifests, skipUserDataCleanup);

            progress?.Report(new LaunchProgress { Phase = LaunchPhase.Starting, PercentComplete = 90 });

            var configResult = await PrepareLaunchConfigAndExecutableAsync(
                profile,
                installation,
                manifests,
                actualInstallationPath,
                workspaceInfo,
                isSteamLaunch,
                cancellationToken);

            if (!configResult.Success)
            {
                await launchRegistry.UnregisterLaunchAsync(launchId);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(configResult.FirstError ?? "Launch config failed", launchId, profile.Id);
            }

            var (finalExecutablePath, launchConfig, steamPrep, steamAppId) = configResult.Data;

            var executionResult = await ExecuteAndRegisterProcessAsync(
                launchId,
                profile,
                manifests,
                workspaceInfo,
                finalExecutablePath,
                launchConfig,
                steamPrep,
                steamAppId,
                isSteamLaunch,
                cancellationToken);

            if (!executionResult.Success || executionResult.Data == null)
            {
                await launchRegistry.UnregisterLaunchAsync(launchId);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(executionResult.FirstError ?? "Process launch failed", launchId, profile.Id);
            }

            var launchInfo = executionResult.Data;
            progress?.Report(new LaunchProgress { Phase = LaunchPhase.Running, PercentComplete = 100 });
            logger.LogInformation("[GameLauncher] === Launch completed successfully for profile {ProfileId} ===", profile.Id);
            return LaunchOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo, launchId, profile.Id);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Launch cancelled for profile {ProfileId}, cleaning up placeholder entry", profile.Id);
            await launchRegistry.UnregisterLaunchAsync(launchId);
            throw new TaskCanceledException();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to launch profile {ProfileId}, cleaning up placeholder entry", profile.Id);
            await launchRegistry.UnregisterLaunchAsync(launchId);
            return LaunchOperationResult<GameLaunchInfo>.CreateFailure($"Launch failed: {ex.Message}", launchId, profile.Id);
        }
        finally
        {
            steamInstallationLock?.Dispose();
        }
    }

    private async Task<OperationResult<List<ContentManifest>>> PrepareLaunchPrerequisitesAsync(
        GameProfile profile,
        CancellationToken cancellationToken)
    {
        var resolutionResult = await ResolveContentManifestsAsync(profile, cancellationToken);
        if (!resolutionResult.Success || resolutionResult.Data == null)
        {
            return OperationResult<List<ContentManifest>>.CreateFailure(
                resolutionResult.FirstError ?? "Failed to resolve content dependencies.");
        }

        var manifests = resolutionResult.Data;
        logger.LogInformation(
            "[GameLauncher] Resolved {Count} manifests (from {EnabledCount} enabled IDs, including dependencies)",
            manifests.Count,
            profile.EnabledContentIds?.Count ?? 0);

        foreach (var manifest in manifests)
        {
            logger.LogDebug(
                "[GameLauncher] Manifest details - ID: {Id}, Name: {Name}, Type: {Type}, Files: {FileCount}",
                manifest.Id.Value,
                manifest.Name,
                manifest.ContentType,
                manifest.Files?.Count ?? 0);
        }

        logger.LogDebug("[GameLauncher] Applying profile settings to Options.ini before workspace preparation");
        await ApplyProfileSettingsToIniOptionsAsync(profile);

        logger.LogDebug("[GameLauncher] Running CAS preflight check");
        var casCheckResult = await PreflightCasCheckAsync(manifests, cancellationToken);
        if (!casCheckResult.Success)
        {
            logger.LogError("[GameLauncher] CAS preflight check failed: {Error}", casCheckResult.FirstError);
            return OperationResult<List<ContentManifest>>.CreateFailure(
                casCheckResult.FirstError ?? "CAS preflight check failed");
        }

        logger.LogDebug("[GameLauncher] CAS preflight check passed");
        return OperationResult<List<ContentManifest>>.CreateSuccess(manifests);
    }

    private async Task<OperationResult<(GameInstallation Installation, GenHub.Core.Models.GameClients.GameClient GameClient, string ActualInstallationPath, WorkspaceInfo Workspace, IDisposable? Lock)>> SetupWorkspaceAndAcquireLockAsync(
        GameProfile profile,
        List<ContentManifest> manifests,
        IProgress<LaunchProgress>? progress,
        CancellationToken cancellationToken)
    {
        var manifestSourcePaths = await BuildManifestSourcePathsAsync(manifests, profile, cancellationToken);
        var installResult = await ResolveInstallationAndPathsAsync(profile, cancellationToken);
        if (!installResult.Success)
        {
            return OperationResult<(GameInstallation, GenHub.Core.Models.GameClients.GameClient, string, WorkspaceInfo, IDisposable?)>.CreateFailure(
                installResult.FirstError ?? "Installation not configured.");
        }

        var (installation, gameClient, actualInstallationPath, dynamicWorkspacePath, isSteamLaunch) = installResult.Data;
        var setupContext = new WorkspaceSetupContext(actualInstallationPath, dynamicWorkspacePath, isSteamLaunch, manifestSourcePaths);

        var workspaceSetupResult = await SetupAndAcquireWorkspaceAsync(
            profile,
            manifests,
            gameClient,
            setupContext,
            progress,
            cancellationToken);

        if (!workspaceSetupResult.Success || workspaceSetupResult.Data.Workspace == null)
        {
            return OperationResult<(GameInstallation, GenHub.Core.Models.GameClients.GameClient, string, WorkspaceInfo, IDisposable?)>.CreateFailure(
                workspaceSetupResult.FirstError ?? "Workspace preparation failed");
        }

        var (workspaceInfo, acquiredLock) = workspaceSetupResult.Data;
        return OperationResult<(GameInstallation, GenHub.Core.Models.GameClients.GameClient, string, WorkspaceInfo, IDisposable?)>.CreateSuccess(
            (installation, gameClient, actualInstallationPath, workspaceInfo, acquiredLock));
    }

    private async Task<OperationResult<(string FinalExecutablePath, GameLaunchConfiguration LaunchConfig, SteamLaunchPrepResult? SteamPrep, string? SteamAppId)>> PrepareLaunchConfigAndExecutableAsync(
        GameProfile profile,
        GameInstallation installation,
        IReadOnlyList<ContentManifest> manifests,
        string actualInstallationPath,
        WorkspaceInfo workspaceInfo,
        bool isSteamLaunch,
        CancellationToken cancellationToken)
    {
        var executableResult = ResolveAndValidateExecutablePath(profile, workspaceInfo);
        if (!executableResult.Success || executableResult.Data == null)
        {
            return OperationResult<(string, GameLaunchConfiguration, SteamLaunchPrepResult?, string?)>.CreateFailure(
                executableResult.FirstError ?? "No executable path available");
        }

        var finalExecutablePath = executableResult.Data;

        var prepResult = await PrepareLaunchConfigurationAndProxyAsync(
            profile,
            installation,
            manifests,
            actualInstallationPath,
            finalExecutablePath,
            workspaceInfo,
            isSteamLaunch,
            cancellationToken);

        if (!prepResult.Success || prepResult.Data.LaunchConfig == null)
        {
            return OperationResult<(string, GameLaunchConfiguration, SteamLaunchPrepResult?, string?)>.CreateFailure(
                prepResult.FirstError ?? "Launch configuration failed");
        }

        var (launchConfig, steamPrep, steamAppId) = prepResult.Data;
        return OperationResult<(string, GameLaunchConfiguration, SteamLaunchPrepResult?, string?)>.CreateSuccess(
            (finalExecutablePath, launchConfig, steamPrep, steamAppId));
    }

    private async Task<OperationResult<GameLaunchInfo>> ExecuteAndRegisterProcessAsync(
        string launchId,
        GameProfile profile,
        List<ContentManifest> manifests,
        WorkspaceInfo workspaceInfo,
        string finalExecutablePath,
        GameLaunchConfiguration launchConfig,
        SteamLaunchPrepResult? steamPrep,
        string? steamAppId,
        bool isSteamLaunch,
        CancellationToken cancellationToken)
    {
        var effectiveStrategy = profile.WorkspaceStrategy ?? configurationProvider.GetDefaultWorkspaceStrategy();

        var processResult = await LaunchProcessAsync(
            isSteamLaunch,
            manifests,
            finalExecutablePath,
            effectiveStrategy,
            launchConfig,
            workspaceInfo,
            steamPrep,
            steamAppId,
            cancellationToken);

        if (!processResult.Success || processResult.Data == null)
        {
            logger.LogError("[GameLauncher] Process start/discovery failed: {Error}", processResult.FirstError);
            return OperationResult<GameLaunchInfo>.CreateFailure(processResult.FirstError ?? "Process start failed");
        }

        var processInfo = processResult.Data;
        logger.LogInformation("[GameLauncher] Process started successfully - PID: {ProcessId}", processInfo.ProcessId);

        var launchInfo = new GameLaunchInfo
        {
            LaunchId = launchId,
            ProfileId = profile.Id,
            WorkspaceId = workspaceInfo.Id,
            ProcessInfo = processInfo,
            LaunchedAt = DateTime.UtcNow,
        };

        logger.LogDebug("[GameLauncher] Updating launch registry with real process info");
        await launchRegistry.RegisterLaunchAsync(launchInfo);
        return OperationResult<GameLaunchInfo>.CreateSuccess(launchInfo);
    }

    private async Task<OperationResult<(WorkspaceInfo Workspace, IDisposable? Lock)>> SetupAndAcquireWorkspaceAsync(
        GameProfile profile,
        List<ContentManifest> manifests,
        GenHub.Core.Models.GameClients.GameClient gameClient,
        WorkspaceSetupContext context,
        IProgress<LaunchProgress>? progress,
        CancellationToken cancellationToken)
    {
        IDisposable? steamInstallationLock = null;
        try
        {
            if (context.IsSteamLaunch)
            {
                steamInstallationLock = await AcquireSteamInstallationLockAsync(context.ActualInstallationPath, cancellationToken);
                logger.LogInformation("[GameLauncher] Steam launch detected - workspace will be adjacent to installation in .genhub-workspace directory");
            }

            logger.LogDebug("[GameLauncher] Using dynamic workspace path: {WorkspacePath} (Installation: {InstallPath})", context.DynamicWorkspacePath, context.ActualInstallationPath);

            var workspaceResult = await PrepareWorkspaceForLaunchAsync(
                profile,
                manifests,
                gameClient,
                context.ActualInstallationPath,
                context.DynamicWorkspacePath,
                context.IsSteamLaunch,
                context.ManifestSourcePaths,
                progress,
                cancellationToken);

            if (!workspaceResult.Success || workspaceResult.Data == null)
            {
                steamInstallationLock?.Dispose();
                steamInstallationLock = null;
                return OperationResult<(WorkspaceInfo, IDisposable?)>.CreateFailure(workspaceResult.FirstError ?? "Workspace preparation failed");
            }

            var lockToReturn = steamInstallationLock;
            steamInstallationLock = null; // ownership transferred to caller
            return OperationResult<(WorkspaceInfo, IDisposable?)>.CreateSuccess((workspaceResult.Data, lockToReturn));
        }
        catch (Exception)
        {
            steamInstallationLock?.Dispose();
            throw;
        }
    }

    private async Task<OperationResult<(GameLaunchConfiguration LaunchConfig, SteamLaunchPrepResult? SteamPrep, string? SteamAppId)>> PrepareLaunchConfigurationAndProxyAsync(
        GameProfile profile,
        GameInstallation installation,
        IReadOnlyList<ContentManifest> manifests,
        string actualInstallationPath,
        string finalExecutablePath,
        WorkspaceInfo workspaceInfo,
        bool isSteamLaunch,
        CancellationToken cancellationToken)
    {
        var argsResult = BuildCommandLineArguments(profile);
        if (!argsResult.Success || argsResult.Data == null)
        {
            return OperationResult<(GameLaunchConfiguration, SteamLaunchPrepResult?, string?)>.CreateFailure(argsResult.FirstError ?? "Invalid command line arguments");
        }

        var arguments = argsResult.Data;
        SteamLaunchPrepResult? steamPrep = null;
        string? steamAppId = null;

        if (isSteamLaunch)
        {
            var prepResult = await PrepareSteamProxyAsync(
                profile,
                installation,
                manifests,
                actualInstallationPath,
                finalExecutablePath,
                workspaceInfo,
                arguments,
                cancellationToken);
            if (!prepResult.Success)
            {
                return OperationResult<(GameLaunchConfiguration, SteamLaunchPrepResult?, string?)>.CreateFailure(prepResult.FirstError ?? "Steam prep failed");
            }

            steamPrep = prepResult.Data.PrepResult;
            steamAppId = prepResult.Data.SteamAppId;
        }

        var launchConfig = BuildGameLaunchConfiguration(finalExecutablePath, workspaceInfo, arguments, profile, installation);

        var archiveRootError = ValidateRetailArchiveRoots(launchConfig.EnvironmentVariables, installation, profile.GameClient!.GameType);
        if (archiveRootError is not null)
        {
            logger.LogError("[GameLauncher] Retail archive root validation failed: {Error}", archiveRootError);
            return OperationResult<(GameLaunchConfiguration, SteamLaunchPrepResult?, string?)>.CreateFailure(archiveRootError);
        }

        return OperationResult<(GameLaunchConfiguration, SteamLaunchPrepResult?, string?)>.CreateSuccess((launchConfig, steamPrep, steamAppId));
    }

    private async Task<OperationResult<WorkspaceInfo>> PrepareWorkspaceForLaunchAsync(
        GameProfile profile,
        List<ContentManifest> manifests,
        GenHub.Core.Models.GameClients.GameClient gameClient,
        string actualInstallationPath,
        string dynamicWorkspacePath,
        bool isSteamLaunch,
        Dictionary<string, string> manifestSourcePaths,
        IProgress<LaunchProgress>? progress,
        CancellationToken cancellationToken)
    {
        var effectiveStrategy = profile.WorkspaceStrategy ?? configurationProvider.GetDefaultWorkspaceStrategy();
        logger.LogDebug("[GameLauncher] Creating workspace configuration - Strategy: {Strategy} (Effective)", effectiveStrategy);
        var workspaceConfig = new WorkspaceConfiguration
        {
            Id = profile.Id,
            Manifests = manifests,
            GameClient = gameClient,
            Strategy = effectiveStrategy,
            ForceRecreate = false,
            WorkspaceRootPath = dynamicWorkspacePath,
            BaseInstallationPath = actualInstallationPath,
            ManifestSourcePaths = manifestSourcePaths,
        };
        logger.LogDebug("[GameLauncher] BaseInstallationPath set to: {Path}", workspaceConfig.BaseInstallationPath);

        if (isSteamLaunch && !string.IsNullOrEmpty(actualInstallationPath))
        {
            var cleanupResult = await PerformPreLaunchSteamCleanupAsync(actualInstallationPath, cancellationToken);
            if (!cleanupResult.Success)
            {
                return OperationResult<WorkspaceInfo>.CreateFailure(cleanupResult.FirstError ?? "Pre-launch Steam cleanup failed");
            }
        }

        logger.LogInformation("[GameLauncher] Preparing workspace at: {WorkspacePath}", workspaceConfig.WorkspaceRootPath);
        var workspaceProgress = new Progress<WorkspacePreparationProgress>(
            wp =>
            {
                var percentComplete = 20 + (int)(wp.FilesProcessed / (double)Math.Max(1, wp.TotalFiles) * 60);
                progress?.Report(new LaunchProgress
                {
                    Phase = LaunchPhase.PreparingWorkspace,
                    PercentComplete = Math.Min(percentComplete, 80),
                    IsInitializingWorkspace = true,
                    TotalFiles = wp.TotalFiles,
                    FilesProcessed = wp.FilesProcessed,
                    CurrentFile = wp.CurrentFile,
                });
            });

        var workspaceResult = await workspaceManager.PrepareWorkspaceAsync(workspaceConfig, workspaceProgress, skipCleanup: false, cancellationToken);
        if (!workspaceResult.Success || workspaceResult.Data == null)
        {
            logger.LogError("[GameLauncher] Workspace preparation failed: {Error}", workspaceResult.FirstError);
            return OperationResult<WorkspaceInfo>.CreateFailure(workspaceResult.FirstError ?? "Workspace preparation failed");
        }

        var workspaceInfo = workspaceResult.Data;
        logger.LogInformation("[GameLauncher] Workspace prepared successfully: {WorkspaceId}", workspaceInfo.Id);

        if (profile.VideoSkipEALogo == true)
        {
            HandleVideoSkipEaLogo(workspaceInfo.WorkspacePath);
        }

        return OperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo);
    }

    private async Task<OperationResult<WorkspaceInfo>> PrepareWorkspaceForLaunchAsync(
        WorkspaceConfiguration workspaceConfig,
        bool isSteamLaunch,
        IProgress<LaunchProgress>? progress,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("[GameLauncher] Preparing workspace at: {WorkspacePath}", workspaceConfig.WorkspaceRootPath);
        var workspaceProgress = new Progress<WorkspacePreparationProgress>(
            wp =>
            {
                var percentComplete = 20 + (int)(wp.FilesProcessed / (double)Math.Max(1, wp.TotalFiles) * 60);
                progress?.Report(new LaunchProgress
                {
                    Phase = LaunchPhase.PreparingWorkspace,
                    PercentComplete = Math.Min(percentComplete, 80),
                    IsInitializingWorkspace = true,
                    TotalFiles = wp.TotalFiles,
                    FilesProcessed = wp.FilesProcessed,
                    CurrentFile = wp.CurrentFile,
                });
            });

        return await workspaceManager.PrepareWorkspaceAsync(workspaceConfig, workspaceProgress, skipCleanup: false, cancellationToken);
    }

    private void TriggerBackgroundUserDataSwitch(
        GameProfile profile,
        List<ContentManifest> manifests,
        bool skipUserDataCleanup)
    {
        var previousActiveProfileId = profileContentLinker.GetActiveProfileId();
        _ = Task.Run(
            async () =>
            {
                try
                {
                    logger.LogDebug(
                        "[GameLauncher] Background: Switching user data from profile {OldProfile} to {NewProfile}",
                        previousActiveProfileId ?? "(none)",
                        profile.Id);
                    var userDataResult = await profileContentLinker.SwitchProfileUserDataAsync(
                        previousActiveProfileId,
                        profile.Id,
                        manifests,
                        profile.GameClient?.GameType ?? GameType.ZeroHour,
                        skipUserDataCleanup,
                        CancellationToken.None);
                    if (!userDataResult.Success)
                    {
                        logger.LogWarning("[GameLauncher] Background user data preparation had issues: {Error}", userDataResult.FirstError);
                    }
                    else
                    {
                        logger.LogInformation("[GameLauncher] Background user data content prepared for profile {ProfileId}", profile.Id);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[GameLauncher] Unexpected error in background user data linkage for profile {ProfileId}", profile.Id);
                }
            },
            CancellationToken.None);
    }

    private OperationResult<string> ResolveAndValidateExecutablePath(
        GameProfile profile,
        WorkspaceInfo workspaceInfo)
    {
        var finalExecutablePath = workspaceInfo.ExecutablePath;

        if (string.IsNullOrEmpty(finalExecutablePath))
        {
            finalExecutablePath = profile.GameClient?.ExecutablePath;
            logger.LogWarning("[GameLauncher] Executable not resolved from workspace, falling back to profile: {ExecutablePath}", finalExecutablePath);
        }

        if (string.IsNullOrEmpty(finalExecutablePath))
        {
            logger.LogError("[GameLauncher] No executable path available");
            return OperationResult<string>.CreateFailure("Executable path not specified in workspace or profile");
        }

        if (!string.IsNullOrEmpty(workspaceInfo.ExecutablePath))
        {
            logger.LogDebug("[GameLauncher] Validating executable is within workspace bounds");
            var normalizedWorkspacePath = NormalizePath(workspaceInfo.WorkspacePath);
            var normalizedWorkspacePrefix = normalizedWorkspacePath.TrimEnd('/') + '/';
            var normalizedExecutablePath = NormalizePath(finalExecutablePath);
            if (!normalizedExecutablePath.StartsWith(normalizedWorkspacePrefix, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalizedExecutablePath, normalizedWorkspacePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                logger.LogError("[GameLauncher] Security violation - executable outside workspace");
                return OperationResult<string>.CreateFailure($"Security violation: Workspace executable path '{finalExecutablePath}' is outside workspace");
            }
        }
        else if (profile.GameClient != null && !string.IsNullOrEmpty(profile.GameClient.WorkingDirectory))
        {
            var normalizedWorkingDir = NormalizePath(profile.GameClient.WorkingDirectory);
            var normalizedWorkingDirPrefix = normalizedWorkingDir.TrimEnd('/') + '/';
            var normalizedExecutablePath = NormalizePath(finalExecutablePath);
            if (!normalizedExecutablePath.StartsWith(normalizedWorkingDirPrefix, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalizedExecutablePath, normalizedWorkingDir.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                logger.LogError("[GameLauncher] Security violation - profile fallback executable outside working directory");
                return OperationResult<string>.CreateFailure(
                    $"Security violation: Fallback executable path '{finalExecutablePath}' is outside working directory");
            }
        }

        logger.LogInformation("[GameLauncher] Final executable: {ExecutablePath}", finalExecutablePath);
        logger.LogDebug("[GameLauncher] Working directory: {WorkingDirectory}", workspaceInfo.WorkspacePath);
        return OperationResult<string>.CreateSuccess(finalExecutablePath);
    }

    private GameLaunchConfiguration BuildGameLaunchConfiguration(
        string finalExecutablePath,
        WorkspaceInfo workspaceInfo,
        Dictionary<string, string> arguments,
        GameProfile profile,
        GameInstallation installation)
    {
        return new GameLaunchConfiguration
        {
            ExecutablePath = finalExecutablePath,
            WorkingDirectory = workspaceInfo.WorkspacePath,
            Arguments = arguments,
            EnvironmentVariables = BuildEnvironmentVariables(profile.EnvironmentVariables, installation),
            ExpectedChildProcessName = LaunchEntryPointResolver.ResolveExpectedChildProcessName(finalExecutablePath),
        };
    }

    private async Task<OperationResult<GameProcessInfo>> LaunchProcessAsync(
        bool isSteamLaunch,
        List<ContentManifest> manifests,
        string finalExecutablePath,
        WorkspaceStrategy effectiveStrategy,
        GameLaunchConfiguration launchConfig,
        WorkspaceInfo workspaceInfo,
        SteamLaunchPrepResult? steamPrep,
        string? steamAppId,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("[GameLauncher] Starting game process...");
        if (isSteamLaunch)
        {
            return await StartSteamGameAsync(
                manifests,
                finalExecutablePath,
                effectiveStrategy,
                launchConfig,
                workspaceInfo,
                steamPrep,
                steamAppId,
                cancellationToken);
        }

        return await processManager.StartProcessAsync(launchConfig, cancellationToken);
    }

    private async Task<OperationResult<GameProcessInfo>> StartSteamGameAsync(
        List<ContentManifest> manifests,
        string finalExecutablePath,
        WorkspaceStrategy effectiveStrategy,
        GameLaunchConfiguration launchConfig,
        WorkspaceInfo workspaceInfo,
        SteamLaunchPrepResult? steamPrep,
        string? steamAppId,
        CancellationToken cancellationToken)
    {
        if (steamPrep == null || string.IsNullOrWhiteSpace(steamPrep.ExecutablePath))
        {
            logger.LogError("[GameLauncher] Steam prep missing proxy path");
            return OperationResult<GameProcessInfo>.CreateFailure("Steam proxy not prepared");
        }

        if (string.IsNullOrWhiteSpace(steamAppId))
        {
            logger.LogError("[GameLauncher] Steam AppId is missing");
            return OperationResult<GameProcessInfo>.CreateFailure("Steam AppId missing");
        }

        var steamUrl = $"steam://rungameid/{steamAppId}";
        logger.LogInformation("[GameLauncher] Launching via Steam URL: {SteamUrl}", steamUrl);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = steamUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[GameLauncher] Failed to launch via Steam URL");
            return OperationResult<GameProcessInfo>.CreateFailure($"Failed to launch via Steam: {ex.Message}");
        }

        var gameProcessName = DetermineMonitoringProcessName(
            manifests,
            finalExecutablePath,
            effectiveStrategy,
            launchConfig.ExpectedChildProcessName);

        return await processManager.DiscoverAndTrackProcessAsync(
            gameProcessName,
            workspaceInfo.WorkspacePath,
            cancellationToken);
    }

    private async Task<OperationResult<List<ContentManifest>>> ResolveContentManifestsAsync(
        GameProfile profile,
        CancellationToken cancellationToken)
    {
        var enabledIds = profile.EnabledContentIds ?? Enumerable.Empty<string>();
        logger.LogDebug("[GameLauncher] Resolving {Count} enabled content manifests with dependencies", profile.EnabledContentIds?.Count ?? 0);
        var resolutionResult = await dependencyResolver.ResolveDependenciesWithManifestsAsync(enabledIds, cancellationToken);
        if (!resolutionResult.Success)
        {
            logger.LogError("[GameLauncher] Failed to resolve content dependencies: {Error}", resolutionResult.FirstError);
            return OperationResult<List<ContentManifest>>.CreateFailure($"Failed to resolve content dependencies: {resolutionResult.FirstError}");
        }

        if (resolutionResult.Warnings?.Any() == true)
        {
            foreach (var warning in resolutionResult.Warnings)
            {
                logger.LogWarning("[GameLauncher] Dependency resolution warning: {Warning}", warning);
            }
        }

        var manifests = resolutionResult.ResolvedManifests.ToList();
        logger.LogInformation(
            "[GameLauncher] Resolved {Count} manifests (from {EnabledCount} enabled IDs, including dependencies)",
            manifests.Count,
            enabledIds.Count());
        foreach (var manifest in manifests)
        {
            logger.LogDebug(
                "[GameLauncher] Manifest details - ID: {Id}, Name: {Name}, Type: {Type}, Files: {FileCount}",
                manifest.Id.Value,
                manifest.Name,
                manifest.ContentType,
                manifest.Files?.Count ?? 0);
        }

        return OperationResult<List<ContentManifest>>.CreateSuccess(manifests);
    }

    private async Task<Dictionary<string, string>> BuildManifestSourcePathsAsync(
        IReadOnlyList<ContentManifest> manifests,
        GameProfile profile,
        CancellationToken cancellationToken)
    {
        var manifestSourcePaths = new Dictionary<string, string>();
        foreach (var manifest in manifests)
        {
            if (manifest.ContentType == ContentType.GameInstallation)
            {
                continue;
            }

            var contentDirResult = await manifestPool.GetContentDirectoryAsync(manifest.Id, cancellationToken);
            if (contentDirResult.Success && !string.IsNullOrEmpty(contentDirResult.Data))
            {
                manifestSourcePaths[manifest.Id.Value] = contentDirResult.Data;
                logger.LogDebug(
                    "[GameLauncher] Source path for content {ManifestId} ({ContentType}): {SourcePath}",
                    manifest.Id.Value,
                    manifest.ContentType,
                    contentDirResult.Data);
            }
            else if (manifest.ContentType == ContentType.GameClient && !string.IsNullOrEmpty(profile.GameClient?.WorkingDirectory))
            {
                manifestSourcePaths[manifest.Id.Value] = profile.GameClient.WorkingDirectory;
                logger.LogDebug(
                    "[GameLauncher] Source path for GameClient {ManifestId} resolved from profile working directory: {SourcePath}",
                    manifest.Id.Value,
                    profile.GameClient.WorkingDirectory);
            }
            else
            {
                bool isCasBacked = manifest.Files is { Count: > 0 } &&
                    manifest.Files.All(f => f.SourceType == ContentSourceType.ContentAddressable || !string.IsNullOrEmpty(f.Hash));

                if (isCasBacked)
                {
                    logger.LogDebug(
                        "[GameLauncher] Source path for CAS-backed manifest {ManifestId} ({ContentType}) is managed by CAS pool",
                        manifest.Id.Value,
                        manifest.ContentType);
                }
                else
                {
                    logger.LogWarning(
                        "[GameLauncher] Could not resolve source path for manifest {ManifestId} ({ContentType})",
                        manifest.Id.Value,
                        manifest.ContentType);
                }
            }
        }

        return manifestSourcePaths;
    }

    private async Task<OperationResult<(GameInstallation Installation, GenHub.Core.Models.GameClients.GameClient GameClient, string ActualInstallationPath, string DynamicWorkspacePath, bool IsSteamLaunch)>> ResolveInstallationAndPathsAsync(
        GameProfile profile,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.GameInstallationId))
        {
            logger.LogError("[GameLauncher] Profile {ProfileId} has no GameInstallationId set", profile.Id);
            return OperationResult<(GameInstallation, GenHub.Core.Models.GameClients.GameClient, string, string, bool)>.CreateFailure("Game installation not configured for this profile.");
        }

        var installationResult = await gameInstallationService.GetInstallationAsync(profile.GameInstallationId, cancellationToken);
        if (!installationResult.Success || installationResult.Data == null)
        {
            logger.LogError("[GameLauncher] Failed to retrieve installation {InstallationId}: {Error}", profile.GameInstallationId, installationResult.FirstError);
            return OperationResult<(GameInstallation, GenHub.Core.Models.GameClients.GameClient, string, string, bool)>.CreateFailure(installationResult.FirstError ?? "Game installation not found.");
        }

        var installation = installationResult.Data;
        var gameClient = profile.GameClient;
        if (gameClient == null)
        {
            logger.LogError("[GameLauncher] GameClient is not set for profile {ProfileId}", profile.Id);
            return OperationResult<(GameInstallation, GenHub.Core.Models.GameClients.GameClient, string, string, bool)>.CreateFailure("GameClient not configured for profile.");
        }

        var actualInstallationPath = gameClient.GameType == GameType.Generals
            ? installation.GeneralsPath ?? string.Empty
            : installation.ZeroHourPath ?? string.Empty;
        if (string.IsNullOrEmpty(actualInstallationPath))
        {
            logger.LogError("[GameLauncher] Installation path is not set for {GameType}", gameClient.GameType);
            return OperationResult<(GameInstallation, GenHub.Core.Models.GameClients.GameClient, string, string, bool)>.CreateFailure("Installation path not found.");
        }

        var dynamicWorkspacePath = storageLocationService.GetWorkspacePath(installation);
        var isSteamLaunch = profile.UseSteamLaunch == true && installation.InstallationType == GameInstallationType.Steam;

        return OperationResult<(GameInstallation, GenHub.Core.Models.GameClients.GameClient, string, string, bool)>.CreateSuccess((installation, gameClient, actualInstallationPath, dynamicWorkspacePath, isSteamLaunch));
    }

    private async Task<OperationResult<bool>> PerformPreLaunchSteamCleanupAsync(
        string actualInstallationPath,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("[GameLauncher] Performing pre-launch cleanup to ensure original executables are present");
        var steamExecutableName = GameClientConstants.GeneralsExecutable;
        var cleanupResult = await steamLauncher.CleanupGameDirectoryAsync(
            actualInstallationPath,
            steamExecutableName,
            cancellationToken);
        if (!cleanupResult.Success)
        {
            logger.LogError(
                "[GameLauncher] Pre-launch Steam cleanup failed: {Error}",
                cleanupResult.FirstError);
            return OperationResult<bool>.CreateFailure($"Failed to restore the Steam installation before launch: {cleanupResult.FirstError}");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private void HandleVideoSkipEaLogo(string workspacePath)
    {
        var possiblePaths = new[]
        {
            Path.Combine(workspacePath, "Data", "Movies", GameContentConstants.EaLogoBikFileName),
            Path.Combine(workspacePath, "Data", "English", "Movies", GameContentConstants.EaLogoBikFileName),
            Path.Combine(workspacePath, "Movies", GameContentConstants.EaLogoBikFileName),
            Path.Combine(workspacePath, "data", "movies", GameContentConstants.EaLogoBikFileName),
        };

        logger.LogInformation("[GameLauncher] Skip EA Logo enabled - checking workspace: {WorkspacePath}", workspacePath);

        var deleted = false;
        foreach (var logoPath in possiblePaths)
        {
            if (File.Exists(logoPath))
            {
                try
                {
                    File.Delete(logoPath);
                    logger.LogInformation("[GameLauncher] Successfully deleted EA logo at: {LogoPath}", logoPath);
                    deleted = true;
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[GameLauncher] Failed to delete {LogoFileName} at {LogoPath}", GameContentConstants.EaLogoBikFileName, logoPath);
                }
            }
        }

        if (!deleted)
        {
            logger.LogWarning("[GameLauncher] Skip EA Logo enabled but {LogoFileName} not found in workspace. Checked paths: {Paths}", GameContentConstants.EaLogoBikFileName, string.Join(", ", possiblePaths));
        }
    }

    private OperationResult<Dictionary<string, string>> BuildCommandLineArguments(GameProfile profile)
    {
        var arguments = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(profile.CommandLineArguments))
        {
            var args = profile.CommandLineArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var arg in args)
            {
                if (!IsValidCommandArgument(arg))
                {
                    return OperationResult<Dictionary<string, string>>.CreateFailure($"Invalid command argument: {arg}");
                }
            }

            var positionalIndex = 0;
            foreach (var arg in args)
            {
                if (arg.StartsWith('-'))
                {
                    arguments[arg] = string.Empty;
                }
                else
                {
                    arguments[$"_pos{positionalIndex}"] = arg;
                    positionalIndex++;
                }
            }
        }

        foreach (var kvp in profile.LaunchOptions)
        {
            arguments[kvp.Key] = kvp.Value;
        }

        if (profile.VideoWindowed == true && !arguments.ContainsKey("-win"))
        {
            arguments["-win"] = string.Empty;
            logger.LogInformation("[GameLauncher] Added -win argument for windowed mode");
        }

        return OperationResult<Dictionary<string, string>>.CreateSuccess(arguments);
    }

    private async Task<OperationResult<(SteamLaunchPrepResult PrepResult, string SteamAppId)>> PrepareSteamProxyAsync(
        GameProfile profile,
        GameInstallation installation,
        IReadOnlyList<ContentManifest> manifests,
        string actualInstallationPath,
        string finalExecutablePath,
        WorkspaceInfo workspaceInfo,
        Dictionary<string, string> arguments,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("[GameLauncher] Steam integration enabled - using in-place file provisioning");
        var steamExecutableName = GameClientConstants.GeneralsExecutable;
        logger.LogInformation("[GameLauncher] Steam executable to replace with proxy: {ExecutableName}", steamExecutableName);

        string steamAppId;
        if (SteamAppIdResolver.TryResolveSteamAppIdFromInstallationPath(actualInstallationPath, out var resolvedSteamAppId))
        {
            steamAppId = resolvedSteamAppId;
        }
        else
        {
            steamAppId = profile.GameClient?.GameType == GameType.Generals
                ? SteamConstants.GeneralsAppId
                : SteamConstants.ZeroHourAppId;
        }

        var targetArguments = arguments.Select(kvp => string.IsNullOrEmpty(kvp.Value) ? kvp.Key : $"{kvp.Key} {kvp.Value}").ToArray();

        var steamLaunchResult = await steamLauncher.PrepareForProfileAsync(
            actualInstallationPath,
            profile.Id,
            manifests,
            steamExecutableName,
            finalExecutablePath,
            workspaceInfo.WorkspacePath,
            targetArguments,
            steamAppId,
            cancellationToken);

        if (!steamLaunchResult.Success || steamLaunchResult.Data == null)
        {
            logger.LogError("[GameLauncher] Steam launch preparation failed: {Error}", steamLaunchResult.FirstError);
            return OperationResult<(SteamLaunchPrepResult, string)>.CreateFailure(
                $"Failed to prepare game directory for Steam integration: {steamLaunchResult.FirstError}");
        }

        logger.LogInformation(
            "[GameLauncher] Steam launch preparation complete. Files: {Linked} linked, {Removed} removed, {BackedUp} backed up",
            steamLaunchResult.Data.FilesLinked,
            steamLaunchResult.Data.FilesRemoved,
            steamLaunchResult.Data.FilesBackedUp);

        logger.LogInformation(
            "[GameLauncher] Steam integration ready. Proxy sidecar: {ProxyPath}",
            steamLaunchResult.Data.ExecutablePath);

        return OperationResult<(SteamLaunchPrepResult, string)>.CreateSuccess((steamLaunchResult.Data, steamAppId));
    }

    private string DetermineMonitoringProcessName(
        IReadOnlyList<ContentManifest> manifests,
        string finalExecutablePath,
        WorkspaceStrategy effectiveStrategy,
        string? expectedChildProcessName)
    {
        var executableManifestForMonitor = manifests.FirstOrDefault(m =>
            m.ContentType == ContentType.GameClient ||
            m.ContentType == ContentType.Executable ||
            m.ContentType == ContentType.ModdingTool);
        var executableFileForMonitor = executableManifestForMonitor?.Files?.FirstOrDefault(f => f.IsExecutable);

        if (executableFileForMonitor is { SourceType: ContentSourceType.ContentAddressable } &&
            effectiveStrategy == WorkspaceStrategy.SymlinkOnly)
        {
            var gameProcessName = executableFileForMonitor.Hash;
            logger.LogInformation("[GameLauncher] Monitoring for CAS symlinked process with hash: {Hash}", gameProcessName);

            if (!string.IsNullOrEmpty(expectedChildProcessName))
            {
                logger.LogWarning(
                    "[GameLauncher] Launching {Entry} through a bootstrapper under CAS symlinking; monitoring may track the wrong process",
                    Path.GetFileName(finalExecutablePath));
            }

            return gameProcessName;
        }

        var processName = executableFileForMonitor != null
            ? Path.GetFileNameWithoutExtension(executableFileForMonitor.RelativePath)
            : Path.GetFileNameWithoutExtension(finalExecutablePath);

        if (!string.IsNullOrEmpty(expectedChildProcessName))
        {
            processName = expectedChildProcessName;
        }

        logger.LogInformation("[GameLauncher] Monitoring for process: {ProcessName}", processName);
        return processName;
    }

    private async Task<OperationResult<(GameInstallation Installation, GameClient GameClient, string ActualInstallationPath, string DynamicWorkspacePath, bool IsSteamLaunch)>> ResolveLaunchInstallationAndPathsAsync(
        GameProfile profile,
        string launchId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.GameInstallationId))
        {
            logger.LogError("[GameLauncher] Profile {ProfileId} has no GameInstallationId set", profile.Id);
            await launchRegistry.UnregisterLaunchAsync(launchId);
            return OperationResult<(GameInstallation, GameClient, string, string, bool)>.CreateFailure("Game installation not configured for this profile.");
        }

        var installationResult = await gameInstallationService.GetInstallationAsync(profile.GameInstallationId, cancellationToken);
        if (!installationResult.Success || installationResult.Data == null)
        {
            logger.LogError("[GameLauncher] Failed to retrieve installation {InstallationId}: {Error}", profile.GameInstallationId, installationResult.FirstError);
            await launchRegistry.UnregisterLaunchAsync(launchId);
            return OperationResult<(GameInstallation, GameClient, string, string, bool)>.CreateFailure($"Failed to retrieve game installation: {installationResult.FirstError ?? "Installation not found"}");
        }

        var installation = installationResult.Data;
        var gameClient = profile.GameClient;
        if (gameClient == null)
        {
            logger.LogError("[GameLauncher] GameClient is not set for profile {ProfileId}", profile.Id);
            await launchRegistry.UnregisterLaunchAsync(launchId);
            return OperationResult<(GameInstallation, GameClient, string, string, bool)>.CreateFailure("GameClient not configured for profile.");
        }

        var actualInstallationPath = gameClient.GameType == GameType.Generals
            ? installation.GeneralsPath ?? string.Empty
            : installation.ZeroHourPath ?? string.Empty;

        if (string.IsNullOrEmpty(actualInstallationPath))
        {
            logger.LogError("[GameLauncher] Installation path is not set for {GameType}", gameClient.GameType);
            return OperationResult<(GameInstallation, GameClient, string, string, bool)>.CreateFailure("Installation path not found.");
        }

        var dynamicWorkspacePath = storageLocationService.GetWorkspacePath(installation);
        var isSteamLaunch = profile.UseSteamLaunch == true && installation.InstallationType == GameInstallationType.Steam;

        return OperationResult<(GameInstallation, GameClient, string, string, bool)>.CreateSuccess(
            (installation, gameClient, actualInstallationPath, dynamicWorkspacePath, isSteamLaunch));
    }

    private WorkspaceConfiguration CreateWorkspaceLaunchConfiguration(
        GameProfile profile,
        List<ContentManifest> workspaceManifests,
        GameClient gameClient,
        Dictionary<string, string> manifestSourcePaths,
        string actualInstallationPath,
        string dynamicWorkspacePath,
        bool isSteamLaunch)
    {
        var effectiveStrategy = profile.WorkspaceStrategy ?? configurationProvider.GetDefaultWorkspaceStrategy();
        return new WorkspaceConfiguration
        {
            Id = profile.Id,
            Manifests = workspaceManifests,
            GameClient = gameClient,
            Strategy = effectiveStrategy,
            ForceRecreate = false,
            WorkspaceRootPath = dynamicWorkspacePath,
            BaseInstallationPath = actualInstallationPath,
            ManifestSourcePaths = manifestSourcePaths,
        };
    }

    private void HandlePostWorkspacePreparation(
        GameProfile profile,
        WorkspaceInfo workspaceInfo,
        List<ContentManifest> manifests,
        bool skipUserDataCleanup,
        IProgress<LaunchProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (profile.VideoSkipEALogo == true)
        {
            DeleteEaLogoIfExists(workspaceInfo.WorkspacePath);
        }

        progress?.Report(new LaunchProgress { Phase = LaunchPhase.PreparingUserData, PercentComplete = 82 });
        var previousActiveProfileId = profileContentLinker.GetActiveProfileId();
        _ = Task.Run(
            async () =>
            {
                try
                {
                    logger.LogDebug(
                        "[GameLauncher] Background: Switching user data from profile {OldProfile} to {NewProfile}",
                        previousActiveProfileId ?? "(none)",
                        profile.Id);
                    var userDataResult = await profileContentLinker.SwitchProfileUserDataAsync(
                        previousActiveProfileId,
                        profile.Id,
                        manifests,
                        profile.GameClient?.GameType ?? GameType.ZeroHour,
                        skipUserDataCleanup,
                        CancellationToken.None);
                    if (!userDataResult.Success)
                    {
                        logger.LogWarning("[GameLauncher] Background user data preparation had issues: {Error}", userDataResult.FirstError);
                    }
                    else
                    {
                        logger.LogInformation("[GameLauncher] Background user data content prepared for profile {ProfileId}", profile.Id);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[GameLauncher] Unexpected error in background user data linkage for profile {ProfileId}", profile.Id);
                }
            },
            cancellationToken);
    }

    private void DeleteEaLogoIfExists(string workspacePath)
    {
        var possiblePaths = new[]
        {
            Path.Combine(workspacePath, "Data", "Movies", "EA_LOGO.BIK"),
            Path.Combine(workspacePath, "Data", "English", "Movies", "EA_LOGO.BIK"),
            Path.Combine(workspacePath, "Movies", "EA_LOGO.BIK"),
            Path.Combine(workspacePath, "data", "movies", "EA_LOGO.BIK"),
        };

        foreach (var logoPath in possiblePaths.Where(File.Exists))
        {
            try
            {
                File.Delete(logoPath);
                logger.LogInformation("[GameLauncher] Successfully deleted EA logo at: {LogoPath}", logoPath);
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[GameLauncher] Failed to delete EA_LOGO.BIK at {LogoPath}", logoPath);
            }
        }
    }

    /// <summary>
    /// Applies the profile-specific game settings to the Options.ini file before launching.
    /// This ensures the game launches with the settings configured for this specific profile.
    /// </summary>
    /// <param name="profile">The game profile containing the settings to apply.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ApplyProfileSettingsToIniOptionsAsync(GameProfile profile)
    {
        try
        {
            // Add null check for GameClient to resolve CS8602 warnings
            if (profile.GameClient == null)
            {
                logger.LogWarning("Profile {ProfileId} has no GameClient configured, skipping Options.ini write", profile.Id);
                return;
            }

            // Determine the game type from the profile's game client
            var gameType = profile.GameClient.GameType;
            if (gameType == GameType.Unknown)
            {
                logger.LogWarning("Profile {ProfileId} has unknown game type, skipping Options.ini write", profile.Id);
                return;
            }

            logger.LogDebug("Loading existing Options.ini for {GameType} to preserve TheSuperHackers/GeneralsOnline settings", gameType);

            // ALWAYS load the current Options.ini to preserve TheSuperHackers/GeneralsOnline settings
            // Even if profile has no custom settings, we need to re-save to ensure the file exists
            var loadResult = await gameSettingsService.LoadOptionsAsync(gameType);
            var options = loadResult.Success && loadResult.Data != null
                ? loadResult.Data
                : new IniOptions();

            // Apply profile settings to the options object (only overwrites settings that are configured in profile)
            if (profile.HasCustomSettings())
            {
                logger.LogInformation("Applying profile custom settings to Options.ini for {GameType}", gameType);
                GameSettingsMapper.ApplyToOptions(profile, options);
            }
            else
            {
                logger.LogDebug("Profile {ProfileId} has no custom settings, preserving existing Options.ini as-is", profile.Id);
            }

            // Save the updated Options.ini (preserves TheSuperHackers settings in AdditionalSections)
            var saveResult = await gameSettingsService.SaveOptionsAsync(gameType, options);
            if (!saveResult.Success)
            {
                logger.LogWarning("[GameLauncher] Failed to save Options.ini for {GameType}: {Error}", gameType, saveResult.FirstError);
            }
            else
            {
                logger.LogInformation("[GameLauncher] Successfully wrote Options.ini for {GameType}", gameType);
            }

            // Apply GeneralsOnline settings
            await ApplyGeneralsOnlineSettingsAsync(profile);
        }
        catch (Exception ex)
        {
            // Don't fail the launch if Options.ini writing fails - log and continue
            logger.LogError(ex, "Failed to apply profile settings to Options.ini, continuing with launch");
        }
    }

    /// <summary>
    /// Applies GeneralsOnline-specific settings to the settings.json file.
    /// </summary>
    /// <remarks>
    /// settings.json is a single global file owned by the GeneralsOnline client, not a
    /// per-profile one. Only a GeneralsOnline profile may rewrite it: a retail, TheSuperHackers
    /// or CommunityOutpost Zero Hour profile has nothing to say about that client's settings,
    /// and writing anyway replaced whatever the user had configured inside the client itself.
    /// </remarks>
    /// <param name="profile">The game profile containing the settings.</param>
    private async Task ApplyGeneralsOnlineSettingsAsync(GameProfile profile)
    {
        if (profile.GameClient?.GameType != GameType.ZeroHour || !profile.IsGeneralsOnlineProfile())
        {
            return;
        }

        try
        {
            logger.LogInformation("[GameLauncher] Applying GeneralsOnline settings to settings.json for profile {ProfileId}", profile.Id);

            // Loaded first so the settings the client owns and the profile says nothing about
            // survive the rewrite; the mapper then overwrites only what the profile declares.
            var loadResult = await gameSettingsService.LoadGeneralsOnlineSettingsAsync();
            if (loadResult?.Success != true || loadResult.Data == null)
            {
                // A missing settings.json loads as defaults and reports success, so a failure here
                // means the client's own file exists and could not be read. Rewriting it from
                // defaults would discard every key the client owns.
                logger.LogWarning(
                    "[GameLauncher] Not writing GeneralsOnline settings because settings.json could not be read: {Error}",
                    loadResult?.FirstError ?? "LoadGeneralsOnlineSettings result was null");
                return;
            }

            var settings = loadResult.Data;

            GameSettingsMapper.ApplyToGeneralsOnlineSettings(profile, settings);

            var saveResult = await gameSettingsService.SaveGeneralsOnlineSettingsAsync(settings);
            if (!saveResult.Success)
            {
                logger.LogWarning("[GameLauncher] Failed to save GeneralsOnline settings: {Error}", saveResult.FirstError);
            }
            else
            {
                logger.LogInformation("[GameLauncher] Successfully saved GeneralsOnline settings to settings.json");
            }
        }
        catch (Exception ex)
        {
            // Log and continue
            logger.LogError(ex, "[GameLauncher] Failed to apply GeneralsOnline settings, continuing with launch");
        }
    }

    /// <summary>
    /// Performs a preflight check to ensure all CAS content required by the manifests is available.
    /// </summary>
    /// <param name="manifests">The manifests to check.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    private async Task<OperationResult<bool>> PreflightCasCheckAsync(IEnumerable<ContentManifest> manifests, CancellationToken cancellationToken)
    {
        var missingHashes = new List<string>();
        foreach (var manifest in manifests)
        {
            if (manifest.Files != null)
            {
                foreach (var file in manifest.Files.Where(f => f.SourceType == ContentSourceType.ContentAddressable && !string.IsNullOrEmpty(f.Hash)))
                {
                    var existsResult = await casService.ExistsAsync(file.Hash, manifest.ContentType, cancellationToken);
                    if (!existsResult.Success || !existsResult.Data)
                    {
                        missingHashes.Add(file.Hash);
                    }
                }
            }
        }

        if (missingHashes.Count > 0)
        {
            return OperationResult<bool>.CreateFailure($"Missing CAS objects: {string.Join(", ", missingHashes.Distinct())}");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }
}
