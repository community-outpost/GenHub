using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
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
    ILaunchRegistry launchRegistry,
    IGameInstallationService gameInstallationService,
    ICasService casService,
    IConfigurationProviderService configurationProvider,
    IGameSettingsService gameSettingsService) : IGameLauncher
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _profileLaunchLocks = new();

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
                }, cancellationToken);
        }
        catch (System.Exception ex)
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
                }, cancellationToken);
        }
        catch (System.Exception ex)
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
            logger.LogError(ex, "Failed to terminate game process with ID {ProcessId}", processId);
            return false;
        }
    }

    /// <summary>
    /// Launches a game profile by its ID.
    /// </summary>
    /// <param name="profileId">The ID of the game profile to launch.</param>
    /// <param name="progress">Optional progress reporter for launch progress.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="LaunchOperationResult{GameLaunchInfo}"/> representing the result of the launch operation.</returns>
    public async Task<LaunchOperationResult<GameLaunchInfo>> LaunchProfileAsync(string profileId, IProgress<LaunchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        // Report initial progress
        progress?.Report(new LaunchProgress { Phase = LaunchPhase.ValidatingProfile, PercentComplete = 0 });

        // Get the profile
        var profileResult = await profileManager.GetProfileAsync(profileId, cancellationToken);
        if (!profileResult.Success)
        {
            return LaunchOperationResult<GameLaunchInfo>.CreateFailure(profileResult.FirstError!, profileId: profileId);
        }

        var profile = profileResult.Data!;
        return await LaunchProfileAsync(profile, progress, cancellationToken);
    }

    /// <summary>
    /// Launches a game using the provided game profile object.
    /// </summary>
    /// <param name="profile">The game profile to launch.</param>
    /// <param name="progress">Optional progress reporter for launch progress.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="LaunchOperationResult{GameLaunchInfo}"/> representing the result of the launch operation.</returns>
    public async Task<LaunchOperationResult<GameLaunchInfo>> LaunchProfileAsync(GameProfile profile, IProgress<LaunchProgress>? progress = null, CancellationToken cancellationToken = default)
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
            return await LaunchProfileAsync(profile, progress, cancellationToken, launchId);
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
                return LaunchOperationResult<IReadOnlyList<GameProcessInfo>>.CreateFailure(result.FirstError!);
            }

            return LaunchOperationResult<IReadOnlyList<GameProcessInfo>>.CreateSuccess(result.Data!);
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
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(result.FirstError!, launchId, launchInfo.ProfileId);
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

        // Block command separators and shell metacharacters
        // This includes: ; | & && || ` $ % newlines, carriage returns
        if (arg.IndexOfAny(new[] { ';', '|', '&', '\n', '\r', '`', '$', '%' }) >= 0)
            return false;

        // Block path traversal attempts
        if (arg.Contains("..") || arg.Contains("~"))
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
        if ((arg.StartsWith("\"") && arg.EndsWith("\"")) ||
            (arg.StartsWith("'") && arg.EndsWith("'")))
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
            if (arg.Contains("..") || arg.ToLowerInvariant().Contains("windows\\system") ||
                arg.ToLowerInvariant().Contains("windows\\system32"))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if a profile has any custom settings defined.
    /// </summary>
    /// <param name="profile">The game profile.</param>
    /// <returns>True if the profile has custom settings, false otherwise.</returns>
    private static bool HasProfileSettings(GameProfile profile)
    {
        return profile.VideoResolutionWidth.HasValue ||
               profile.VideoResolutionHeight.HasValue ||
               profile.VideoWindowed.HasValue ||
               profile.VideoTextureQuality.HasValue ||
               profile.VideoShadows.HasValue ||
               profile.VideoParticleEffects.HasValue ||
               profile.VideoExtraAnimations.HasValue ||
               profile.VideoBuildingAnimations.HasValue ||
               profile.VideoGamma.HasValue ||
               profile.AudioSoundVolume.HasValue ||
               profile.AudioThreeDSoundVolume.HasValue ||
               profile.AudioSpeechVolume.HasValue ||
               profile.AudioMusicVolume.HasValue ||
               profile.AudioEnabled.HasValue ||
               profile.AudioNumSounds.HasValue;
    }

    /// <summary>
    /// Applies the settings from a game profile to an IniOptions object.
    /// </summary>
    /// <param name="profile">The game profile containing the settings.</param>
    /// <param name="options">The IniOptions object to apply settings to.</param>
    private static void ApplyProfileSettingsToOptions(GameProfile profile, IniOptions options)
    {
        // Apply video settings if they exist
        if (profile.VideoResolutionWidth.HasValue)
            options.Video.ResolutionWidth = profile.VideoResolutionWidth.Value;

        if (profile.VideoResolutionHeight.HasValue)
            options.Video.ResolutionHeight = profile.VideoResolutionHeight.Value;

        if (profile.VideoWindowed.HasValue)
            options.Video.Windowed = profile.VideoWindowed.Value;

        if (profile.VideoTextureQuality.HasValue)
        {
            // Map TextureQuality (0-2) to TextureReduction (0-3, inverted)
            options.Video.TextureReduction = 2 - profile.VideoTextureQuality.Value;
        }

        if (profile.VideoShadows.HasValue)
        {
            options.Video.UseShadowVolumes = profile.VideoShadows.Value;
            options.Video.UseShadowDecals = profile.VideoShadows.Value;
        }

        if (profile.VideoExtraAnimations.HasValue)
            options.Video.ExtraAnimations = profile.VideoExtraAnimations.Value;

        if (profile.VideoGamma.HasValue)
            options.Video.Gamma = profile.VideoGamma.Value;

        // Apply audio settings if they exist
        if (profile.AudioSoundVolume.HasValue)
            options.Audio.SFXVolume = profile.AudioSoundVolume.Value;

        if (profile.AudioThreeDSoundVolume.HasValue)
            options.Audio.SFX3DVolume = profile.AudioThreeDSoundVolume.Value;

        if (profile.AudioSpeechVolume.HasValue)
            options.Audio.VoiceVolume = profile.AudioSpeechVolume.Value;

        if (profile.AudioMusicVolume.HasValue)
            options.Audio.MusicVolume = profile.AudioMusicVolume.Value;

        if (profile.AudioEnabled.HasValue)
            options.Audio.AudioEnabled = profile.AudioEnabled.Value;

        if (profile.AudioNumSounds.HasValue)
            options.Audio.NumSounds = profile.AudioNumSounds.Value;
    }

    private async Task<LaunchOperationResult<GameLaunchInfo>> LaunchProfileAsync(GameProfile profile, IProgress<LaunchProgress>? progress, CancellationToken cancellationToken, string launchId)
    {
        try
        {
            logger.LogInformation("[GameLauncher] === Starting launch for profile '{ProfileName}' (ID: {ProfileId}) ===", profile.Name, profile.Id);

            // Check for cancellation early
            cancellationToken.ThrowIfCancellationRequested();

            // Report validating profile
            progress?.Report(new LaunchProgress { Phase = LaunchPhase.ValidatingProfile, PercentComplete = 0 });

            // Resolve content manifests
            logger.LogDebug("[GameLauncher] Resolving {Count} enabled content manifests", profile.EnabledContentIds?.Count ?? 0);
            progress?.Report(new LaunchProgress { Phase = LaunchPhase.ResolvingContent, PercentComplete = 10 });

            var manifests = new List<ContentManifest>();
            foreach (var contentId in profile.EnabledContentIds ?? Enumerable.Empty<string>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                logger.LogDebug("[GameLauncher] Loading manifest: {ContentId}", contentId);
                var manifestResult = await manifestPool.GetManifestAsync(ManifestId.Create(contentId), cancellationToken);
                if (!manifestResult.Success)
                {
                    logger.LogError("[GameLauncher] Failed to resolve manifest {ContentId}: {Error}", contentId, manifestResult.FirstError);
                    return LaunchOperationResult<GameLaunchInfo>.CreateFailure($"Failed to resolve content '{contentId}': {manifestResult.FirstError}", launchId, profile.Id);
                }

                if (manifestResult.Data == null)
                {
                    logger.LogError("[GameLauncher] Manifest {ContentId} returned null data", contentId);
                    return LaunchOperationResult<GameLaunchInfo>.CreateFailure($"Content manifest '{contentId}' not found", launchId, profile.Id);
                }

                logger.LogDebug("[GameLauncher] Manifest loaded: {Name} (Type: {Type})", manifestResult.Data.Name, manifestResult.Data.ContentType);
                manifests.Add(manifestResult.Data);
            }

            logger.LogInformation("[GameLauncher] Resolved {Count} manifests successfully", manifests.Count);

            // CRITICAL: Apply profile-specific game settings to Options.ini BEFORE workspace preparation
            // This prevents race conditions where the game might start reading Options.ini before we write it
            logger.LogDebug("[GameLauncher] Applying profile settings to Options.ini before workspace preparation");
            await ApplyProfileSettingsToIniOptionsAsync(profile, cancellationToken);

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
                if (manifest.ContentType == Core.Models.Enums.ContentType.GameInstallation)
                {
                    continue;
                }

                // For GameClient, use WorkingDirectory if available
                if (manifest.ContentType == Core.Models.Enums.ContentType.GameClient &&
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

            logger.LogDebug("[GameLauncher] Creating workspace configuration - Strategy: {Strategy}", profile.WorkspaceStrategy);
            var workspaceConfig = new WorkspaceConfiguration
            {
                Id = profile.Id,
                Manifests = manifests,
                GameClient = profile.GameClient!,
                Strategy = profile.WorkspaceStrategy,
                WorkspaceRootPath = configurationProvider.GetWorkspacePath(),
                ManifestSourcePaths = manifestSourcePaths,
            };

            // Resolve the base installation path from the installation ID
            logger.LogDebug("[GameLauncher] Resolving installation path for ID: {InstallationId}", profile.GameInstallationId);
            var installationResult = await gameInstallationService.GetInstallationAsync(profile.GameInstallationId, cancellationToken);
            if (!installationResult.Success || installationResult.Data == null)
            {
                logger.LogError("[GameLauncher] Failed to resolve installation: {Error}", installationResult.FirstError);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure($"Failed to resolve game installation '{profile.GameInstallationId}': {installationResult.FirstError}", launchId, profile.Id);
            }

            var installation = (GameInstallation)installationResult.Data!;
            workspaceConfig.BaseInstallationPath = installation.InstallationPath;
            logger.LogDebug("[GameLauncher] Installation path set: {Path}", workspaceConfig.BaseInstallationPath);

            // Note: Removed fallback manifest generation - the profile should explicitly include
            // all required manifests in EnabledContentIds. This prevents conflicts between cached
            // manifests and newly generated ones with different version numbers.
            logger.LogInformation("[GameLauncher] Preparing workspace at: {WorkspacePath}", workspaceConfig.WorkspaceRootPath);
            var workspaceProgress = new Progress<WorkspacePreparationProgress>(wp =>
            {
                // Convert workspace progress to launch progress
                var percentComplete = 20 + (int)(wp.FilesProcessed / (double)Math.Max(1, wp.TotalFiles) * 60); // 20-80%
                progress?.Report(new LaunchProgress { Phase = LaunchPhase.PreparingWorkspace, PercentComplete = Math.Min(percentComplete, 80) });
            });

            var workspaceResult = await workspaceManager.PrepareWorkspaceAsync(workspaceConfig, workspaceProgress, cancellationToken);
            if (!workspaceResult.Success || workspaceResult.Data == null)
            {
                logger.LogError("[GameLauncher] Workspace preparation failed: {Error}", workspaceResult.FirstError);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(workspaceResult.FirstError ?? "Workspace preparation failed", launchId, profile.Id);
            }

            var workspaceInfo = workspaceResult.Data!;
            logger.LogInformation("[GameLauncher] Workspace prepared successfully: {WorkspaceId}", workspaceInfo.Id);

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
                    if (arg.StartsWith("-"))
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

            logger.LogDebug("[GameLauncher] Building launch configuration with {ArgCount} arguments", arguments.Count);
            var launchConfig = new GameLaunchConfiguration
            {
                ExecutablePath = finalExecutablePath,
                WorkingDirectory = workspaceInfo.WorkspacePath,
                Arguments = arguments,
                EnvironmentVariables = profile.EnvironmentVariables,
            };

            logger.LogInformation("[GameLauncher] Starting game process...");
            var processResult = await processManager.StartProcessAsync(launchConfig, cancellationToken);
            if (!processResult.Success || processResult.Data == null)
            {
                logger.LogError("[GameLauncher] Process start failed: {Error}", processResult.FirstError);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(processResult.FirstError ?? "Process start failed", launchId, profile.Id);
            }

            if (processResult.Data == null)
            {
                logger.LogError("[GameLauncher] Process start succeeded but returned null process info");
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure("Process start failed: no process info returned", launchId, profile.Id);
            }

            var processInfo = processResult.Data;
            logger.LogInformation("[GameLauncher] Process started successfully - PID: {ProcessId}", processInfo.ProcessId);

            // Create launch info and register
            var launchInfo = new GameLaunchInfo
            {
                LaunchId = launchId,
                ProfileId = profile.Id,
                WorkspaceId = workspaceInfo.Id,
                ProcessInfo = processInfo,
                LaunchedAt = DateTime.UtcNow,
            };

            logger.LogDebug("[GameLauncher] Registering launch in registry");
            await launchRegistry.RegisterLaunchAsync(launchInfo);

            // Report completion
            progress?.Report(new LaunchProgress { Phase = LaunchPhase.Running, PercentComplete = 100 });

            logger.LogInformation("[GameLauncher] === Launch completed successfully for profile {ProfileId} ===", profile.Id);
            return LaunchOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo, launchId, profile.Id);
        }
        catch (OperationCanceledException)
        {
            throw new TaskCanceledException();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to launch profile {ProfileId}", profile.Id);
            return LaunchOperationResult<GameLaunchInfo>.CreateFailure($"Launch failed: {ex.Message}", launchId, profile.Id);
        }
    }

    /// <summary>
    /// Applies the profile-specific game settings to the Options.ini file before launching.
    /// This ensures the game launches with the settings configured for this specific profile.
    /// </summary>
    /// <param name="profile">The game profile containing the settings to apply.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ApplyProfileSettingsToIniOptionsAsync(GameProfile profile, CancellationToken cancellationToken)
    {
        try
        {
            // Determine the game type from the profile's game client
            var gameType = profile.GameClient.GameType;
            if (gameType == GameType.Unknown)
            {
                logger.LogWarning("Profile {ProfileId} has unknown game type, skipping Options.ini write", profile.Id);
                return;
            }

            // Check if the profile has any custom settings defined
            if (!HasProfileSettings(profile))
            {
                logger.LogInformation("Profile {ProfileId} has no custom settings, skipping Options.ini write", profile.Id);
                return;
            }

            logger.LogInformation("Applying profile settings to Options.ini for {GameType}", gameType);

            // Load the current Options.ini or create a new one
            var loadResult = await gameSettingsService.LoadOptionsAsync(gameType);
            var options = loadResult.Success && loadResult.Data != null
                ? loadResult.Data
                : new IniOptions();

            // Apply profile settings to the options object
            ApplyProfileSettingsToOptions(profile, options);

            // Save the updated Options.ini
            var saveResult = await gameSettingsService.SaveOptionsAsync(gameType, options);
            if (!saveResult.Success)
            {
                logger.LogWarning("Failed to save Options.ini for {GameType}: {Error}", gameType, saveResult.FirstError);
            }
            else
            {
                logger.LogInformation("Successfully wrote profile settings to Options.ini for {GameType}", gameType);
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
                foreach (var file in manifest.Files.Where(f => f.SourceType == Core.Models.Enums.ContentSourceType.ContentAddressable && !string.IsNullOrEmpty(f.Hash)))
                {
                    var existsResult = await casService.ExistsAsync(file.Hash, cancellationToken);
                    if (!existsResult.Success || !existsResult.Data)
                    {
                        missingHashes.Add(file.Hash);
                    }
                }
            }
        }

        if (missingHashes.Any())
        {
            return OperationResult<bool>.CreateFailure($"Missing CAS objects: {string.Join(", ", missingHashes.Distinct())}");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }
}
