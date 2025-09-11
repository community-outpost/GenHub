using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.GameProfile;
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
    ILaunchRegistry launchRegistry) : IGameLauncher
{
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
                            Arguments = config.Arguments != null ? string.Join(" ", config.Arguments.Select(kvp => $"{kvp.Key} {kvp.Value}")) : string.Empty,
                            UseShellExecute = false,
                        },
                    };
                    if (!process.Start())
                        return LaunchResult.CreateFailure("Failed to start process", null);
                    var launchTime = DateTime.UtcNow - startTime;
                    return LaunchResult.CreateSuccess(process.Id, process.StartTime, launchTime);
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

        var launchId = Guid.NewGuid().ToString();

        try
        {
            // Check for cancellation early
            cancellationToken.ThrowIfCancellationRequested();

            // Report validating profile
            progress?.Report(new LaunchProgress { Phase = LaunchPhase.ValidatingProfile, PercentComplete = 0 });

            // Resolve content manifests
            progress?.Report(new LaunchProgress { Phase = LaunchPhase.ResolvingContent, PercentComplete = 10 });

            var manifests = new List<ContentManifest>();
            foreach (var contentId in profile.EnabledContentIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var manifestResult = await manifestPool.GetManifestAsync(contentId, cancellationToken);
                if (!manifestResult.Success)
                {
                    return LaunchOperationResult<GameLaunchInfo>.CreateFailure($"Failed to resolve content '{contentId}': {manifestResult.FirstError}", launchId, profile.Id);
                }

                if (manifestResult.Data == null)
                {
                    return LaunchOperationResult<GameLaunchInfo>.CreateFailure($"Content manifest '{contentId}' not found", launchId, profile.Id);
                }

                manifests.Add(manifestResult.Data);
            }

            // Prepare workspace
            progress?.Report(new LaunchProgress { Phase = LaunchPhase.PreparingWorkspace, PercentComplete = 20 });

            var workspaceConfig = new WorkspaceConfiguration
            {
                Id = profile.Id,
                Manifests = manifests,
                GameVersion = profile.GameVersion,
                Strategy = profile.WorkspaceStrategy,
                WorkspaceRootPath = Path.Combine(Path.GetTempPath(), "GenHub", "Workspaces"),
                BaseInstallationPath = profile.GameInstallationId, // This might need adjustment
            };

            var workspaceProgress = new Progress<WorkspacePreparationProgress>(wp =>
            {
                // Convert workspace progress to launch progress
                var percentComplete = 20 + (int)(wp.FilesProcessed / (double)wp.TotalFiles * 60); // 20-80%
                progress?.Report(new LaunchProgress { Phase = LaunchPhase.PreparingWorkspace, PercentComplete = Math.Min(percentComplete, 80) });
            });

            var workspaceResult = await workspaceManager.PrepareWorkspaceAsync(workspaceConfig, workspaceProgress, cancellationToken);
            if (!workspaceResult.Success)
            {
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(workspaceResult.FirstError!, launchId, profile.Id);
            }

            var workspaceInfo = workspaceResult.Data!;

            // Start the process
            progress?.Report(new LaunchProgress { Phase = LaunchPhase.Starting, PercentComplete = 90 });

            var launchConfig = new GameLaunchConfiguration
            {
                ExecutablePath = workspaceInfo.ExecutablePath ?? profile.GameVersion.ExecutablePath,
                WorkingDirectory = workspaceInfo.WorkspacePath,
                Arguments = profile.LaunchOptions,
            };

            var processResult = await processManager.StartProcessAsync(launchConfig, cancellationToken);
            if (!processResult.Success)
            {
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(processResult.FirstError!, launchId, profile.Id);
            }

            var processInfo = processResult.Data!;

            // Create launch info and register
            var launchInfo = new GameLaunchInfo
            {
                LaunchId = launchId,
                ProfileId = profile.Id,
                WorkspaceId = workspaceInfo.Id,
                ProcessInfo = processInfo,
                LaunchedAt = DateTime.UtcNow,
            };

            await launchRegistry.RegisterLaunchAsync(launchInfo);

            // Report completion
            progress?.Report(new LaunchProgress { Phase = LaunchPhase.Running, PercentComplete = 100 });

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
                // Still unregister even if termination failed
                await launchRegistry.UnregisterLaunchAsync(launchId);
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
}
