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
    ISteamLauncher steamLauncher) : IGameLauncher
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _profileLaunchLocks = new();
    private static readonly SearchValues<char> InvalidArgChars = SearchValues.Create(";|&\n\r`$%");

    /// <inheritdoc/>
    public async Task<IDisposable> AcquireProfileLockAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var semaphore = _profileLaunchLocks.GetOrAdd(profileId, _ => new SemaphoreSlim(1, 1));
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
                else
                {
                    // Process is not running but launch record exists - clean it up
                    logger.LogWarning(
                        "Launch record {LaunchId} for profile {ProfileId} exists but process {ProcessId} is not running - cleaning up",
                        activeLaunch.LaunchId,
                        profile.Id,
                        activeLaunch.ProcessInfo.ProcessId);
                    activeLaunch.TerminatedAt = DateTime.UtcNow;
                    await launchRegistry.UnregisterLaunchAsync(activeLaunch.LaunchId);
                }
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

    private async Task<LaunchOperationResult<GameLaunchInfo>> LaunchProfileAsync(GameProfile profile, bool skipUserDataCleanup, IProgress<LaunchProgress>? progress, string launchId, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("[GameLauncher] === Starting launch for profile '{ProfileName}' (ID: {ProfileId}) ===", profile.Name, profile.Id);

            // Check for cancellation early
            cancellationToken.ThrowIfCancellationRequested();

            // Report validating profile
            progress?.Report(new LaunchProgress { Phase = LaunchPhase.ValidatingProfile, PercentComplete = 0 });

            // Resolve content manifests WITH dependencies
            // This ensures that when a GameClient depends on a MapPack, the MapPack is included
            logger.LogDebug("[GameLauncher] Resolving {Count} enabled content manifests with dependencies", profile.EnabledContentIds?.Count ?? 0);
            progress?.Report(new LaunchProgress { Phase = LaunchPhase.ResolvingContent, PercentComplete = 10 });
            var enabledIds = profile.EnabledContentIds ?? Enumerable.Empty<string>();
            var resolutionResult = await dependencyResolver.ResolveDependenciesWithManifestsAsync(enabledIds, cancellationToken);
            if (!resolutionResult.Success)
            {
                logger.LogError("[GameLauncher] Failed to resolve content dependencies: {Error}", resolutionResult.FirstError);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure($"Failed to resolve content dependencies: {resolutionResult.FirstError}", launchId, profile.Id);
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

            logger.LogDebug(
                "[GameLauncher] Profile GameClient - Name: {Name}, WorkingDir: {WorkingDir}",
                profile.GameClient?.Name ?? "null",
                profile.GameClient?.WorkingDirectory ?? "null");
            logger.LogDebug("[GameLauncher] Applying profile settings to Options.ini before workspace preparation");
            await ApplyProfileSettingsToIniOptionsAsync(profile);

            // Prepare workspace
            progress?.Report(new LaunchProgress { Phase = LaunchPhase.PreparingWorkspace, PercentComplete = 20 });

            // Preflight check: ensure all CAS content is available
            logger.LogDebug("[GameLauncher] Running CAS preflight check");
            var casCheckResult = await PreflightCasCheckAsync(manifests, cancellationToken);
            if (!casCheckResult.Success)
            {
                logger.LogError("[GameLauncher] CAS preflight check failed: {Error}", casCheckResult.FirstError);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(casCheckResult.FirstError ?? "CAS preflight check failed", launchId, profile.Id);
            }

            logger.LogDebug("[GameLauncher] CAS preflight check passed");

            // Resolve source paths for all manifests
            var manifestSourcePaths = new Dictionary<string, string>();
            foreach (var manifest in manifests)
            {
                // Skip GameInstallation manifests - they use BaseInstallationPath
                if (manifest.ContentType == ContentType.GameInstallation)
                {
                    continue;
                }

                // For GameClient, use WorkingDirectory if available
                if (manifest.ContentType == ContentType.GameClient &&
                    !string.IsNullOrEmpty(profile.GameClient?.WorkingDirectory))
                {
                    manifestSourcePaths[manifest.Id.Value] = profile.GameClient.WorkingDirectory;
                    logger.LogDebug("[GameLauncher] Source path for GameClient {ManifestId}: {SourcePath}", manifest.Id.Value, profile.GameClient.WorkingDirectory);
                    continue;
                }

                // For all other content types, query the manifest pool for the content directory
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
                else
                {
                    logger.LogWarning(
                        "[GameLauncher] Could not resolve source path for manifest {ManifestId} ({ContentType})",
                        manifest.Id.Value,
                        manifest.ContentType);
                }
            }

            // Resolve the installation
            var installationResult = await gameInstallationService.GetInstallationAsync(profile.GameInstallationId, cancellationToken);
            if (!installationResult.Success || installationResult.Data == null)
            {
                logger.LogError("[GameLauncher] Failed to resolve game installation for profile {ProfileId}", profile.Id);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure("Failed to resolve game installation.", launchId, profile.Id);
            }

            var installation = installationResult.Data;
            var gameClient = profile.GameClient;
            if (gameClient == null)
            {
                logger.LogError("[GameLauncher] GameClient is not set for profile {ProfileId}", profile.Id);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure("GameClient not configured for profile.", launchId, profile.Id);
            }

            var actualInstallationPath = gameClient.GameType == GameType.Generals
                ? installation.GeneralsPath ?? string.Empty
                : installation.ZeroHourPath ?? string.Empty;
            if (string.IsNullOrEmpty(actualInstallationPath))
            {
                logger.LogError("[GameLauncher] Installation path is not set for {GameType}", gameClient.GameType);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure("Installation path not found.", launchId, profile.Id);
            }

            // Use dynamic workspace path based on the installation location
            var dynamicWorkspacePath = storageLocationService.GetWorkspacePath(installation);
            var workspaceManifests = manifests;
            var isSteamLaunch = profile.UseSteamLaunch == true && installation.InstallationType == GameInstallationType.Steam;

            if (isSteamLaunch)
            {
                logger.LogInformation("[GameLauncher] Steam launch detected - workspace will be adjacent to installation in .genhub-workspace directory");

                // Workspace should be adjacent to the installation directory, not inside it
                // e.g., A:\Steam\steamapps\common\.genhub-workspace\{profileId}\
                // This keeps the installation directory clean and follows proper workspace isolation
                // The dynamicWorkspacePath from GetWorkspacePath already points to the correct location
            }

            logger.LogDebug("[GameLauncher] Using dynamic workspace path: {WorkspacePath} (Installation: {InstallPath})", dynamicWorkspacePath, actualInstallationPath);
            logger.LogDebug("[GameLauncher] Creating workspace configuration - Strategy: {Strategy}", profile.WorkspaceStrategy);
            var workspaceConfig = new WorkspaceConfiguration
            {
                Id = profile.Id,
                Manifests = workspaceManifests,
                GameClient = gameClient,
                Strategy = profile.WorkspaceStrategy,

                // Always rebuild the workspace for Steam launches so we don't accidentally reuse
                // a cached workspace that still contains the proxy launcher instead of the real client.
                ForceRecreate = isSteamLaunch,
                WorkspaceRootPath = dynamicWorkspacePath,
                BaseInstallationPath = actualInstallationPath,
                ManifestSourcePaths = manifestSourcePaths,
            };
            logger.LogDebug("[GameLauncher] BaseInstallationPath set to: {Path}", workspaceConfig.BaseInstallationPath);

            // Note: Removed fallback manifest generation - the profile should explicitly include
            // all required manifests in EnabledContentIds. This prevents conflicts between cached
            // manifests and newly generated ones with different version numbers.

            // Ensure the game directory is clean (restored to original state) BEFORE we prepare the workspace.
            // If a previous Steam launch crashed, the "generalszh.exe" in the install dir might still be our Proxy Launcher.
            // If we don't clean it up, we'll copy the Proxy into the workspace as the "game", causing an infinite loop.
            if (isSteamLaunch)
            {
                if (!string.IsNullOrEmpty(actualInstallationPath))
                {
                    logger.LogInformation("[GameLauncher] Performing pre-launch cleanup to ensure original executables are present");

                    // Always use generals.exe for Steam cleanup (the actual Steam executable)
                    var steamExecutableName = GameClientConstants.GeneralsExecutable;
                    await steamLauncher.CleanupGameDirectoryAsync(actualInstallationPath, steamExecutableName, cancellationToken);
                }
            }

            logger.LogInformation("[GameLauncher] Preparing workspace at: {WorkspacePath}", workspaceConfig.WorkspaceRootPath);
            var workspaceProgress = new Progress<WorkspacePreparationProgress>(
                wp =>
                {
                    // Convert workspace progress to launch progress
                    var percentComplete = 20 + (int)(wp.FilesProcessed / (double)Math.Max(1, wp.TotalFiles) * 60); // 20-80%
                    progress?.Report(new LaunchProgress { Phase = LaunchPhase.PreparingWorkspace, PercentComplete = Math.Min(percentComplete, 80) });
                });

            // For In-Place Steam launch, we MUST skip cleanup to avoid deleting user files (screenshots, replays, untracked mods)
            // that exist in the installation directory.
            var skipCleanup = isSteamLaunch;
            var workspaceResult = await workspaceManager.PrepareWorkspaceAsync(workspaceConfig, workspaceProgress, skipCleanup: skipCleanup, cancellationToken);
            if (!workspaceResult.Success || workspaceResult.Data == null)
            {
                logger.LogError("[GameLauncher] Workspace preparation failed: {Error}", workspaceResult.FirstError);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(workspaceResult.FirstError ?? "Workspace preparation failed", launchId, profile.Id);
            }

            var workspaceInfo = workspaceResult.Data;
            logger.LogInformation("[GameLauncher] Workspace prepared successfully: {WorkspaceId}", workspaceInfo.Id);

            // Prepare user data content (maps, replays, etc.) for this profile
            // This creates hard links from CAS to user's Documents folder for content with UserMapsDirectory, etc. install targets
            // Uses SwitchProfileUserDataAsync to deactivate any other profile's user data first (unlinks their maps)
            // Prepare user data content (maps, replays, etc.) for this profile in the background
            // to ensure instant launch as requested by the user.
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
                        CancellationToken.None); // Don't cancel background linkage if launch process finishes
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

            // Start the process
            progress?.Report(new LaunchProgress { Phase = LaunchPhase.Starting, PercentComplete = 90 });
            logger.LogDebug("[GameLauncher] Resolving executable path from workspace");
            var finalExecutablePath = workspaceInfo.ExecutablePath;

            // Fallback to profile path if workspace didn't resolve it (legacy/simple scenarios)
            if (string.IsNullOrEmpty(finalExecutablePath))
            {
                finalExecutablePath = profile.GameClient?.ExecutablePath;
                logger.LogWarning(
                    "[GameLauncher] Executable not resolved from workspace, falling back to profile: {ExecutablePath}",
                    finalExecutablePath);
            }
            else
            {
                logger.LogDebug("[GameLauncher] Executable resolved from workspace: {ExecutablePath}", finalExecutablePath);
            }

            // Validate we have an executable path
            if (string.IsNullOrEmpty(finalExecutablePath))
            {
                logger.LogError("[GameLauncher] No executable path available");
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(
                    "Executable path not specified in workspace or profile",
                    launchId,
                    profile.Id);
            }

            // Security: If executable is from workspace, validate it's within workspace
            if (!string.IsNullOrEmpty(workspaceInfo.ExecutablePath))
            {
                logger.LogDebug("[GameLauncher] Validating executable is within workspace bounds");
                var normalizedWorkspacePath = Path.GetFullPath(workspaceInfo.WorkspacePath);
                var normalizedExecutablePath = Path.GetFullPath(finalExecutablePath);
                if (!normalizedExecutablePath.StartsWith(normalizedWorkspacePath, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogError("[GameLauncher] Security violation - executable outside workspace");
                    return LaunchOperationResult<GameLaunchInfo>.CreateFailure(
                        $"Security violation: Workspace executable path '{finalExecutablePath}' is outside workspace",
                        launchId,
                        profile.Id);
                }

                logger.LogDebug("[GameLauncher] Security check passed");
            }

            logger.LogInformation("[GameLauncher] Final executable: {ExecutablePath}", finalExecutablePath);
            logger.LogDebug("[GameLauncher] Working directory: {WorkingDirectory}", workspaceInfo.WorkspacePath);

            // Parse command line arguments from the profile
            logger.LogDebug("[GameLauncher] Parsing command line arguments");
            var arguments = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(profile.CommandLineArguments))
            {
                // Split command line arguments and parse them
                var args = profile.CommandLineArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var arg in args)
                {
                    if (!IsValidCommandArgument(arg))
                    {
                        return LaunchOperationResult<GameLaunchInfo>.CreateFailure(
                            $"Invalid command argument: {arg}", launchId, profile.Id);
                    }
                }

                var positionalIndex = 0;
                foreach (var arg in args)
                {
                    // Arguments starting with - are flags
                    if (arg.StartsWith('-'))
                    {
                        arguments[arg] = string.Empty; // Flags don't have values
                    }
                    else
                    {
                        // Otherwise it's a positional argument - use index to avoid overwriting
                        arguments[$"_pos{positionalIndex}"] = arg;
                        positionalIndex++;
                    }
                }
            }

            // Merge with LaunchOptions (LaunchOptions take priority)
            foreach (var kvp in profile.LaunchOptions)
            {
                arguments[kvp.Key] = kvp.Value;
            }

            // Apply windowed mode argument if specified in profile settings
            // Generals/Zero Hour require the -win argument to actually launch in windowed mode
            if (profile.VideoWindowed == true && !arguments.ContainsKey("-win"))
            {
                arguments["-win"] = string.Empty;
                logger.LogInformation("[GameLauncher] Added -win argument for windowed mode");
            }

            SteamLaunchPrepResult? steamPrep = null;
            string? steamAppId = null;

            // Check if this is a Steam profile - use in-place provisioning instead of workspace
            if (profile.UseSteamLaunch == true &&
                installation.InstallationType == GameInstallationType.Steam)
            {
                logger.LogInformation("[GameLauncher] Steam integration enabled - using in-place file provisioning");

                // CRITICAL: For Steam integration, we must deploy the proxy as the STEAM EXECUTABLE (generals.exe),
                // NOT the GameClient executable (e.g., GeneralsOnlineZH_60.exe).
                // Steam launches generals.exe, so the proxy must replace that file.
                // The proxy will then launch the actual GameClient executable from the workspace.
                var steamExecutableName = GameClientConstants.GeneralsExecutable; // Always "generals.exe" for Steam
                logger.LogInformation("[GameLauncher] Steam executable to replace with proxy: {ExecutableName}", steamExecutableName);

                // Resolve Steam AppID from local Steam appmanifest
                if (SteamAppIdResolver.TryResolveSteamAppIdFromInstallationPath(actualInstallationPath, out var resolvedSteamAppId))
                {
                    steamAppId = resolvedSteamAppId;
                }
                else
                {
                    // Use hardcoded defaults
                    steamAppId = gameClient.GameType == GameType.Generals
                        ? SteamConstants.GeneralsAppId
                        : SteamConstants.ZeroHourAppId;
                }

                // Convert arguments dictionary to string array for the proxy config
                var targetArguments = arguments.Select(kvp => string.IsNullOrEmpty(kvp.Value) ? kvp.Key : $"{kvp.Key} {kvp.Value}").ToArray();

                // Provision files directly to game installation directory (Proxy Launcher)
                var steamLaunchResult = await steamLauncher.PrepareForProfileAsync(
                    actualInstallationPath,
                    profile.Id,
                    manifests,
                    steamExecutableName, // Use Steam executable name (generals.exe), not GameClient executable
                    finalExecutablePath, // Target Workspace Executable
                    workspaceInfo.WorkspacePath,
                    targetArguments,
                    steamAppId,
                    cancellationToken);
                if (!steamLaunchResult.Success)
                {
                    logger.LogError("[GameLauncher] Steam launch preparation failed: {Error}", steamLaunchResult.FirstError);
                    return LaunchOperationResult<GameLaunchInfo>.CreateFailure(
                        $"Failed to prepare game directory for Steam integration: {steamLaunchResult.FirstError}",
                        launchId,
                        profile.Id);
                }

                logger.LogInformation(
                    "[GameLauncher] Steam launch preparation complete. Files: {Linked} linked, {Removed} removed, {BackedUp} backed up",
                    steamLaunchResult.Data!.FilesLinked,
                    steamLaunchResult.Data.FilesRemoved,
                    steamLaunchResult.Data.FilesBackedUp);
                steamPrep = steamLaunchResult.Data;
                logger.LogInformation(
                    "[GameLauncher] Steam integration ready. Proxy sidecar: {ProxyPath}",
                    steamLaunchResult.Data.ExecutablePath);
            }

            logger.LogDebug("[GameLauncher] Building launch configuration with {ArgCount} arguments", arguments.Count);
            var launchConfig = new GameLaunchConfiguration
            {
                ExecutablePath = finalExecutablePath,
                WorkingDirectory = workspaceInfo.WorkspacePath,
                Arguments = arguments,
                EnvironmentVariables = profile.EnvironmentVariables,
            };
            logger.LogInformation("[GameLauncher] Starting game process...");
            OperationResult<GameProcessInfo> processResult;

            // For Steam launch: launch via Steam so playtime/overlay tracking is owned by Steam.
            // We rely on SteamLauncher to have prepared the install dir (proxy + proxy_config.json + steam_appid.txt)
            // so the game entrypoint Steam launches will route into the workspace.
            if (profile.UseSteamLaunch == true && installation.InstallationType == GameInstallationType.Steam)
            {
                if (steamPrep == null || string.IsNullOrWhiteSpace(steamPrep.ExecutablePath))
                {
                    logger.LogError("[GameLauncher] Steam prep missing proxy path");
                    return LaunchOperationResult<GameLaunchInfo>.CreateFailure("Steam proxy not prepared", launchId, profile.Id);
                }

                if (string.IsNullOrWhiteSpace(steamAppId))
                {
                    logger.LogError("[GameLauncher] Steam AppId is missing");
                    return LaunchOperationResult<GameLaunchInfo>.CreateFailure("Steam AppId missing", launchId, profile.Id);
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
                    return LaunchOperationResult<GameLaunchInfo>.CreateFailure($"Failed to launch via Steam: {ex.Message}", launchId, profile.Id);
                }

                // Get the executable name from the GameClient manifest for process monitoring
                // The proxy launches generalszh.exe from workspace, so we monitor for that
                var gameClientManifestForMonitor = manifests.FirstOrDefault(m => m.ContentType == ContentType.GameClient);
                var executableFileForMonitor = gameClientManifestForMonitor?.Files?.FirstOrDefault(f => f.IsExecutable);
                var gameProcessName = executableFileForMonitor != null
                    ? Path.GetFileNameWithoutExtension(executableFileForMonitor.RelativePath)
                    : Path.GetFileNameWithoutExtension(GameClientConstants.SuperHackersZeroHourExecutable);
                logger.LogInformation("[GameLauncher] Monitoring for process: {ProcessName}", gameProcessName);

                // Discover and track the game process that Steam->proxy launches
                processResult = await processManager.DiscoverAndTrackProcessAsync(
                    gameProcessName,
                    workspaceInfo.WorkspacePath,
                    cancellationToken);
            }
            else
            {
                processResult = await processManager.StartProcessAsync(launchConfig, cancellationToken);
            }

            if (!processResult.Success || processResult.Data == null)
            {
                logger.LogError("[GameLauncher] Process start/discovery failed: {Error}", processResult.FirstError);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(processResult.FirstError ?? "Process start failed", launchId, profile.Id);
            }

            if (processResult.Data == null)
            {
                logger.LogError("[GameLauncher] Process start succeeded but returned null process info");
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure("Process start failed: no process info returned", launchId, profile.Id);
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
            };
            logger.LogDebug("[GameLauncher] Updating launch registry with real process info");
            await launchRegistry.RegisterLaunchAsync(launchInfo);

            // Report completion
            progress?.Report(new LaunchProgress { Phase = LaunchPhase.Running, PercentComplete = 100 });
            logger.LogInformation("[GameLauncher] === Launch completed successfully for profile {ProfileId} ===", profile.Id);
            return LaunchOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo, launchId, profile.Id);
        }
        catch (OperationCanceledException)
        {
            // Clean up placeholder if launch was cancelled
            logger.LogWarning("Launch cancelled for profile {ProfileId}, cleaning up placeholder entry", profile.Id);
            await launchRegistry.UnregisterLaunchAsync(launchId);
            throw new TaskCanceledException();
        }
        catch (Exception ex)
        {
            // Clean up placeholder if launch failed
            logger.LogError(ex, "Failed to launch profile {ProfileId}, cleaning up placeholder entry", profile.Id);
            await launchRegistry.UnregisterLaunchAsync(launchId);
            return LaunchOperationResult<GameLaunchInfo>.CreateFailure($"Launch failed: {ex.Message}", launchId, profile.Id);
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
                logger.LogWarning("Failed to save Options.ini for {GameType}: {Error}", gameType, saveResult.FirstError);
            }
            else
            {
                logger.LogInformation("Successfully wrote Options.ini for {GameType}", gameType);
            }
        }
        catch (Exception ex)
        {
            // Don't fail the launch if Options.ini writing fails - log and continue
            logger.LogError(ex, "Failed to apply profile settings to Options.ini, continuing with launch");
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
                    var existsResult = await casService.ExistsAsync(file.Hash, cancellationToken);
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
