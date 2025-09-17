using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
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

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Facade for game profile launching operations, coordinating between multiple services
/// to provide a simplified interface for launching game profiles.
/// </summary>
public class ProfileLauncherFacade(
    IGameProfileManager profileManager,
    IGameLauncher gameLauncher,
    IWorkspaceManager workspaceManager,
    ILaunchRegistry launchRegistry,
    IContentManifestPool manifestPool,
    IGameInstallationService installationService,
    IConfigurationProviderService config,
    IDependencyResolver dependencyResolver,
    ILogger<ProfileLauncherFacade> logger) : IProfileLauncherFacade
{
    private readonly IGameProfileManager _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
    private readonly IGameLauncher _gameLauncher = gameLauncher ?? throw new ArgumentNullException(nameof(gameLauncher));
    private readonly IWorkspaceManager _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
    private readonly ILaunchRegistry _launchRegistry = launchRegistry ?? throw new ArgumentNullException(nameof(launchRegistry));
    private readonly IContentManifestPool _manifestPool = manifestPool ?? throw new ArgumentNullException(nameof(manifestPool));
    private readonly IGameInstallationService _installationService = installationService ?? throw new ArgumentNullException(nameof(installationService));
    private readonly IConfigurationProviderService _config = config ?? throw new ArgumentNullException(nameof(config));
    private readonly IDependencyResolver _dependencyResolver = dependencyResolver ?? throw new ArgumentNullException(nameof(dependencyResolver));
    private readonly ILogger<ProfileLauncherFacade> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameLaunchInfo>> LaunchProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Launching profile {ProfileId}", profileId);

            // Get the profile
            var profileResult = await _profileManager.GetProfileAsync(profileId, cancellationToken);
            if (profileResult.Failed)
            {
                return ProfileOperationResult<GameLaunchInfo>.CreateFailure(string.Join(", ", profileResult.Errors));
            }

            var profile = profileResult.Data!;

            // Validate the profile before launching
            var validationResult = await ValidateLaunchAsync(profileId, cancellationToken);
            if (validationResult.Failed)
            {
                return ProfileOperationResult<GameLaunchInfo>.CreateFailure(string.Join(", ", validationResult.Errors));
            }

            // Launch the game using the profile object
            var launchResult = await _gameLauncher.LaunchProfileAsync(profile, cancellationToken: cancellationToken);
            if (launchResult.Failed)
            {
                return ProfileOperationResult<GameLaunchInfo>.CreateFailure(string.Join(", ", launchResult.Errors));
            }

            var launchInfo = launchResult.Data!;
            _logger.LogInformation("Successfully launched profile {ProfileId} with process ID {ProcessId}", profileId, launchInfo.ProcessInfo.ProcessId);

            return ProfileOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch profile {ProfileId}", profileId);
            return ProfileOperationResult<GameLaunchInfo>.CreateFailure($"Failed to launch profile: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<bool>> ValidateLaunchAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Validating launch for profile {ProfileId}", profileId);

            // Get the profile first
            var profileResult = await _profileManager.GetProfileAsync(profileId, cancellationToken);
            if (profileResult.Failed)
            {
                return ProfileOperationResult<bool>.CreateFailure(string.Join(", ", profileResult.Errors));
            }

            var profile = profileResult.Data!;

            var errors = new List<string>();

            // Basic validation
            if (string.IsNullOrWhiteSpace(profile.GameInstallationId))
            {
                errors.Add("Game installation is required for launch");
            }

            // A game profile must have content enabled to be launchable
            if (profile.EnabledContentIds == null || !profile.EnabledContentIds.Any())
            {
                errors.Add("At least one content item must be enabled for launch");
                return ProfileOperationResult<bool>.CreateFailure(string.Join(", ", errors));
            }

            var hasGameInstallationManifest = false;
            var hasGameClientManifest = false;

            // A game profile must explicitly enable both GameInstallation and GameClient content
            // to be considered complete and launchable:
            // - GameInstallation provides the base game files
            // - GameClient provides the executable variant to launch
            foreach (var contentId in profile.EnabledContentIds)
            {
                try
                {
                    var manifestResult = await _manifestPool.GetManifestAsync(ManifestId.Create(contentId), cancellationToken);
                    if (manifestResult.Success && manifestResult.Data != null)
                    {
                        if (manifestResult.Data.ContentType == Core.Models.Enums.ContentType.GameInstallation)
                        {
                            hasGameInstallationManifest = true;
                        }
                        else if (manifestResult.Data.ContentType == Core.Models.Enums.ContentType.GameClient)
                        {
                            hasGameClientManifest = true;
                        }
                    }
                }
                catch (ArgumentException ex)
                {
                    // Skip invalid manifest IDs
                    _logger.LogWarning(ex, "Skipping invalid manifest ID during validation: {ContentId}", contentId);
                }
            }

            if (!hasGameInstallationManifest)
            {
                errors.Add("At least one game installation content item must be enabled for launch");
            }

            if (!hasGameClientManifest)
            {
                errors.Add("At least one game client content item must be enabled for launch");
            }

            if (errors.Any())
            {
                _logger.LogWarning("Profile {ProfileId} launch validation failed: {Errors}", profile.Id, string.Join(", ", errors));
                return ProfileOperationResult<bool>.CreateFailure(string.Join(", ", errors));
            }

            _logger.LogDebug("Profile {ProfileId} launch validation successful", profile.Id);
            return ProfileOperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate launch for profile {ProfileId}", profileId);
            return ProfileOperationResult<bool>.CreateFailure($"Launch validation failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameProcessInfo>> GetLaunchStatusAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting launch status for profile {ProfileId}", profileId);

            var launches = await _launchRegistry.GetAllActiveLaunchesAsync();
            var launch = launches.FirstOrDefault(l => l.ProfileId == profileId);
            if (launch == null)
            {
                return ProfileOperationResult<GameProcessInfo>.CreateFailure($"No active launch found for profile {profileId}");
            }

            _logger.LogDebug("Profile {ProfileId} launch status: {Status}", profileId, launch.ProcessInfo.IsRunning ? "Running" : "Not Running");

            return ProfileOperationResult<GameProcessInfo>.CreateSuccess(launch.ProcessInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get launch status for profile {ProfileId}", profileId);
            return ProfileOperationResult<GameProcessInfo>.CreateFailure($"Failed to get launch status: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<bool>> StopProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Stopping profile {ProfileId}", profileId);

            var launches = await _launchRegistry.GetAllActiveLaunchesAsync();
            var launch = launches.FirstOrDefault(l => l.ProfileId == profileId);
            if (launch == null)
            {
                return ProfileOperationResult<bool>.CreateFailure($"No active launch found for profile {profileId}");
            }

            var stopResult = await _gameLauncher.TerminateGameAsync(launch.LaunchId, cancellationToken);
            if (stopResult.Failed)
            {
                return ProfileOperationResult<bool>.CreateFailure(string.Join(", ", stopResult.Errors));
            }

            _logger.LogInformation("Successfully stopped profile {ProfileId}", profileId);
            return ProfileOperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop profile {ProfileId}", profileId);
            return ProfileOperationResult<bool>.CreateFailure($"Failed to stop profile: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<WorkspaceInfo>> PrepareWorkspaceAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Preparing workspace for profile {ProfileId}", profileId);

            // Get the profile to understand what content needs to be prepared
            var profileResult = await _profileManager.GetProfileAsync(profileId, cancellationToken);
            if (profileResult.Failed)
            {
                return ProfileOperationResult<WorkspaceInfo>.CreateFailure(string.Join(", ", profileResult.Errors));
            }

            var profile = profileResult.Data!;

            // Build list of manifests from enabled content IDs only
            var manifests = new List<ContentManifest>();
            var resolvedContentIds = new HashSet<string>(profile.EnabledContentIds ?? Enumerable.Empty<string>());

            // Resolve dependencies recursively
            var resolutionResult = await _dependencyResolver.ResolveDependenciesWithManifestsAsync(profile.EnabledContentIds ?? Enumerable.Empty<string>(), cancellationToken);
            if (!resolutionResult.Success)
            {
                return ProfileOperationResult<WorkspaceInfo>.CreateFailure(string.Join(", ", resolutionResult.Errors));
            }

            manifests = resolutionResult.ResolvedManifests.ToList();

            // Create workspace configuration
            var workspaceConfig = new WorkspaceConfiguration
            {
                Id = profileId,
                Manifests = manifests,
                GameVersion = profile.GameVersion,
                Strategy = profile.WorkspaceStrategy,
                ForceRecreate = false,
                ValidateAfterPreparation = true,
            };

            // resolve installation path and workspace root
            var install = await _installationService.GetInstallationAsync(profile.GameInstallationId, cancellationToken);
            if (install.Failed || install.Data == null)
            {
                return ProfileOperationResult<WorkspaceInfo>.CreateFailure(
                    $"Failed to resolve installation '{profile.GameInstallationId}': {install.FirstError}");
            }

            workspaceConfig.BaseInstallationPath = install.Data.InstallationPath;
            workspaceConfig.WorkspaceRootPath = _config.GetWorkspacePath();

            var prepareResult = await _workspaceManager.PrepareWorkspaceAsync(workspaceConfig, cancellationToken: cancellationToken);
            if (prepareResult.Failed)
            {
                return ProfileOperationResult<WorkspaceInfo>.CreateFailure(string.Join(", ", prepareResult.Errors));
            }

            var workspaceInfo = prepareResult.Data!;
            _logger.LogInformation("Successfully prepared workspace {WorkspaceId} for profile {ProfileId}", workspaceInfo.Id, profileId);

            return ProfileOperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prepare workspace for profile {ProfileId}", profileId);
            return ProfileOperationResult<WorkspaceInfo>.CreateFailure($"Failed to prepare workspace: {ex.Message}");
        }
    }
}
