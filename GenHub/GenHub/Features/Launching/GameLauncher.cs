using System;
using System.Collections.Generic;
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

namespace GenHub.Features.Launching
{
    /// <summary>
    /// Service for launching games from prepared workspaces.
    /// </summary>
    public class GameLauncher : IGameLauncher
    {
        private readonly IGameProfileManager _profileManager;
        private readonly IWorkspaceManager _workspaceManager;
        private readonly IContentManifestPool _manifestPool;
        private readonly IGameProcessManager _processManager;
        private readonly ILaunchRegistry _launchRegistry;
        private readonly ILogger<GameLauncher> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="GameLauncher"/> class.
        /// </summary>
        /// <param name="profileManager">The game profile manager.</param>
        /// <param name="workspaceManager">The workspace manager.</param>
        /// <param name="manifestPool">The game manifest pool.</param>
        /// <param name="processManager">The game process manager.</param>
        /// <param name="launchRegistry">The launch registry.</param>
        /// <param name="logger">The logger.</param>
        public GameLauncher(
            IGameProfileManager profileManager,
            IWorkspaceManager workspaceManager,
            IContentManifestPool manifestPool,
            IGameProcessManager processManager,
            ILaunchRegistry launchRegistry,
            ILogger<GameLauncher> logger)
        {
            _profileManager = profileManager;
            _workspaceManager = workspaceManager;
            _manifestPool = manifestPool;
            _processManager = processManager;
            _launchRegistry = launchRegistry;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<LaunchOperationResult<GameLaunchInfo>> LaunchProfileAsync(string profileId, IProgress<LaunchProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report(new LaunchProgress { Phase = LaunchPhase.ValidatingProfile, PercentComplete = 0 });

                // Get the profile
                var profileResult = await _profileManager.GetProfileAsync(profileId, cancellationToken);
                if (profileResult.Failed)
                {
                    return LaunchOperationResult<GameLaunchInfo>.CreateFailure(profileResult.FirstError!);
                }

                var profile = profileResult.Data!;
                return await LaunchProfileAsync(profile, progress, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Convert to TaskCanceledException for test expectations
                throw new TaskCanceledException();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to launch profile {ProfileId}", profileId);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure($"Launch failed: {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public async Task<LaunchOperationResult<GameLaunchInfo>> LaunchProfileAsync(GameProfile profile, IProgress<LaunchProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (profile == null)
                {
                    return LaunchOperationResult<GameLaunchInfo>.CreateFailure("Profile cannot be null");
                }

                // Generate launch ID at the beginning
                var launchId = Guid.NewGuid().ToString();

                progress?.Report(new LaunchProgress { Phase = LaunchPhase.ResolvingContent, PercentComplete = 10 });

                var manifests = new List<ContentManifest>();
                if (profile.EnabledContentIds.Any())
                {
                    foreach (var contentId in profile.EnabledContentIds)
                    {
                        var manifestResult = await _manifestPool.GetManifestAsync(contentId, cancellationToken);
                        if (manifestResult.Failed)
                        {
                            return LaunchOperationResult<GameLaunchInfo>.CreateFailure($"Failed to resolve content with ID: {contentId}. Reason: {manifestResult.FirstError}", launchId, profile.Id);
                        }

                        if (manifestResult.Data != null)
                        {
                            manifests.Add(manifestResult.Data);
                        }
                        else
                        {
                            // This case indicates success but null data, which is unexpected for a required manifest.
                            return LaunchOperationResult<GameLaunchInfo>.CreateFailure($"Failed to resolve content: Manifest for content ID '{contentId}' was not found.", launchId, profile.Id);
                        }
                    }
                }

                progress?.Report(new LaunchProgress { Phase = LaunchPhase.PreparingWorkspace, PercentComplete = 40 });

                var workspaceConfig = new WorkspaceConfiguration
                {
                    Id = profile.Id,
                    Manifests = manifests,
                    Strategy = profile.PreferredStrategy,
                    GameVersion = profile.GameVersion, // Pass the whole GameVersion object
                    BaseInstallationPath = Path.GetDirectoryName(profile.GameVersion.ExecutablePath) ?? string.Empty,
                };

                var workspaceProgress = progress == null ? null : new Progress<WorkspacePreparationProgress>(p =>
                {
                    // Translate workspace progress (0-100) to the launch progress slice (40-90)
                    var launchPercent = 40 + (int)(p.PercentComplete * 0.5);
                    progress.Report(new LaunchProgress { Phase = LaunchPhase.PreparingWorkspace, PercentComplete = launchPercent });
                });

                var workspaceResult = await _workspaceManager.PrepareWorkspaceAsync(workspaceConfig, workspaceProgress, cancellationToken);
                if (workspaceResult.Failed)
                {
                    return LaunchOperationResult<GameLaunchInfo>.CreateFailure(workspaceResult.FirstError!, launchId, profile.Id);
                }

                progress?.Report(new LaunchProgress { Phase = LaunchPhase.Starting, PercentComplete = 90 });

                var launchConfig = new GameLaunchConfiguration
                {
                    ExecutablePath = workspaceResult.Data!.ExecutablePath, // Use executable from workspace info
                    WorkingDirectory = workspaceResult.Data!.WorkspacePath,
                    Arguments = profile.LaunchArguments,
                    EnvironmentVariables = profile.EnvironmentVariables,
                };

                var processResult = await _processManager.StartProcessAsync(launchConfig, cancellationToken);
                if (processResult.Failed)
                {
                    return LaunchOperationResult<GameLaunchInfo>.CreateFailure(processResult.FirstError!, launchId, profile.Id);
                }

                var launchInfo = new GameLaunchInfo
                {
                    LaunchId = launchId,
                    ProfileId = profile.Id,
                    WorkspaceId = workspaceResult.Data!.Id,
                    ProcessInfo = processResult.Data!,
                    LaunchedAt = DateTime.UtcNow,
                };

                await _launchRegistry.RegisterLaunchAsync(launchInfo);

                progress?.Report(new LaunchProgress { Phase = LaunchPhase.Running, PercentComplete = 100 });

                _logger.LogInformation("Successfully launched profile: {ProfileName}", profile.Name);
                return LaunchOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo, launchInfo.LaunchId, profile.Id);
            }
            catch (OperationCanceledException)
            {
                // Convert to TaskCanceledException for test expectations
                throw new TaskCanceledException();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to launch profile {ProfileId}", profile?.Id ?? "unknown");
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure($"Launch failed: {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public async Task<LaunchOperationResult<GameLaunchInfo>> TerminateGameAsync(string launchId, CancellationToken cancellationToken = default)
        {
            var launchInfo = await _launchRegistry.GetLaunchInfoAsync(launchId);
            if (launchInfo == null)
            {
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure("Launch ID not found.", launchId);
            }

            var terminateResult = await _processManager.TerminateProcessAsync(launchInfo.ProcessInfo.ProcessId, cancellationToken);

            // Always unregister from registry, even if termination failed
            await _launchRegistry.UnregisterLaunchAsync(launchId);

            if (terminateResult.Failed)
            {
                _logger.LogWarning("Process termination failed for Launch ID: {LaunchId}, but unregistered from registry", launchId);
                return LaunchOperationResult<GameLaunchInfo>.CreateFailure(terminateResult.FirstError!, launchId, launchInfo.ProfileId);
            }

            _logger.LogInformation("Successfully terminated game with Launch ID: {LaunchId}", launchId);
            return LaunchOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo, launchId, launchInfo.ProfileId);
        }

        /// <inheritdoc/>
        public async Task<LaunchOperationResult<IReadOnlyList<GameProcessInfo>>> GetActiveGamesAsync(CancellationToken cancellationToken = default)
        {
            var processResult = await _processManager.GetActiveProcessesAsync(cancellationToken);
            if (processResult.Failed)
            {
                return LaunchOperationResult<IReadOnlyList<GameProcessInfo>>.CreateFailure(error: processResult.FirstError!);
            }

            return LaunchOperationResult<IReadOnlyList<GameProcessInfo>>.CreateSuccess(processResult.Data!);
        }

        /// <inheritdoc/>
        public async Task<LaunchOperationResult<GameProcessInfo>> GetGameProcessInfoAsync(string launchId, CancellationToken cancellationToken = default)
        {
            var launchInfo = await _launchRegistry.GetLaunchInfoAsync(launchId);
            if (launchInfo == null)
            {
                return LaunchOperationResult<GameProcessInfo>.CreateFailure("Launch ID not found.", launchId);
            }

            var processInfoResult = await _processManager.GetProcessInfoAsync(launchInfo.ProcessInfo.ProcessId, cancellationToken);
            if (processInfoResult.Failed)
            {
                return LaunchOperationResult<GameProcessInfo>.CreateFailure(processInfoResult.FirstError!, launchId, launchInfo.ProfileId);
            }

            return LaunchOperationResult<GameProcessInfo>.CreateSuccess(processInfoResult.Data!, launchId, launchInfo.ProfileId);
        }
    }
}
