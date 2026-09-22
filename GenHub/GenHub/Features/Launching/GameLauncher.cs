using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Extensions.Storage;
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
using GenHub.Core.Services.Tools.Checksum;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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
    IConfigurationProviderService configurationProvider,
    ILaunchReceiptService launchReceiptService,
    ILocalizationService? localizationService = null) : IGameLauncher
{
    private const string EaLogoBik = "EA_LOGO.BIK";
    private const string EaLogo640Bik = "EA_LOGO640.BIK";
    private const string MoviesDirectoryName = "Movies";
    private const string DataDirectoryName = "Data";
    private const string EnglishDirectoryName = "English";
    private const string LowerMoviesDirectoryName = "movies";
    private const string LowerDataDirectoryName = "data";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _profileLaunchLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _steamInstallationLaunchLocks =
        new(InstallationPathLockKey.Comparer);

    private static readonly SearchValues<char> InvalidArgChars = SearchValues.Create(";|&\n\r\t`$%");

    /// <inheritdoc/>
    public async Task<IDisposable> AcquireProfileLockAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var semaphore = _profileLaunchLocks.GetOrAdd(profileId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new SemaphoreReleaser(semaphore);
    }

    /// <summary>
    /// Resolves the supplemental archive root to configure on a workspace, applying the
    /// Windows gate: on Windows the registry resolves both roots, so no root is configured.
    /// This is the single entry point workspace builders use; the pure resolution below stays
    /// operating-system agnostic so its precedence rules are testable on every platform.
    /// </summary>
    /// <param name="gameType">The game being launched.</param>
    /// <param name="effectiveGeneralsArchivePath">The effective base Generals archive root, if any.</param>
    /// <param name="profileEnvironment">The profile's environment variables, if any.</param>
    /// <returns>The supplemental archive root as an absolute path, or <c>null</c> when the launch needs none.</returns>
    internal static string? ResolveSupplementalArchiveRootForWorkspace(
        GameType gameType,
        string? effectiveGeneralsArchivePath,
        IReadOnlyDictionary<string, string>? profileEnvironment) =>
        OperatingSystem.IsWindows()
            ? null
            : ResolveSupplementalArchiveRoot(gameType, effectiveGeneralsArchivePath, profileEnvironment);

    /// <summary>
    /// Resolves the supplemental archive root whose top-level archives are linked into the workspace.
    /// </summary>
    /// <remarks>
    /// Only Zero Hour needs this: it mounts the base Generals archives in addition to its own.
    /// A profile-level <c>CNC_GENERALS_INSTALLPATH</c> override wins over the installation root,
    /// mirroring <c>AddArchiveRoot</c>: it names the root actually used. Whether the archives get
    /// linked is decided separately at link time against the resolved workspace executable, which
    /// is the exact path <see cref="WineRunner"/> will wrap; the client's declared path cannot be
    /// used here because workspace aliasing may rewrite it (e.g. <c>game.dat</c> to
    /// <c>generals.exe</c>). Workspace builders call
    /// <see cref="ResolveSupplementalArchiveRootForWorkspace"/> instead, which applies the
    /// <see cref="OperatingSystem.IsWindows"/> gate on top of this resolution.
    /// </remarks>
    /// <param name="gameType">The game being launched.</param>
    /// <param name="effectiveGeneralsArchivePath">The effective base Generals archive root, if any.</param>
    /// <param name="profileEnvironment">The profile's environment variables, if any.</param>
    /// <returns>The supplemental archive root as an absolute path, or <c>null</c> when the launch needs none.</returns>
    internal static string? ResolveSupplementalArchiveRoot(
        GameType gameType,
        string? effectiveGeneralsArchivePath,
        IReadOnlyDictionary<string, string>? profileEnvironment)
    {
        if (gameType != GameType.ZeroHour)
        {
            return null;
        }

        string? root = null;
        if (profileEnvironment?.TryGetValue(RetailArchiveConstants.GeneralsInstallPathVariable, out var configured) == true &&
            !string.IsNullOrWhiteSpace(configured))
        {
            root = configured;
        }
        else if (!string.IsNullOrWhiteSpace(effectiveGeneralsArchivePath))
        {
            root = effectiveGeneralsArchivePath;
        }

        if (root is null)
        {
            return null;
        }

        // Pin the root to an absolute path: link targets are stored verbatim, and a relative
        // target would be classified as foreign on every subsequent run. Unusable values
        // resolve to null so launch validation reports them instead of failing here.
        try
        {
            return Path.GetFullPath(root);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Inspects and sanitizes the user data MapCache.ini file if it contains corrupted or NaN values.
    /// SAGE engine (Generals / Zero Hour) crashes with "A serious error has occurred" on startup
    /// if MapCache.ini contains NaN floats (-nan, nan) or corrupted entries.
    /// </summary>
    /// <param name="mapCachePath">Path to MapCache.ini.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <returns>True if a corrupted MapCache.ini was detected and purged; otherwise false.</returns>
    internal static bool TrySanitizeMapCacheFile(string? mapCachePath, ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(mapCachePath) || !File.Exists(mapCachePath))
        {
            return false;
        }

        try
        {
            var content = File.ReadAllText(mapCachePath);
            if (System.Text.RegularExpressions.Regex.IsMatch(content, @":\s*-?nan\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
            {
                logger?.LogWarning("[GameLauncher] Detected corrupted MapCache.ini containing NaN values at {MapCachePath}. Backing up and removing to prevent game startup crash.", mapCachePath);
                var backupPath = mapCachePath + ".corrupt.bak";
                try
                {
                    File.Copy(mapCachePath, backupPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    logger?.LogDebug(ex, "[GameLauncher] Failed to create backup of corrupted MapCache.ini");
                }

                File.Delete(mapCachePath);
                logger?.LogInformation("[GameLauncher] Removed corrupted MapCache.ini; game engine will regenerate a clean cache.");
                return true;
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[GameLauncher] Failed to inspect or sanitize MapCache.ini at {MapCachePath}", mapCachePath);
        }

        return false;
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

    /// <inheritdoc/>
    public async Task<LaunchOperationResult<GameLaunchInfo>> LaunchProfileAsync(
        string profileId,
        IProgress<LaunchProgress>? progress = null,
        bool skipUserDataCleanup = false,
        IReadOnlyDictionary<string, string>? additionalArguments = null,
        CancellationToken cancellationToken = default)
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
        return await LaunchProfileAsync(profile, progress, skipUserDataCleanup, additionalArguments, cancellationToken);
    }

    /// <summary>
    /// Launches a game using the provided game profile object with optional transient command line arguments.
    /// </summary>
    /// <param name="profile">The game profile to launch.</param>
    /// <param name="progress">Optional progress reporter for launch progress.</param>
    /// <param name="skipUserDataCleanup">Whether to skip cleanup of user data files (maps, etc.) from other profiles.</param>
    /// <param name="additionalArguments">Optional transient command line arguments to merge with profile launch options.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="LaunchOperationResult{T}"/> representing the result of the launch operation.</returns>
    public async Task<LaunchOperationResult<GameLaunchInfo>> LaunchProfileAsync(
        GameProfile profile,
        IProgress<LaunchProgress>? progress = null,
        bool skipUserDataCleanup = false,
        IReadOnlyDictionary<string, string>? additionalArguments = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (additionalArguments != null)
        {
            var validateResult = ValidateAdditionalArguments(additionalArguments);
            if (!validateResult.Success)
            {
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(
                    validateResult.FirstError ?? "Invalid additional command line arguments.",
                    profileId: profile.Id);
            }
        }

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
            return await LaunchProfileAsync(profile, skipUserDataCleanup, additionalArguments, progress, launchId, cancellationToken);
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

            return LaunchOperationResult<GameProcessInfo>.CreateSuccess(result.Data, launchId, launchInfo.ProfileId);
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

    private static OperationResult<bool> PopulateCommandLineArguments(
        string commandLineArguments,
        Dictionary<string, string> arguments)
    {
        var args = commandLineArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var arg in args)
        {
            if (!IsValidCommandArgument(arg))
            {
                return OperationResult<bool>.CreateFailure($"Invalid command argument: {arg}");
            }
        }

        var positionalIndex = 0;
        var i = 0;
        while (i < args.Length)
        {
            var arg = args[i];
            if (arg.StartsWith('-'))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                {
                    arguments[arg] = args[i + 1];
                    i += 2;
                }
                else
                {
                    // Flags without explicit values (e.g. -quickstart, -win).
                    // Repeated flags overwrite previous entries (last-wins behavior).
                    arguments[arg] = string.Empty;
                    i++;
                }
            }
            else
            {
                arguments[$"_pos{positionalIndex}"] = arg;
                positionalIndex++;
                i++;
            }
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private static OperationResult<bool> ValidateAdditionalArguments(IReadOnlyDictionary<string, string> additionalArguments)
    {
        foreach (var kvp in additionalArguments)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key) ||
                kvp.Key.Contains(' ') ||
                kvp.Key.Contains('\t') ||
                kvp.Key.Contains('"') ||
                kvp.Key.StartsWith("_pos", StringComparison.OrdinalIgnoreCase) ||
                !IsValidCommandArgument(kvp.Key))
            {
                return OperationResult<bool>.CreateFailure($"Invalid additional command argument key: {kvp.Key}");
            }

            if (!string.IsNullOrEmpty(kvp.Value) &&
                (kvp.Value.Contains('"') || kvp.Value.Contains('\t') || !IsValidCommandArgument(kvp.Value)))
            {
                return OperationResult<bool>.CreateFailure($"Invalid additional command argument value for '{kvp.Key}': {kvp.Value}");
            }
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private static OperationResult<bool> MergeAdditionalArguments(
        IReadOnlyDictionary<string, string> additionalArguments,
        Dictionary<string, string> arguments)
    {
        var validation = ValidateAdditionalArguments(additionalArguments);
        if (!validation.Success)
        {
            return validation;
        }

        foreach (var kvp in additionalArguments)
        {
            arguments[kvp.Key] = kvp.Value;
        }

        return OperationResult<bool>.CreateSuccess(true);
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

        var roots = GetArchiveRootsToValidate(environment, installation, gameType);

        foreach (var (variableName, declaredPath) in roots)
        {
            var error = ValidateArchiveRoot(environment, variableName, declaredPath);
            if (error is not null)
            {
                return error;
            }
        }

        return null;
    }

    /// <summary>
    /// Collects the archive roots that matter for the launching game.
    /// </summary>
    private static List<(string Variable, string? Path)> GetArchiveRootsToValidate(
        Dictionary<string, string> environment,
        GameInstallation? installation,
        GameType gameType)
    {
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
            // Checked only when declared or discovered via bundled ZH_Generals, because an absent
            // Generals root is not by itself wrong: the engine mounts archives from the working
            // directory as well, so base content may legitimately sit in the workspace instead of
            // a retail root. That is the arrangement this whole mechanism replaces, but it remains valid.
            //
            // KNOWN GAP: when no Generals root is declared, no bundled ZH_Generals directory exists,
            // and the workspace does not carry base content either, Zero Hour still starts with
            // nothing to mount and this check cannot tell. Archive filenames are arbitrary — a real
            // install holds mod, hotkey and control-bar archives alongside the retail ones — so
            // presence of *.big anywhere proves nothing about base content specifically. Detecting
            // it needs the engine to report a failed mount; see the engine-side work tracked
            // for GeneralsGameCode (#333). A workspace *.big check was considered and rejected:
            // a Zero Hour workspace always contains archives, so it would always pass.
            var effectiveGenerals = installation?.EffectiveGeneralsArchivePath;
            if (!string.IsNullOrWhiteSpace(effectiveGenerals) ||
                environment.ContainsKey(RetailArchiveConstants.GeneralsInstallPathVariable))
            {
                roots.Add((RetailArchiveConstants.GeneralsInstallPathVariable, effectiveGenerals));
            }
        }

        return roots;
    }

    /// <summary>
    /// Checks one archive root for presence, readability and archive content.
    /// </summary>
    private static string? ValidateArchiveRoot(
        Dictionary<string, string> environment,
        string variableName,
        string? declaredPath)
    {
        // A profile override is the root actually used, so it is what gets checked.
        var root = environment.TryGetValue(variableName, out var configured) && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : declaredPath;

        // Nothing configured means that game is simply not installed separately.
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        // A relative root is never coherent: GenHub resolves it against its own working
        // directory while the engine resolves it against the workspace, so one of the two
        // always reads the wrong directory. Refuse loudly instead of mounting nothing.
        if (!Path.IsPathRooted(root))
        {
            return $"The retail archive root for {variableName} must be an absolute path: {root}. " +
                   "A relative path resolves against a different directory for the launcher and the engine, so the game would mount nothing.";
        }

        // The probe is the enumeration itself: Directory.Exists returns false for an
        // unreadable root as well as a missing one, which would report a permission
        // problem as missing content. Only DirectoryNotFoundException means absence.
        bool hasArchive;
        try
        {
            hasArchive = Directory
                .EnumerateFiles(root, RetailArchiveConstants.ArchiveSearchPattern, RetailArchiveConstants.ArchiveSearch)
                .Any();
        }
        catch (DirectoryNotFoundException)
        {
            return $"The retail archive root for {variableName} does not exist: {root}. " +
                   "The engine would abort during initialisation with a generic crash naming nothing, so the launch was stopped.";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
        {
            return $"The retail archive root for {variableName} could not be read: {root} ({ex.Message}).";
        }

        if (!hasArchive)
        {
            return $"The retail archive root for {variableName} contains no .big archives: {root}. " +
                   "The engine would abort during initialisation with a generic crash naming nothing, so the launch was stopped.";
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
        AddArchiveRoot(environment, RetailArchiveConstants.GeneralsInstallPathVariable, installation.EffectiveGeneralsArchivePath);
        AddArchiveRoot(environment, RetailArchiveConstants.GeneralsXZeroHourInstallPathVariable, installation.ZeroHourPath);
        AddArchiveRoot(environment, RetailArchiveConstants.GeneralsXGeneralsInstallPathVariable, installation.EffectiveGeneralsArchivePath);
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

    /// <summary>
    /// Builds the receipt context describing what this launch consists of, shared by the
    /// configuration comparison before spawn and the recording after it.
    /// </summary>
    /// <param name="profile">The profile being launched.</param>
    /// <param name="gameClient">The game client being launched.</param>
    /// <param name="workspaceInfo">The prepared workspace.</param>
    /// <param name="launchConfig">The configuration the process is started with.</param>
    /// <param name="manifests">The manifests resolved for the launch.</param>
    /// <param name="launchId">The launch identifier.</param>
    /// <param name="installation">The resolved retail installation.</param>
    /// <returns>The receipt context.</returns>
    private static LaunchReceiptContext BuildLaunchReceiptContext(
        GameProfile profile,
        GameClient gameClient,
        WorkspaceInfo workspaceInfo,
        GameLaunchConfiguration launchConfig,
        IReadOnlyList<ContentManifest> manifests,
        string launchId,
        GameInstallation installation)
    {
        var manifestVersions = new Dictionary<string, string>();
        foreach (var manifest in manifests)
        {
            manifestVersions[manifest.Id.Value] = manifest.Version;
        }

        return new LaunchReceiptContext
        {
            LaunchId = launchId,
            ProfileId = profile.Id,
            GameClientId = gameClient.Id,
            GameType = gameClient.GameType,
            WorkspaceId = workspaceInfo.Id,
            WorkspacePath = workspaceInfo.WorkspacePath,
            ExecutablePath = launchConfig.ExecutablePath,
            WorkingDirectory = launchConfig.WorkingDirectory ?? workspaceInfo.WorkspacePath,
            EnvironmentVariables = launchConfig.EnvironmentVariables,
            ArchiveRoots = BuildReceiptArchiveRoots(installation, gameClient.GameType, launchConfig.EnvironmentVariables),
            ManifestIds = manifests.Select(m => m.Id.Value).ToList(),
            ManifestVersions = manifestVersions,
            Variant = ResolveVariantIdentity(manifests),
        };
    }

    /// <summary>Resolves the retail roots relevant to the selected game on this platform.</summary>
    /// <param name="installation">The resolved installation.</param>
    /// <param name="gameType">The game being launched.</param>
    /// <param name="environment">The effective child environment.</param>
    /// <returns>The roots to fingerprint, using the existing receipt identifiers.</returns>
    private static Dictionary<string, string> BuildReceiptArchiveRoots(
        GameInstallation installation,
        GameType gameType,
        IReadOnlyDictionary<string, string> environment)
    {
        // Windows engines use installation paths from the registry, not the CNC variables.
        var roots = OperatingSystem.IsWindows()
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(environment);
        AddRetailArchiveRoots(roots, installation);

        if (gameType != GameType.ZeroHour)
        {
            roots.Remove(RetailArchiveConstants.ZeroHourInstallPathVariable);
        }

        return roots.Where(pair => RetailArchiveConstants.InstallPathVariables.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    /// <summary>
    /// Resolves the variant and entry-point identity for the receipt by re-running the
    /// same <see cref="ManifestVariantResolver"/> resolution workspace preparation applies
    /// to the game client manifest — same manifest, same host runtime, same outcome. Null
    /// when no game client manifest is part of the launch. Other executable manifests may
    /// still use variant resolution, but their identity is not recorded by this helper.
    /// </summary>
    /// <param name="manifests">The manifests resolved for the launch.</param>
    /// <returns>The game client identity, or null when no game client manifest participated.</returns>
    private static LaunchReceiptVariant? ResolveVariantIdentity(IReadOnlyList<ContentManifest> manifests)
    {
        var gameClientManifest = manifests.FirstOrDefault(m => m.ContentType == ContentType.GameClient);
        if (gameClientManifest is null)
        {
            return null;
        }

        var variant = ManifestVariantResolver.ResolveVariant(gameClientManifest);
        var entryPoint = ManifestVariantResolver.ResolveEntryPoint(gameClientManifest);

        return new LaunchReceiptVariant
        {
            GameClientManifestId = gameClientManifest.Id.Value,
            RuntimeIdentifier = ManifestVariantResolver.CurrentRuntimeIdentifier,
            HasVariants = gameClientManifest.Variants.Count > 0,
            VariantRuntimeIdentifiers = variant is null ? [] : [.. variant.RuntimeIdentifiers],
            EntryPointRelativePath = entryPoint.RelativePath,
            Resolution = entryPoint.Reason,
        };
    }

    private static string FormatCommandLineArgument(string key, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return key;
        }

        var formattedValue = value.Contains(' ') || value.Contains('\t')
            ? $"\"{value}\""
            : value;

        return $"{key} {formattedValue}";
    }

    private static (int Width, int Height) TryReadResolutionFromOptionsIni(string filePath)
    {
        try
        {
            foreach (var line in File.ReadLines(filePath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith(GameSettingsIniConstants.ResolutionKey, StringComparison.OrdinalIgnoreCase))
                {
                    var parts = trimmed.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        var dims = parts[1].Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (dims.Length >= 2 &&
                            int.TryParse(dims[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) &&
                            int.TryParse(dims[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height) &&
                            width > 0 && height > 0)
                        {
                            return (width, height);
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Non-critical: gracefully fall back
        }

        return (0, 0);
    }

    /// <summary>
    /// Attempts to resolve an executable relative path from the profile manifests before workspace setup.
    /// </summary>
    private static string? TryResolveManifestExecutablePath(IReadOnlyList<ContentManifest> manifests)
    {
        var gameClientManifest = manifests.FirstOrDefault(m => m.ContentType == ContentType.GameClient);
        if (gameClientManifest != null)
        {
            var resolution = ManifestVariantResolver.ResolveEntryPoint(gameClientManifest);
            return resolution.Success && !string.IsNullOrWhiteSpace(resolution.RelativePath)
                ? resolution.RelativePath
                : null;
        }

        var executableManifest = manifests.FirstOrDefault(m => m.ContentType == ContentType.Executable);
        if (executableManifest != null)
        {
            var resolution = ManifestVariantResolver.ResolveEntryPoint(executableManifest);
            return resolution.Success && !string.IsNullOrWhiteSpace(resolution.RelativePath)
                ? resolution.RelativePath
                : null;
        }

        return null;
    }

    private static string UpdateGameDataCameraSettings(
        string content,
        float? cameraHeight,
        float? cameraMaxHeight,
        float? cameraMinHeight,
        float? cameraPitch)
    {
        if (content.StartsWith("; GenHub Camera Override\r\n", StringComparison.OrdinalIgnoreCase))
        {
            content = content.Substring("; GenHub Camera Override\r\n".Length);
        }
        else if (content.StartsWith("; GenHub Camera Override\n", StringComparison.OrdinalIgnoreCase))
        {
            content = content.Substring("; GenHub Camera Override\n".Length);
        }

        content = UpdateOrInsertIniValue(content, "CameraHeight", cameraHeight ?? GameSettingsConstants.Camera.DefaultHeight);
        content = UpdateOrInsertIniValue(content, "MaxCameraHeight", cameraMaxHeight ?? GameSettingsConstants.Camera.DefaultMaxHeight);
        content = UpdateOrInsertIniValue(content, "MinCameraHeight", cameraMinHeight ?? GameSettingsConstants.Camera.DefaultMinHeight);
        content = UpdateOrInsertIniValue(content, "CameraPitch", cameraPitch ?? GameSettingsConstants.Camera.DefaultPitch);

        return content;
    }

    private static string UpdateOrInsertIniValue(string content, string key, float value)
    {
        var formattedVal = value.ToString("0.0#", CultureInfo.InvariantCulture);
        var pattern = @"^([ \t]*" + Regex.Escape(key) + @"[ \t]*=[ \t]*)[^\r\n]*";
        var regex = new Regex(pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

        if (regex.IsMatch(content))
        {
            return regex.Replace(content, "${1}" + formattedVal, 1);
        }

        var gameDataHeader = new Regex(@"^([ \t]*GameData[ \t]*\r?\n)", RegexOptions.Multiline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (gameDataHeader.IsMatch(content))
        {
            return gameDataHeader.Replace(content, "${1}  " + key + " = " + formattedVal + "\r\n", 1);
        }

        return content + "\r\nGameData\r\n  " + key + " = " + formattedVal + "\r\nEnd\r\n";
    }

    /// <summary>
    /// Validates dependencies, applies initial INI settings, and performs preflight CAS checks for the profile.
    /// </summary>
    private async Task<LaunchOperationResult<List<ContentManifest>>> PrepareManifestsAndPreflightAsync(
        GameProfile profile,
        IProgress<LaunchProgress>? progress,
        string launchId,
        CancellationToken cancellationToken)
    {
        progress?.Report(new LaunchProgress { Phase = LaunchPhase.ValidatingProfile, PercentComplete = 0 });
        progress?.Report(new LaunchProgress { Phase = LaunchPhase.ResolvingContent, PercentComplete = 10 });

        var resolutionResult = await ResolveContentManifestsAsync(profile, cancellationToken);
        if (!resolutionResult.Success || resolutionResult.Data == null)
        {
            return LaunchOperationResult<List<ContentManifest>>.CreateFailure(
                resolutionResult.FirstError ?? "Failed to resolve content dependencies.",
                launchId,
                profile.Id);
        }

        var manifests = resolutionResult.Data;
        logger.LogDebug("[GameLauncher] Applying profile settings to Options.ini before workspace preparation");
        await ApplyProfileSettingsToIniOptionsAsync(profile);

        progress?.Report(new LaunchProgress { Phase = LaunchPhase.PreparingWorkspace, PercentComplete = 20 });

        logger.LogDebug("[GameLauncher] Running CAS preflight check");
        var casCheckResult = await PreflightCasCheckAsync(manifests, cancellationToken);
        if (!casCheckResult.Success)
        {
            logger.LogError("[GameLauncher] CAS preflight check failed: {Error}", casCheckResult.FirstError);
            return LaunchOperationResult<List<ContentManifest>>.CreateFailure(
                casCheckResult.FirstError ?? "CAS preflight check failed",
                launchId,
                profile.Id);
        }

        logger.LogDebug("[GameLauncher] CAS preflight check passed");
        return LaunchOperationResult<List<ContentManifest>>.CreateSuccess(manifests, launchId, profile.Id);
    }

    /// <summary>
    /// Disables Steam integration if the profile's executable uses a non-retail or native platform format.
    /// </summary>
    private bool AdjustSteamLaunchForExecutable(bool isSteamLaunch, string profileId, string? executablePath)
    {
        if (isSteamLaunch && ReplayCrcMatchingHelper.HasNonRetailExecutableFormat(executablePath))
        {
            logger.LogInformation(
                "[GameLauncher] Disabling Steam integration for profile {ProfileId} because executable '{Path}' uses a non-retail or native platform format",
                profileId,
                executablePath);
            return false;
        }

        return isSteamLaunch;
    }

    /// <summary>
    /// Re-evaluates Steam launch eligibility for the final resolved executable, releasing the lock if revoked.
    /// </summary>
    private bool ReevaluateSteamLaunch(
        bool isSteamLaunch,
        string profileId,
        string finalExecutablePath,
        ref IDisposable? steamInstallationLock)
    {
        var adjusted = AdjustSteamLaunchForExecutable(isSteamLaunch, profileId, finalExecutablePath);
        if (isSteamLaunch && !adjusted)
        {
            steamInstallationLock?.Dispose();
            steamInstallationLock = null;
            return false;
        }

        return adjusted;
    }

    private async Task<LaunchOperationResult<GameLaunchInfo>> LaunchProfileAsync(GameProfile profile, bool skipUserDataCleanup, IReadOnlyDictionary<string, string>? additionalArguments, IProgress<LaunchProgress>? progress, string launchId, CancellationToken cancellationToken)
    {
        IDisposable? steamInstallationLock = null;

        try
        {
            logger.LogInformation("[GameLauncher] === Starting launch for profile '{ProfileName}' (ID: {ProfileId}) ===", profile.Name, profile.Id);
            cancellationToken.ThrowIfCancellationRequested();

            var preflightResult = await PrepareManifestsAndPreflightAsync(profile, progress, launchId, cancellationToken);
            if (!preflightResult.Success || preflightResult.Data == null)
            {
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(
                    preflightResult.FirstError ?? "Failed to resolve content dependencies.",
                    launchId,
                    profile.Id);
            }

            var manifests = preflightResult.Data;

            var manifestSourcePaths = await ManifestSourcePathResolver.ResolveManifestSourcePathsAsync(manifests, profile, manifestPool, logger, cancellationToken);
            var installResult = await ResolveInstallationAndPathsAsync(profile, cancellationToken);
            if (!installResult.Success)
            {
                await launchRegistry.UnregisterLaunchAsync(launchId);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(installResult.FirstError ?? "Installation not configured.", launchId, profile.Id);
            }

            var (installation, gameClient, actualInstallationPath, dynamicWorkspacePath, isSteamLaunch) = installResult.Data;

            var candidateExecutable = TryResolveManifestExecutablePath(manifests) ?? gameClient.ExecutablePath;
            isSteamLaunch = AdjustSteamLaunchForExecutable(isSteamLaunch, profile.Id, candidateExecutable);

            // Reconciliation removes the prior receipt, so compare it before preparation.
            var receiptDriftWarnings = new List<string>();
            var previousReceipt = await RevalidateLaunchReceiptAsync(
                Path.Combine(dynamicWorkspacePath, profile.Id), profile.Id, receiptDriftWarnings, cancellationToken);

            var workspaceSetupResult = await SetupAndAcquireWorkspaceAsync(
                profile,
                manifests,
                gameClient,
                installation,
                actualInstallationPath,
                dynamicWorkspacePath,
                isSteamLaunch,
                manifestSourcePaths,
                progress,
                cancellationToken);

            if (!workspaceSetupResult.Success)
            {
                await launchRegistry.UnregisterLaunchAsync(launchId);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(workspaceSetupResult.FirstError ?? "Workspace preparation failed", launchId, profile.Id);
            }

            var (workspaceInfo, acquiredLock) = workspaceSetupResult.Data;
            steamInstallationLock = acquiredLock;

            progress?.Report(new LaunchProgress { Phase = LaunchPhase.PreparingUserData, PercentComplete = 82 });
            var userDataPrepResult = await PrepareUserDataAsync(profile, manifests, skipUserDataCleanup, cancellationToken);
            if (!userDataPrepResult.Success)
            {
                steamInstallationLock?.Dispose();
                steamInstallationLock = null;
                await launchRegistry.UnregisterLaunchAsync(launchId);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(userDataPrepResult.FirstError ?? "User data preparation failed", launchId, profile.Id);
            }

            progress?.Report(new LaunchProgress { Phase = LaunchPhase.Starting, PercentComplete = 90 });
            var executableResult = ResolveAndValidateExecutablePath(profile, workspaceInfo);
            if (!executableResult.Success || executableResult.Data == null)
            {
                await launchRegistry.UnregisterLaunchAsync(launchId);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(executableResult.FirstError ?? "No executable path available", launchId, profile.Id);
            }

            var finalExecutablePath = executableResult.Data;
            isSteamLaunch = ReevaluateSteamLaunch(isSteamLaunch, profile.Id, finalExecutablePath, ref steamInstallationLock);

            var prepResult = await PrepareLaunchConfigurationAndProxyAsync(
                profile,
                installation,
                manifests,
                actualInstallationPath,
                finalExecutablePath,
                workspaceInfo,
                isSteamLaunch,
                additionalArguments,
                cancellationToken);

            if (!prepResult.Success || prepResult.Data.LaunchConfig == null)
            {
                await launchRegistry.UnregisterLaunchAsync(launchId);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(prepResult.FirstError ?? "Launch configuration failed", launchId, profile.Id);
            }

            var (launchConfig, steamPrep, steamAppId) = prepResult.Data;
            var effectiveStrategy = profile.WorkspaceStrategy ?? configurationProvider.GetDefaultWorkspaceStrategy();

            var receiptContext = BuildLaunchReceiptContext(profile, gameClient, workspaceInfo, launchConfig, manifests, launchId, installation);
            AppendConfigurationDrift(profile.Id, previousReceipt, receiptContext, receiptDriftWarnings);

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
                await launchRegistry.UnregisterLaunchAsync(launchId);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(processResult.FirstError ?? "Process start failed", launchId, profile.Id);
            }

            var processInfo = processResult.Data;
            logger.LogInformation("[GameLauncher] Process started successfully - PID: {ProcessId}", processInfo.ProcessId);

            // Update the placeholder launch entry with real process info
            // (The placeholder was registered earlier to prevent deletion during launch)
            var launchInfo = new GameLaunchInfo
            {
                LaunchId = launchId,
                ProfileId = profile.Id,
                WorkspaceId = workspaceInfo.Id,
                ProcessInfo = processInfo,
                LaunchedAt = DateTime.UtcNow,
                ReceiptDriftWarnings = receiptDriftWarnings,
            };
            logger.LogDebug("[GameLauncher] Updating launch registry with real process info");
            await launchRegistry.RegisterLaunchAsync(launchInfo);
            await RecordLaunchReceiptAsync(receiptContext);

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

    private async Task<OperationResult<(WorkspaceInfo Workspace, IDisposable? SteamLock)>> SetupAndAcquireWorkspaceAsync(
        GameProfile profile,
        List<ContentManifest> manifests,
        GenHub.Core.Models.GameClients.GameClient gameClient,
        GameInstallation installation,
        string actualInstallationPath,
        string dynamicWorkspacePath,
        bool isSteamLaunch,
        Dictionary<string, string> manifestSourcePaths,
        IProgress<LaunchProgress>? progress,
        CancellationToken cancellationToken)
    {
        IDisposable? steamInstallationLock = null;
        try
        {
            if (isSteamLaunch)
            {
                steamInstallationLock = await AcquireSteamInstallationLockAsync(actualInstallationPath, cancellationToken);
                logger.LogInformation("[GameLauncher] Steam launch detected - workspace will be adjacent to installation in .genhub-workspace directory");
            }

            logger.LogDebug("[GameLauncher] Using dynamic workspace path: {WorkspacePath} (Installation: {InstallPath})", dynamicWorkspacePath, actualInstallationPath);

            var workspaceResult = await PrepareWorkspaceForLaunchAsync(
                profile,
                manifests,
                gameClient,
                installation,
                actualInstallationPath,
                dynamicWorkspacePath,
                isSteamLaunch,
                manifestSourcePaths,
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

    private void ApplyResolutionFromNativeOptionsIni(GameProfile profile, Dictionary<string, string> arguments)
    {
        if (arguments.ContainsKey(GameClientConstants.XResolutionArgument)
            && arguments.ContainsKey(GameClientConstants.YResolutionArgument))
        {
            return;
        }

        var nativeOptionsPath = TryGetNativeOptionsIniPath(profile.GameClient?.GameType);
        if (string.IsNullOrEmpty(nativeOptionsPath) || !File.Exists(nativeOptionsPath))
        {
            return;
        }

        var (width, height) = TryReadResolutionFromOptionsIni(nativeOptionsPath);
        TryApplyResolutionArgument(arguments, GameClientConstants.XResolutionArgument, width);
        TryApplyResolutionArgument(arguments, GameClientConstants.YResolutionArgument, height);
    }

    private void TryApplyResolutionArgument(Dictionary<string, string> arguments, string argumentName, int value)
    {
        if (value <= 0 || arguments.ContainsKey(argumentName))
        {
            return;
        }

        arguments[argumentName] = value.ToString(CultureInfo.InvariantCulture);
        logger.LogInformation("[GameLauncher] Added {Argument} argument from Options.ini: {Value}", argumentName, value);
    }

    private async Task<OperationResult<(GameLaunchConfiguration LaunchConfig, SteamLaunchPrepResult? SteamPrep, string? SteamAppId)>> PrepareLaunchConfigurationAndProxyAsync(
        GameProfile profile,
        GameInstallation installation,
        IReadOnlyList<ContentManifest> manifests,
        string actualInstallationPath,
        string finalExecutablePath,
        WorkspaceInfo workspaceInfo,
        bool isSteamLaunch,
        IReadOnlyDictionary<string, string>? additionalArguments,
        CancellationToken cancellationToken)
    {
        var argsResult = BuildCommandLineArguments(profile, additionalArguments);
        if (!argsResult.Success || argsResult.Data == null)
        {
            return OperationResult<(GameLaunchConfiguration, SteamLaunchPrepResult?, string?)>.CreateFailure(argsResult.FirstError ?? "Invalid command line arguments");
        }

        var arguments = argsResult.Data;
        ApplyResolutionFromNativeOptionsIni(profile, arguments);
        SanitizeMapCache(profile.GameClient?.GameType);

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

        var targetGame = profile.GameClient?.GameType ?? GameType.Generals;
        var archiveRootError = ValidateRetailArchiveRoots(launchConfig.EnvironmentVariables, installation, targetGame);
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
        GameInstallation installation,
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
            ForceRecreate = isSteamLaunch,
            WorkspaceRootPath = dynamicWorkspacePath,
            BaseInstallationPath = actualInstallationPath,
            ManifestSourcePaths = manifestSourcePaths,
            SupplementalArchiveRoot = ResolveSupplementalArchiveRootForWorkspace(gameClient.GameType, installation.EffectiveGeneralsArchivePath, profile.EnvironmentVariables),
        };
        logger.LogDebug("[GameLauncher] BaseInstallationPath set to: {Path}", workspaceConfig.BaseInstallationPath);

        if (!string.IsNullOrEmpty(actualInstallationPath))
        {
            var backupPath = Path.Combine(actualInstallationPath, $"{GameClientConstants.GeneralsExecutable}{FileTypes.BackupExtension}");
            if (isSteamLaunch || File.Exists(backupPath))
            {
                var cleanupResult = await PerformPreLaunchSteamCleanupAsync(actualInstallationPath, cancellationToken);
                if (!cleanupResult.Success)
                {
                    if (isSteamLaunch)
                    {
                        return OperationResult<WorkspaceInfo>.CreateFailure(cleanupResult.FirstError ?? "Pre-launch Steam cleanup failed");
                    }

                    logger.LogWarning(
                        "[GameLauncher] Pre-launch cleanup for installation at '{Path}' reported failure: {Error}",
                        actualInstallationPath,
                        cleanupResult.FirstError ?? "Unknown error");
                }
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

        var workspaceResult = await workspaceManager.PrepareWorkspaceAsync(workspaceConfig, workspaceProgress, skipCleanup: isSteamLaunch, cancellationToken);
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

        await ApplyCameraSettingsAsync(profile, workspaceInfo.WorkspacePath, actualInstallationPath, cancellationToken);

        return OperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo);
    }

    private async Task<OperationResult<bool>> PrepareUserDataAsync(
        GameProfile profile,
        List<ContentManifest> manifests,
        bool skipUserDataCleanup,
        CancellationToken cancellationToken)
    {
        var gameType = profile.GameClient?.GameType ?? GameType.ZeroHour;
        SanitizeMapCache(gameType);
        var previousActiveProfileId = profileContentLinker.GetActiveProfileId(gameType);
        try
        {
            logger.LogDebug(
                "[GameLauncher] Switching user data from profile {OldProfile} to {NewProfile}",
                previousActiveProfileId ?? "(none)",
                profile.Id);
            var userDataResult = await profileContentLinker.SwitchProfileUserDataAsync(
                previousActiveProfileId,
                profile.Id,
                manifests,
                gameType,
                skipUserDataCleanup,
                cancellationToken);
            if (!userDataResult.Success)
            {
                logger.LogError("[GameLauncher] User data preparation failed: {Error}", userDataResult.FirstError);
                return OperationResult<bool>.CreateFailure(userDataResult.FirstError ?? "User data preparation failed");
            }

            logger.LogInformation("[GameLauncher] User data content prepared for profile {ProfileId}", profile.Id);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[GameLauncher] Unexpected error in user data linkage for profile {ProfileId}", profile.Id);
            return OperationResult<bool>.CreateFailure($"Failed to prepare user data: {ex.Message}");
        }
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
            GameType = profile.GameClient?.GameType,
            NativeOptionsIniPath = TryGetNativeOptionsIniPath(profile.GameClient?.GameType),
        };
    }

    private string? TryGetNativeOptionsIniPath(GameType? gameType)
    {
        if (gameType is null)
        {
            return null;
        }

        try
        {
            return gameSettingsService.GetOptionsFilePath(gameType.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogDebug(ex, "[GameLauncher] Could not resolve native Options.ini path for {GameType}", gameType);
            return null;
        }
    }

    private void SanitizeMapCache(GameType? gameType)
    {
        if (gameType is null)
        {
            return;
        }

        try
        {
            var nativeOptionsPath = TryGetNativeOptionsIniPath(gameType);
            if (string.IsNullOrEmpty(nativeOptionsPath))
            {
                return;
            }

            var userDataDir = Path.GetDirectoryName(nativeOptionsPath);
            if (string.IsNullOrEmpty(userDataDir) || !Directory.Exists(userDataDir))
            {
                return;
            }

            var mapCachePath = Path.Combine(userDataDir, "Maps", "MapCache.ini");
            TrySanitizeMapCacheFile(mapCachePath, logger);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[GameLauncher] Failed to resolve MapCache.ini path for {GameType}", gameType);
        }
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

        var steamUrl = $"{SteamConstants.RunGameIdUrlPrefix}{steamAppId}";
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
        var enabledIds = profile.EnabledContentIds ?? [];
        logger.LogInformation(
            "[GameLauncher] Resolving {Count} enabled content IDs for profile '{ProfileName}' (ID: {ProfileId}): [{ContentIds}]",
            enabledIds.Count,
            profile.Name,
            profile.Id,
            string.Join(", ", enabledIds));

        var resolutionResult = await dependencyResolver.ResolveDependenciesWithManifestsAsync(enabledIds, cancellationToken);
        if (!resolutionResult.Success)
        {
            logger.LogError(
                "[GameLauncher] Failed to resolve content dependencies for profile '{ProfileName}' (ID: {ProfileId}): {Error}. Requested IDs: [{RequestedIds}]",
                profile.Name,
                profile.Id,
                resolutionResult.FirstError,
                string.Join(", ", enabledIds));
            return OperationResult<List<ContentManifest>>.CreateFailure($"Failed to resolve content dependencies: {resolutionResult.FirstError}");
        }

        if (resolutionResult.Warnings?.Any() == true)
        {
            foreach (var warning in resolutionResult.Warnings)
            {
                logger.LogWarning("[GameLauncher] Dependency resolution warning for profile '{ProfileName}': {Warning}", profile.Name, warning);
            }
        }

        var manifests = resolutionResult.ResolvedManifests.ToList();
        logger.LogInformation(
            "[GameLauncher] Successfully resolved {Count} manifests for profile '{ProfileName}' (from {EnabledCount} enabled IDs): [{ManifestSummaries}]",
            manifests.Count,
            profile.Name,
            enabledIds.Count,
            string.Join(", ", manifests.Select(m => $"{m.Id.Value} ('{m.Name}')")));

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
        var isSteamLaunch = profile.UseSteamLaunch == true && ReplayCrcMatchingHelper.IsSteamLaunchEligible(installation.InstallationType, gameClient);

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
            Path.Combine(workspacePath, DataDirectoryName, MoviesDirectoryName, EaLogoBik),
            Path.Combine(workspacePath, DataDirectoryName, EnglishDirectoryName, MoviesDirectoryName, EaLogoBik),
            Path.Combine(workspacePath, MoviesDirectoryName, EaLogoBik),
            Path.Combine(workspacePath, LowerDataDirectoryName, LowerMoviesDirectoryName, EaLogoBik),
            Path.Combine(workspacePath, DataDirectoryName, MoviesDirectoryName, EaLogo640Bik),
            Path.Combine(workspacePath, DataDirectoryName, EnglishDirectoryName, MoviesDirectoryName, EaLogo640Bik),
            Path.Combine(workspacePath, MoviesDirectoryName, EaLogo640Bik),
            Path.Combine(workspacePath, LowerDataDirectoryName, LowerMoviesDirectoryName, EaLogo640Bik),
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
                    logger.LogWarning(ex, "[GameLauncher] Failed to delete EA_LOGO.BIK at {LogoPath}", logoPath);
                }
            }
        }

        if (!deleted)
        {
            logger.LogWarning("[GameLauncher] Skip EA Logo enabled but EA_LOGO.BIK not found in workspace. Checked paths: {Paths}", string.Join(", ", possiblePaths));
        }
    }

    private OperationResult<Dictionary<string, string>> BuildCommandLineArguments(
        GameProfile profile,
        IReadOnlyDictionary<string, string>? additionalArguments)
    {
        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(profile.CommandLineArguments))
        {
            var parseResult = PopulateCommandLineArguments(profile.CommandLineArguments, arguments);
            if (!parseResult.Success)
            {
                return OperationResult<Dictionary<string, string>>.CreateFailure(parseResult.FirstError ?? "Invalid command argument");
            }
        }

        foreach (var kvp in profile.LaunchOptions)
        {
            arguments[kvp.Key] = kvp.Value;
        }

        if (additionalArguments != null)
        {
            var mergeResult = MergeAdditionalArguments(additionalArguments, arguments);
            if (!mergeResult.Success)
            {
                return OperationResult<Dictionary<string, string>>.CreateFailure(mergeResult.FirstError ?? "Invalid additional arguments");
            }
        }

        if (profile.VideoWindowed == true && !arguments.ContainsKey(GameClientConstants.WindowedArgument))
        {
            arguments[GameClientConstants.WindowedArgument] = string.Empty;
            logger.LogInformation("[GameLauncher] Added {Argument} argument for windowed mode", GameClientConstants.WindowedArgument);
        }

        if (profile.VideoResolutionWidth > 0 && !arguments.ContainsKey(GameClientConstants.XResolutionArgument))
        {
            arguments[GameClientConstants.XResolutionArgument] = profile.VideoResolutionWidth.Value.ToString(CultureInfo.InvariantCulture);
            logger.LogInformation("[GameLauncher] Added {Argument} argument: {Width}", GameClientConstants.XResolutionArgument, profile.VideoResolutionWidth.Value);
        }

        if (profile.VideoResolutionHeight > 0 && !arguments.ContainsKey(GameClientConstants.YResolutionArgument))
        {
            arguments[GameClientConstants.YResolutionArgument] = profile.VideoResolutionHeight.Value.ToString(CultureInfo.InvariantCulture);
            logger.LogInformation("[GameLauncher] Added {Argument} argument: {Height}", GameClientConstants.YResolutionArgument, profile.VideoResolutionHeight.Value);
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

        var targetArguments = arguments.Select(kvp => FormatCommandLineArgument(kvp.Key, kvp.Value)).ToArray();

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

    /// <summary>
    /// Cheaply revalidates the previous launch receipt against the filesystem, if any, and
    /// logs a warning naming each drifted field. Drift never blocks the launch; blocking on
    /// a misconfigured root remains the job of <see cref="ValidateRetailArchiveRoots"/>.
    /// </summary>
    /// <param name="workspacePath">The workspace directory the receipt would live in.</param>
    /// <param name="profileId">The profile being launched.</param>
    /// <param name="driftWarnings">Collects the drifted fields for the launch result.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The parsed receipt, when one was present and readable, for the later configuration comparison.</returns>
    private async Task<LaunchReceipt?> RevalidateLaunchReceiptAsync(string workspacePath, string profileId, List<string> driftWarnings, CancellationToken cancellationToken)
    {
        try
        {
            var driftResult = await launchReceiptService.RevalidateAsync(workspacePath, cancellationToken);
            if (!driftResult.Success)
            {
                logger.LogWarning("[GameLauncher] Launch receipt revalidation failed: {Error}", driftResult.FirstError);
                return null;
            }

            if (driftResult.Data is not { HasReceipt: true } driftReport)
            {
                return null;
            }

            if (!driftReport.HasDrift)
            {
                logger.LogDebug("[GameLauncher] Launch receipt for profile {ProfileId} matches the current state", profileId);
            }

            LogReceiptDrift(profileId, driftReport);
            driftWarnings.AddRange(driftReport.DriftedFields);
            return driftReport.Receipt;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[GameLauncher] Receipt revalidation failed for profile {ProfileId}", profileId);
            driftWarnings.Add(LaunchReceiptConstants.RevalidationWarningKey);
            return null;
        }
    }

    /// <summary>Appends configuration changes when a previous receipt is available.</summary>
    /// <param name="profileId">The profile being launched.</param>
    /// <param name="previousReceipt">The previous receipt, if any.</param>
    /// <param name="context">The upcoming launch configuration.</param>
    /// <param name="warnings">The accumulated drift warnings.</param>
    private void AppendConfigurationDrift(string profileId, LaunchReceipt? previousReceipt, LaunchReceiptContext context, List<string> warnings)
    {
        if (previousReceipt is null)
        {
            return;
        }

        var drift = launchReceiptService.CompareUpcomingLaunch(previousReceipt, context);
        LogReceiptDrift(profileId, drift);
        warnings.AddRange(drift.DriftedFields);
    }

    /// <summary>
    /// Logs a structured warning per drifted field.
    /// </summary>
    /// <param name="profileId">The profile being launched.</param>
    /// <param name="driftReport">The report to log.</param>
    private void LogReceiptDrift(string profileId, LaunchReceiptDriftReport driftReport)
    {
        foreach (var driftedField in driftReport.DriftedFields)
        {
            logger.LogWarning(
                "[GameLauncher] Launch receipt drift for profile {ProfileId}: {DriftedField}",
                profileId,
                driftedField);
        }
    }

    /// <summary>
    /// Records a receipt of what this launch consisted of into the workspace. A failure to
    /// record is logged and never fails a launch that has already started.
    /// </summary>
    /// <remarks>
    /// Deliberately not passed the launch cancellation token: the child process is already
    /// running by the time this is called, so cancelling the launch operation must not abandon
    /// the write half-done, and the resulting <see cref="OperationCanceledException"/> must not
    /// reach the caller's catch and report a running game as a failed launch. Everything else
    /// is caught here for the same reason.
    /// </remarks>
    /// <param name="receiptContext">What the launch consisted of.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task RecordLaunchReceiptAsync(LaunchReceiptContext receiptContext)
    {
        try
        {
            var receiptResult = await launchReceiptService.RecordLaunchAsync(receiptContext, CancellationToken.None);
            if (!receiptResult.Success)
            {
                logger.LogWarning("[GameLauncher] Failed to record launch receipt: {Error}", receiptResult.FirstError);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[GameLauncher] Failed to record launch receipt for profile {ProfileId}", receiptContext.ProfileId);
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

    private void LogMissingCasFile(ContentManifest manifest, ManifestFile file)
    {
        logger.LogWarning(
            "[Preflight CAS] Missing CAS object {Hash} for file {RelativePath} in manifest {ManifestId} ({ManifestName})",
            file.Hash,
            file.RelativePath,
            manifest.Id,
            manifest.Name);
    }

    /// <summary>
    /// Performs a preflight check to ensure all CAS content required by the manifests is available.
    /// </summary>
    /// <param name="manifests">The manifests to check.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    private async Task<OperationResult<bool>> PreflightCasCheckAsync(IEnumerable<ContentManifest> manifests, CancellationToken cancellationToken)
    {
        var messageFormat = localizationService?.TryGetString(ProfileValidationConstants.MissingCasObjectsMessageKey, out var localized) == true
            ? localized
            : ProfileValidationConstants.MissingCasObjectsMessage;
        return await casService.VerifyRequiredCasContentAvailableAsync(manifests, messageFormat, LogMissingCasFile, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies profile camera height and pitch settings to GameData.ini in the workspace for non-GeneralsOnline profiles.
    /// If the profile has no custom camera settings, any previously generated GenHub camera override is cleaned up.
    /// </summary>
    private async Task ApplyCameraSettingsAsync(
        GameProfile profile,
        string workspacePath,
        string actualInstallationPath,
        CancellationToken cancellationToken)
    {
        try
        {
            if (profile.IsGeneralsOnlineProfile())
            {
                return;
            }

            var iniDir = Path.Combine(workspacePath, "Data", "INI");
            var iniPath = Path.Combine(iniDir, "GameData.ini");

            if (profile.HasCustomCameraSettings())
            {
                await ApplyCustomCameraSettingsAsync(profile, iniDir, iniPath, workspacePath, actualInstallationPath, cancellationToken);
            }
            else
            {
                await TryRemoveCameraOverrideAsync(iniPath, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[GameLauncher] Failed to apply camera settings for profile {ProfileId}", profile.Id);
        }
    }

    private async Task ApplyCustomCameraSettingsAsync(
        GameProfile profile,
        string iniDir,
        string iniPath,
        string workspacePath,
        string actualInstallationPath,
        CancellationToken cancellationToken)
    {
        string? iniContent = null;
        if (File.Exists(iniPath))
        {
            iniContent = await File.ReadAllTextAsync(iniPath, cancellationToken);
        }
        else
        {
            iniContent = TryExtractGameDataIniFromBig(workspacePath) ??
                         TryExtractGameDataIniFromBig(actualInstallationPath);
        }

        if (string.IsNullOrEmpty(iniContent))
        {
            iniContent = "GameData\r\nEnd\r\n";
        }

        var updatedContent = UpdateGameDataCameraSettings(
            iniContent,
            profile.CameraHeight,
            profile.CameraMaxHeight,
            profile.CameraMinHeight,
            profile.CameraPitch);

        Directory.CreateDirectory(iniDir);
        var tmpPath = iniPath + ".genhub-tmp";
        try
        {
            await File.WriteAllTextAsync(tmpPath, "; GenHub Camera Override\r\n" + updatedContent, cancellationToken);
            File.Move(tmpPath, iniPath, overwrite: true);
            logger.LogInformation("[GameLauncher] Applied custom camera settings to {IniPath}", iniPath);
        }
        finally
        {
            if (File.Exists(tmpPath))
            {
                try
                {
                    File.Delete(tmpPath);
                }
                catch
                {
                    // Ignore cleanup errors for temporary staging file
                }
            }
        }
    }

    private async Task TryRemoveCameraOverrideAsync(string iniPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(iniPath))
        {
            return;
        }

        var lines = await File.ReadAllLinesAsync(iniPath, cancellationToken);
        var firstLine = lines.Length > 0 ? lines[0] : null;
        if (firstLine != null && firstLine.StartsWith("; GenHub Camera Override", StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(iniPath);
            logger.LogInformation("[GameLauncher] Removed GenHub camera override from {IniPath} to restore default camera settings", iniPath);
        }
    }

    private string? TryExtractGameDataIniFromBig(string searchDirectory)
    {
        if (string.IsNullOrWhiteSpace(searchDirectory) || !Directory.Exists(searchDirectory))
        {
            return null;
        }

        var candidateFiles = new List<string>();
        var inizh = Path.Combine(searchDirectory, "INIZH.big");
        if (File.Exists(inizh)) candidateFiles.Add(inizh);
        var ini = Path.Combine(searchDirectory, "INI.big");
        if (File.Exists(ini)) candidateFiles.Add(ini);

        try
        {
            var extraBigs = Directory.EnumerateFiles(searchDirectory, "*.big", SearchOption.TopDirectoryOnly)
                .Where(f => !candidateFiles.Contains(f, StringComparer.OrdinalIgnoreCase));
            candidateFiles.AddRange(extraBigs);
        }
        catch
        {
            // Non-critical directory enumeration fallback
        }

        return candidateFiles
            .Select(TryReadGameDataIniFromArchive)
            .FirstOrDefault(content => content != null);
    }

    private string? TryReadGameDataIniFromArchive(string bigPath)
    {
        if (!BigArchiveReader.TryReadIndex(bigPath, out var entries))
        {
            return null;
        }

        var entry = entries.Values.FirstOrDefault(e => e.Path.EndsWith("gamedata.ini", StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            return null;
        }

        try
        {
            var data = BigArchiveReader.ReadEntryData(entry);
            return Encoding.UTF8.GetString(data);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[GameLauncher] Failed to read GameData.ini entry from {BigPath}", bigPath);
            return null;
        }
    }
}
