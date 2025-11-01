using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameSettings;
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
    ICasService casService,
    IGameSettingsService gameSettingsService,
    ILogger<ProfileLauncherFacade> logger) : IProfileLauncherFacade
{
    /// <summary>
    /// Timeout for game settings application to prevent blocking the launch process.
    /// </summary>
    private static readonly TimeSpan GameSettingsApplicationTimeout = TimeSpan.FromSeconds(5);

    private readonly IGameProfileManager _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
    private readonly IGameLauncher _gameLauncher = gameLauncher ?? throw new ArgumentNullException(nameof(gameLauncher));
    private readonly IWorkspaceManager _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
    private readonly ILaunchRegistry _launchRegistry = launchRegistry ?? throw new ArgumentNullException(nameof(launchRegistry));
    private readonly IContentManifestPool _manifestPool = manifestPool ?? throw new ArgumentNullException(nameof(manifestPool));
    private readonly IGameInstallationService _installationService = installationService ?? throw new ArgumentNullException(nameof(installationService));
    private readonly IConfigurationProviderService _config = config ?? throw new ArgumentNullException(nameof(config));
    private readonly IDependencyResolver _dependencyResolver = dependencyResolver ?? throw new ArgumentNullException(nameof(dependencyResolver));
    private readonly ICasService _casService = casService ?? throw new ArgumentNullException(nameof(casService));
    private readonly IGameSettingsService _gameSettingsService = gameSettingsService ?? throw new ArgumentNullException(nameof(gameSettingsService));
    private readonly ILogger<ProfileLauncherFacade> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameLaunchInfo>> LaunchProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("=== START Launch Profile: {ProfileId} ===", profileId);

            // Get the profile
            _logger.LogDebug("[Launch] Step 1: Loading profile from repository");
            var profileResult = await _profileManager.GetProfileAsync(profileId, cancellationToken);
            if (profileResult.Failed)
            {
                _logger.LogError("[Launch] Failed to load profile: {Errors}", string.Join(", ", profileResult.Errors));
                return ProfileOperationResult<GameLaunchInfo>.CreateFailure(string.Join(", ", profileResult.Errors));
            }

            var profile = profileResult.Data!;
            _logger.LogDebug(
                "[Launch] Profile loaded - Name: '{Name}', GameType: {GameType}, EnabledContent: {ContentCount} items",
                profile.Name,
                profile.GameClient.GameType,
                profile.EnabledContentIds?.Count ?? 0);

            // Try to resolve or rebind the installation if it's stale
            _logger.LogDebug("[Launch] Step 2: Resolving game installation ID: {InstallationId}", profile.GameInstallationId);
            var resolvedInstallation = await ResolveOrRebindInstallationAsync(profile, cancellationToken);
            if (resolvedInstallation == null)
            {
                _logger.LogError("[Launch] Installation resolution failed for ID: {InstallationId}", profile.GameInstallationId);
                return ProfileOperationResult<GameLaunchInfo>.CreateFailure("Could not resolve game installation for profile");
            }

            _logger.LogDebug(
                "[Launch] Installation resolved - ID: {InstallationId}, Path: {Path}",
                resolvedInstallation.Id,
                resolvedInstallation.InstallationPath);

            // Update the profile with the resolved installation if it changed
            if (resolvedInstallation.Id != profile.GameInstallationId)
            {
                var updateRequest = new UpdateProfileRequest
                {
                    GameInstallationId = resolvedInstallation.Id,
                };
                var updateResult = await _profileManager.UpdateProfileAsync(profileId, updateRequest, cancellationToken);
                if (updateResult.Success)
                {
                    profile.GameInstallationId = resolvedInstallation.Id;
                    _logger.LogInformation("Rebound profile {ProfileId} to installation {InstallationId}", profileId, resolvedInstallation.Id);
                }
            }

            // Validate the profile before launching
            _logger.LogDebug("[Launch] Step 3: Validating profile for launch");
            var validationResult = await ValidateLaunchAsync(profileId, cancellationToken);
            if (validationResult.Failed)
            {
                _logger.LogError("[Launch] Validation failed: {Errors}", string.Join(", ", validationResult.Errors));
                return ProfileOperationResult<GameLaunchInfo>.CreateFailure(string.Join(", ", validationResult.Errors));
            }

            _logger.LogDebug("[Launch] Validation passed");

            // NOTE: Options.ini application moved to GameLauncher.LaunchProfileAsync() (before process start)
            // This eliminates duplicate writes and race conditions that caused black screens
            // See: GameLauncher.cs Line 638 - ApplyProfileSettingsToIniOptionsAsync()
            _logger.LogDebug("[Launch] Step 4: Options.ini will be applied by GameLauncher (delegated)");

            var effectiveStrategy = profile.WorkspaceStrategy;
            _logger.LogDebug("[Launch] Step 5: Checking workspace strategy and admin rights - Strategy: {Strategy}", effectiveStrategy);

            // Admin check for symlink strategies
            // NOTE: Auto-fallback to FullCopy has been disabled to allow proper testing of symlink strategies
            // Users will get a clear error if they try to use symlink without admin rights
            var isAdmin = false;
            if (OperatingSystem.IsWindows())
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
                _logger.LogInformation(
                    "Profile {ProfileId} launch - Admin check: IsAdmin={IsAdmin}, User={User}, Strategy={Strategy}",
                    profileId,
                    isAdmin,
                    identity.Name,
                    effectiveStrategy);
            }
            else
            {
                _logger.LogInformation(
                    "Profile {ProfileId} launch - Non-Windows platform, admin check skipped, Strategy={Strategy}",
                    profileId,
                    effectiveStrategy);
            }

            if (!isAdmin && (effectiveStrategy == WorkspaceStrategy.HybridCopySymlink || effectiveStrategy == WorkspaceStrategy.SymlinkOnly || effectiveStrategy == WorkspaceStrategy.HardLink))
            {
                _logger.LogWarning(
                    "Profile {ProfileId} launch blocked - Admin required for {Strategy} but user is not admin",
                    profileId,
                    effectiveStrategy);
                return ProfileOperationResult<GameLaunchInfo>.CreateFailure(
                    $"Administrator privileges required for {effectiveStrategy} workspace strategy. " +
                    $"Please restart GenHub as administrator or change the profile's workspace strategy to FullCopy in settings.");
            }

            // Launch the game using the profile
            _logger.LogDebug("[Launch] Step 6: Delegating to GameLauncher for workspace prep and process start");
            var launchResult = await _gameLauncher.LaunchProfileAsync(profile, cancellationToken: cancellationToken);
            if (launchResult.Failed)
            {
                _logger.LogError("[Launch] GameLauncher failed: {Errors}", string.Join(", ", launchResult.Errors));
                return ProfileOperationResult<GameLaunchInfo>.CreateFailure(string.Join(", ", launchResult.Errors));
            }

            var launchInfo = launchResult.Data!;
            _logger.LogInformation(
                "=== LAUNCH SUCCESS: Profile {ProfileId}, ProcessId {ProcessId} ===",
                profileId,
                launchInfo.ProcessInfo.ProcessId);

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
            var manifests = new List<ContentManifest>();

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
                        manifests.Add(manifestResult.Data);

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

            // Validate dependencies between manifests
            var dependencyErrors = ValidateDependencies(manifests, profile.GameClient.GameType);
            if (dependencyErrors.Any())
            {
                errors.AddRange(dependencyErrors);
                _logger.LogWarning("Profile {ProfileId} dependency validation failed: {Errors}", profile.Id, string.Join(", ", dependencyErrors));
                return ProfileOperationResult<bool>.CreateFailure(string.Join(", ", errors));
            }

            // CAS preflight check
            try
            {
                var casStats = await _casService.GetStatsAsync(cancellationToken);
                _logger.LogDebug("CAS preflight check passed for profile {ProfileId}: {TotalObjects} objects, {TotalSize} bytes", profile.Id, casStats.ObjectCount, casStats.TotalSize);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CAS preflight check failed for profile {ProfileId}", profile.Id);
                return ProfileOperationResult<bool>.CreateFailure("CAS system is not available");
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

            // NOTE: Workspace is NOT cleaned up when stopping - it persists across launches
            // This allows quick re-launches without re-creating symlinks/copies
            // Workspace is only cleaned up when:
            // 1. Profile is deleted
            // 2. Content changes require workspace refresh
            // 3. User manually cleans up old workspaces (future feature)
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

            // Try to resolve or rebind the installation if it's stale
            var resolvedInstallation = await ResolveOrRebindInstallationAsync(profile, cancellationToken);
            if (resolvedInstallation == null)
            {
                return ProfileOperationResult<WorkspaceInfo>.CreateFailure("Could not resolve game installation for profile");
            }

            // Update the profile with the resolved installation if it changed
            if (resolvedInstallation.Id != profile.GameInstallationId)
            {
                var updateRequest = new UpdateProfileRequest
                {
                    GameInstallationId = resolvedInstallation.Id,
                };
                var updateResult = await _profileManager.UpdateProfileAsync(profileId, updateRequest, cancellationToken);
                if (updateResult.Success)
                {
                    profile.GameInstallationId = resolvedInstallation.Id;
                    _logger.LogInformation("Rebound profile {ProfileId} to installation {InstallationId} during workspace preparation", profileId, resolvedInstallation.Id);
                }
            }

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

            // CRITICAL: CAS preflight check - verify all CAS content is available before workspace preparation
            // This prevents late failure and ensures early error detection
            _logger.LogDebug("[Workspace] Running CAS preflight check for {ManifestCount} manifests", manifests.Count);
            var casCheckResult = await VerifyCasContentAvailabilityAsync(manifests, cancellationToken);
            if (!casCheckResult.Success)
            {
                _logger.LogError("[Workspace] CAS preflight check failed: {Error}", casCheckResult.FirstError);
                return ProfileOperationResult<WorkspaceInfo>.CreateFailure(casCheckResult.FirstError ?? "Required content is not available in CAS");
            }

            _logger.LogDebug("[Workspace] CAS preflight check passed");

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
                    _logger.LogDebug("[Workspace] Source path for GameClient {ManifestId}: {SourcePath}", manifest.Id.Value, profile.GameClient.WorkingDirectory);
                    continue;
                }

                // For all other content types, query the manifest pool for the content directory
                var contentDirResult = await _manifestPool.GetContentDirectoryAsync(manifest.Id, cancellationToken);
                if (contentDirResult.Success && !string.IsNullOrEmpty(contentDirResult.Data))
                {
                    manifestSourcePaths[manifest.Id.Value] = contentDirResult.Data;
                    _logger.LogDebug(
                        "[Workspace] Source path for content {ManifestId} ({ContentType}): {SourcePath}",
                        manifest.Id.Value,
                        manifest.ContentType,
                        contentDirResult.Data);
                }
                else
                {
                    _logger.LogWarning(
                        "[Workspace] Could not resolve source path for manifest {ManifestId} ({ContentType})",
                        manifest.Id.Value,
                        manifest.ContentType);
                }
            }

            // Create workspace configuration
            if (profile.GameClient == null)
            {
                return ProfileOperationResult<WorkspaceInfo>.CreateFailure("Profile has no GameClient configured");
            }

            var workspaceConfig = new WorkspaceConfiguration
            {
                Id = profileId,
                Manifests = manifests,
                GameClient = profile.GameClient!,
                Strategy = profile.WorkspaceStrategy,
                ForceRecreate = false,
                ValidateAfterPreparation = true,
                ManifestSourcePaths = manifestSourcePaths,
            };

            // Use resolved installation path and workspace root
            if (resolvedInstallation == null || string.IsNullOrEmpty(resolvedInstallation.InstallationPath))
            {
                return ProfileOperationResult<WorkspaceInfo>.CreateFailure("Resolved installation has no valid installation path");
            }

            var installationPath = resolvedInstallation.InstallationPath;
            workspaceConfig.BaseInstallationPath = installationPath;
            workspaceConfig.WorkspaceRootPath = _config.GetWorkspacePath();

            var prepareResult = await _workspaceManager.PrepareWorkspaceAsync(workspaceConfig, cancellationToken: cancellationToken);
            if (prepareResult.Failed)
            {
                return ProfileOperationResult<WorkspaceInfo>.CreateFailure(string.Join(", ", prepareResult.Errors));
            }

            var workspaceInfo = prepareResult.Data;
            if (workspaceInfo == null)
            {
                return ProfileOperationResult<WorkspaceInfo>.CreateFailure("Workspace preparation succeeded but returned null workspace info");
            }

            // Update the profile with the active workspace ID
            var workspaceUpdateRequest = new UpdateProfileRequest
            {
                ActiveWorkspaceId = workspaceInfo.Id,
            };
            var updateProfileResult = await _profileManager.UpdateProfileAsync(profileId, workspaceUpdateRequest, cancellationToken);
            if (updateProfileResult.Failed)
            {
                _logger.LogWarning("Failed to update profile {ProfileId} with active workspace ID: {Errors}", profileId, string.Join(", ", updateProfileResult.Errors));
            }

            _logger.LogInformation("Successfully prepared workspace {WorkspaceId} for profile {ProfileId}", workspaceInfo.Id, profileId);

            return ProfileOperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prepare workspace for profile {ProfileId}", profileId);
            return ProfileOperationResult<WorkspaceInfo>.CreateFailure($"Failed to prepare workspace: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<bool>> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting profile {ProfileId}", profileId);

            if (string.IsNullOrWhiteSpace(profileId))
            {
                return ProfileOperationResult<bool>.CreateFailure("Profile ID cannot be empty");
            }

            // Check if the profile is currently running
            var launches = await _launchRegistry.GetAllActiveLaunchesAsync();
            var activeLaunch = launches.FirstOrDefault(l => l.ProfileId == profileId);
            if (activeLaunch != null)
            {
                _logger.LogWarning("Cannot delete profile {ProfileId} - it is currently running", profileId);
                return ProfileOperationResult<bool>.CreateFailure(
                    "Cannot delete a running profile. Please stop the profile before deleting it.");
            }

            // Get profile to check for active workspace before deleting
            var profileResult = await _profileManager.GetProfileAsync(profileId, cancellationToken);
            if (profileResult.Success && profileResult.Data != null && !string.IsNullOrEmpty(profileResult.Data.ActiveWorkspaceId))
            {
                _logger.LogInformation("Cleaning up workspace {WorkspaceId} for profile {ProfileId} before deletion", profileResult.Data.ActiveWorkspaceId, profileId);
                var cleanupResult = await _workspaceManager.CleanupWorkspaceAsync(profileResult.Data.ActiveWorkspaceId, cancellationToken);
                if (cleanupResult.Failed)
                {
                    _logger.LogWarning("Failed to cleanup workspace {WorkspaceId} for profile {ProfileId}: {Error}", profileResult.Data.ActiveWorkspaceId, profileId, cleanupResult.FirstError);

                    // Continue with profile deletion even if workspace cleanup fails
                }
            }

            var deleteResult = await _profileManager.DeleteProfileAsync(profileId, cancellationToken);
            if (deleteResult.Success)
            {
                _logger.LogInformation("Successfully deleted profile {ProfileId}", profileId);
                return ProfileOperationResult<bool>.CreateSuccess(true);
            }
            else
            {
                _logger.LogError("Failed to delete profile {ProfileId}: {Errors}", profileId, string.Join(", ", deleteResult.Errors));
                return ProfileOperationResult<bool>.CreateFailure(string.Join(", ", deleteResult.Errors));
            }
        }
        catch (IOException ioEx) when (ioEx.Message.Contains("being used by another process"))
        {
            _logger.LogError(ioEx, "Cannot delete profile {ProfileId} because workspace files are locked", profileId);
            return ProfileOperationResult<bool>.CreateFailure(
                "Cannot delete profile because workspace files are being used. Please ensure the game is fully stopped before deleting.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred while deleting profile {ProfileId}.", profileId);
            return ProfileOperationResult<bool>.CreateFailure("An unexpected error occurred.");
        }
    }

    /// <summary>
    /// Checks if a version string is compatible with dependency requirements.
    /// </summary>
    /// <param name="version">The version to check.</param>
    /// <param name="dependency">The dependency with version requirements.</param>
    /// <returns>True if compatible, false otherwise.</returns>
    private static bool IsVersionCompatible(string version, ContentDependency dependency)
    {
        // If compatible versions list is specified, check exact match
        if (dependency.CompatibleVersions.Any())
        {
            return dependency.CompatibleVersions.Contains(version, StringComparer.OrdinalIgnoreCase);
        }

        // Simple string comparison for min/max versions (semantic versioning would be better in production)
        // For now, we use string comparison which works for versions like "1.04", "1.08", etc.
        if (!string.IsNullOrEmpty(dependency.MinVersion))
        {
            if (string.Compare(version, dependency.MinVersion, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        if (!string.IsNullOrEmpty(dependency.MaxVersion))
        {
            if (string.Compare(version, dependency.MaxVersion, StringComparison.OrdinalIgnoreCase) > 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Builds a human-readable string describing version requirements.
    /// </summary>
    /// <param name="dependency">The dependency with version requirements.</param>
    /// <returns>A string describing the version requirements.</returns>
    private static string BuildVersionRequirementString(ContentDependency dependency)
    {
        if (dependency.CompatibleVersions.Any())
        {
            return $"(version: {string.Join(" or ", dependency.CompatibleVersions)})";
        }

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(dependency.MinVersion))
        {
            parts.Add($"version >= {dependency.MinVersion}");
        }

        if (!string.IsNullOrEmpty(dependency.MaxVersion))
        {
            parts.Add($"version <= {dependency.MaxVersion}");
        }

        return parts.Any() ? $"({string.Join(" and ", parts)})" : string.Empty;
    }

    /// <summary>
    /// Checks if a profile has custom game settings.
    /// </summary>
    private static bool HasGameSettings(GameProfile profile)
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
    /// Applies profile settings to an IniOptions object.
    /// </summary>
    private static void ApplyProfileSettingsToOptions(GameProfile profile, IniOptions options)
    {
        // Video settings
        if (profile.VideoResolutionWidth.HasValue)
        {
            options.Video.ResolutionWidth = profile.VideoResolutionWidth.Value;
        }

        if (profile.VideoResolutionHeight.HasValue)
        {
            options.Video.ResolutionHeight = profile.VideoResolutionHeight.Value;
        }

        if (profile.VideoWindowed.HasValue)
        {
            options.Video.Windowed = profile.VideoWindowed.Value;
        }

        // Map VideoTextureQuality (0-2) to TextureReduction (0-3, inverted: 0=high, 3=low)
        // VideoTextureQuality: 0=low, 1=medium, 2=high
        // TextureReduction: 0=no reduction (high), 1=some reduction, 2=more reduction, 3=max reduction (low)
        if (profile.VideoTextureQuality.HasValue)
        {
            options.Video.TextureReduction = 2 - profile.VideoTextureQuality.Value; // Invert: 2->0, 1->1, 0->2
        }

        // VideoShadows maps to UseShadowVolumes (shadows are primarily volume-based in this engine)
        if (profile.VideoShadows.HasValue)
        {
            options.Video.UseShadowVolumes = profile.VideoShadows.Value;
            options.Video.UseShadowDecals = profile.VideoShadows.Value; // Enable decals when shadows are on
        }

        // ParticleEffects doesn't have a direct Options.ini equivalent, skip for now

        // ExtraAnimations maps directly
        if (profile.VideoExtraAnimations.HasValue)
        {
            options.Video.ExtraAnimations = profile.VideoExtraAnimations.Value;
        }

        // BuildingAnimations doesn't have a direct Options.ini equivalent, skip for now
        if (profile.VideoGamma.HasValue)
        {
            options.Video.Gamma = profile.VideoGamma.Value;
        }

        // Audio settings - map from friendly names to Options.ini names
        if (profile.AudioSoundVolume.HasValue)
        {
            options.Audio.SFXVolume = profile.AudioSoundVolume.Value;
        }

        if (profile.AudioThreeDSoundVolume.HasValue)
        {
            options.Audio.SFX3DVolume = profile.AudioThreeDSoundVolume.Value;
        }

        if (profile.AudioSpeechVolume.HasValue)
        {
            options.Audio.VoiceVolume = profile.AudioSpeechVolume.Value;
        }

        if (profile.AudioMusicVolume.HasValue)
        {
            options.Audio.MusicVolume = profile.AudioMusicVolume.Value;
        }

        if (profile.AudioEnabled.HasValue)
        {
            options.Audio.AudioEnabled = profile.AudioEnabled.Value;
        }

        if (profile.AudioNumSounds.HasValue)
        {
            options.Audio.NumSounds = profile.AudioNumSounds.Value;
        }
    }

    /// <summary>
    /// Validates dependencies between manifests to ensure compatibility.
    /// </summary>
    /// <param name="manifests">The list of manifests to validate.</param>
    /// <param name="profileGameType">The game type from the profile's GameClient.</param>
    /// <returns>A list of validation error messages.</returns>
    private List<string> ValidateDependencies(List<ContentManifest> manifests, GameType profileGameType)
    {
        var errors = new List<string>();

        try
        {
            // Create a lookup for quick manifest searches
            var manifestsByType = manifests.GroupBy(m => m.ContentType).ToDictionary(g => g.Key, g => g.ToList());
            var manifestsById = manifests.ToDictionary(m => m.Id.ToString(), m => m);

            _logger.LogDebug("Validating dependencies for {Count} manifests", manifests.Count);

            foreach (var manifest in manifests)
            {
                // Skip if no dependencies
                if (manifest.Dependencies == null || !manifest.Dependencies.Any())
                {
                    continue;
                }

                _logger.LogDebug("Validating {Count} dependencies for manifest {ManifestName}", manifest.Dependencies.Count, manifest.Name);

                foreach (var dependency in manifest.Dependencies)
                {
                    // Validate by dependency type
                    if (!manifestsByType.TryGetValue(dependency.DependencyType, out var potentialMatches) || !potentialMatches.Any())
                    {
                        errors.Add($"Content '{manifest.Name}' requires {dependency.DependencyType} content, but none is selected");
                        _logger.LogWarning(
                            "Dependency validation failed: {ManifestName} requires {DependencyType} but none found",
                            manifest.Name,
                            dependency.DependencyType);
                        continue;
                    }

                    // Check if specific dependency ID is required (not a generic type-based constraint)
                    if (dependency.Id.ToString() != ManifestConstants.DefaultContentDependencyId)
                    {
                        if (!manifestsById.ContainsKey(dependency.Id.ToString()))
                        {
                            errors.Add($"Content '{manifest.Name}' requires specific content '{dependency.Name}' (ID: {dependency.Id}), but it is not selected");
                            _logger.LogWarning(
                                "Dependency validation failed: {ManifestName} requires specific dependency {DependencyId} but not found",
                                manifest.Name,
                                dependency.Id);
                            continue;
                        }

                        var requiredManifest = manifestsById[dependency.Id.ToString()];

                        // Validate version compatibility if specified
                        if (!string.IsNullOrEmpty(dependency.MinVersion) || !string.IsNullOrEmpty(dependency.MaxVersion) || dependency.CompatibleVersions.Any())
                        {
                            if (!IsVersionCompatible(requiredManifest.Version, dependency))
                            {
                                var versionInfo = BuildVersionRequirementString(dependency);
                                errors.Add($"Content '{manifest.Name}' requires '{dependency.Name}' {versionInfo}, but version {requiredManifest.Version} is selected");
                                _logger.LogWarning(
                                    "Version compatibility failed: {ManifestName} requires {DependencyName} {VersionInfo}, but {ActualVersion} found",
                                    manifest.Name,
                                    dependency.Name,
                                    versionInfo,
                                    requiredManifest.Version);
                            }
                        }
                    }
                    else
                    {
                        // Generic dependency - just check that ANY of that type exists (already validated above)
                        _logger.LogDebug("Generic dependency {DependencyType} satisfied for {ManifestName}", dependency.DependencyType, manifest.Name);
                    }

                    // Validate GameType compatibility for GameInstallation dependencies
                    if (dependency.DependencyType == Core.Models.Enums.ContentType.GameInstallation)
                    {
                        var gameInstallations = potentialMatches;
                        var compatibleInstallation = gameInstallations.FirstOrDefault(gi => gi.TargetGame == profileGameType);

                        if (compatibleInstallation == null)
                        {
                            errors.Add($"Content '{manifest.Name}' requires {profileGameType} game installation, but selected installation is for a different game");
                            _logger.LogWarning(
                                "GameType mismatch: {ManifestName} requires {RequiredGameType}, but no matching installation found",
                                manifest.Name,
                                profileGameType);
                        }
                    }

                    // Validate CompatibleGameTypes for all dependency types
                    if (dependency.CompatibleGameTypes != null && dependency.CompatibleGameTypes.Any())
                    {
                        if (!dependency.CompatibleGameTypes.Contains(profileGameType))
                        {
                            var compatibleGamesStr = string.Join(", ", dependency.CompatibleGameTypes);
                            errors.Add($"Content '{manifest.Name}' dependency '{dependency.Name}' is only compatible with {compatibleGamesStr}, but profile is for {profileGameType}");
                            _logger.LogWarning(
                                "GameType compatibility failed: {ManifestName} dependency {DependencyName} requires {CompatibleGameTypes}, but profile is {ProfileGameType}",
                                manifest.Name,
                                dependency.Name,
                                compatibleGamesStr,
                                profileGameType);
                        }
                    }

                    // Validate RequiredPublisherTypes
                    if (dependency.RequiredPublisherTypes != null && dependency.RequiredPublisherTypes.Any())
                    {
                        // Get the publisher type from the matched dependency manifest
                        var dependencyManifest = potentialMatches.FirstOrDefault();
                        if (dependencyManifest != null)
                        {
                            var publisherType = dependencyManifest.Publisher?.PublisherType ?? PublisherTypeConstants.Unknown;

                            if (!dependency.RequiredPublisherTypes.Contains(publisherType))
                            {
                                var requiredPublishersStr = string.Join(", ", dependency.RequiredPublisherTypes);
                                errors.Add($"Content '{manifest.Name}' dependency '{dependency.Name}' requires publisher type {requiredPublishersStr}, but found '{publisherType}'");
                                _logger.LogWarning(
                                    "Publisher type mismatch: {ManifestName} dependency {DependencyName} requires {RequiredPublishers}, but found {ActualPublisher}",
                                    manifest.Name,
                                    dependency.Name,
                                    requiredPublishersStr,
                                    publisherType);
                            }
                        }
                    }

                    // Validate IncompatiblePublisherTypes
                    if (dependency.IncompatiblePublisherTypes != null && dependency.IncompatiblePublisherTypes.Any())
                    {
                        // Get the publisher type from the matched dependency manifest
                        var dependencyManifest = potentialMatches.FirstOrDefault();
                        if (dependencyManifest != null)
                        {
                            var publisherType = dependencyManifest.Publisher?.PublisherType ?? PublisherTypeConstants.Unknown;

                            if (dependency.IncompatiblePublisherTypes.Contains(publisherType))
                            {
                                var incompatiblePublishersStr = string.Join(", ", dependency.IncompatiblePublisherTypes);
                                errors.Add($"Content '{manifest.Name}' dependency '{dependency.Name}' is incompatible with publisher type '{publisherType}' (incompatible: {incompatiblePublishersStr})");
                                _logger.LogWarning(
                                    "Publisher type conflict: {ManifestName} dependency {DependencyName} is incompatible with {IncompatiblePublisher}",
                                    manifest.Name,
                                    dependency.Name,
                                    publisherType);
                            }
                        }
                    }
                }

                // Check for conflicts
                if (manifest.Dependencies.Any())
                {
                    foreach (var dependency in manifest.Dependencies.Where(d => d.ConflictsWith.Any()))
                    {
                        foreach (var conflictId in dependency.ConflictsWith)
                        {
                            if (manifestsById.ContainsKey(conflictId.ToString()))
                            {
                                errors.Add($"Content '{manifest.Name}' conflicts with '{manifestsById[conflictId.ToString()].Name}' - these cannot be enabled together");
                                _logger.LogWarning(
                                    "Conflict detected: {ManifestName} conflicts with {ConflictingManifest}",
                                    manifest.Name,
                                    manifestsById[conflictId.ToString()].Name);
                            }
                        }
                    }
                }
            }

            if (errors.Any())
            {
                _logger.LogWarning("Dependency validation found {Count} errors", errors.Count);
            }
            else
            {
                _logger.LogDebug("Dependency validation passed for all manifests");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during dependency validation");
            errors.Add($"Dependency validation error: {ex.Message}");
        }

        return errors;
    }

    /// <summary>
    /// Resolves the installation for a profile, rebinding to a current installation if the original is stale.
    /// </summary>
    /// <param name="profile">The game profile.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved game installation, or null if not found.</returns>
    private async Task<Core.Models.GameInstallations.GameInstallation?> ResolveOrRebindInstallationAsync(GameProfile profile, CancellationToken cancellationToken)
    {
        try
        {
            // First try to get the installation by the stored ID
            var installationResult = await _installationService.GetInstallationAsync(profile.GameInstallationId, cancellationToken);
            if (installationResult.Success && installationResult.Data != null)
            {
                return installationResult.Data;
            }

            // If that failed, try to find a current installation that matches the game type and installation path
            _logger.LogWarning("Profile {ProfileId} references stale installation ID {InstallationId}, attempting to rebind", profile.Id, profile.GameInstallationId);

            var allInstallationsResult = await _installationService.GetAllInstallationsAsync(cancellationToken);
            if (allInstallationsResult.Success && allInstallationsResult.Data != null)
            {
                // First try to match by both game type AND installation path (most specific match)
                var exactPathMatches = allInstallationsResult.Data
                    .Where(inst =>
                        ((profile.GameClient.GameType == Core.Models.Enums.GameType.Generals && inst.HasGenerals && !string.IsNullOrEmpty(inst.GeneralsPath) && inst.GeneralsPath.Equals(profile.GameClient.WorkingDirectory, StringComparison.OrdinalIgnoreCase)) ||
                         (profile.GameClient.GameType == Core.Models.Enums.GameType.ZeroHour && inst.HasZeroHour && !string.IsNullOrEmpty(inst.ZeroHourPath) && inst.ZeroHourPath.Equals(profile.GameClient.WorkingDirectory, StringComparison.OrdinalIgnoreCase))))
                    .ToList();

                if (exactPathMatches.Count == 1)
                {
                    var matchingInstallation = exactPathMatches.First();
                    _logger.LogInformation(
                        "Rebound profile {ProfileId} from stale installation {OldId} to current installation {NewId} by path match ({Path})",
                        profile.Id,
                        profile.GameInstallationId,
                        matchingInstallation.Id,
                        profile.GameClient.WorkingDirectory);
                    return matchingInstallation;
                }
                else if (exactPathMatches.Count > 1)
                {
                    // This should never happen - multiple installations with same path
                    _logger.LogWarning(
                        "Profile {ProfileId} has {Count} installations with matching path {Path}, using first match",
                        profile.Id,
                        exactPathMatches.Count,
                        profile.GameClient.WorkingDirectory);
                    return exactPathMatches.First();
                }

                // Fallback: Match by game type only (less specific, only if single match)
                var gameTypeMatches = allInstallationsResult.Data
                    .Where(inst =>
                        (profile.GameClient.GameType == Core.Models.Enums.GameType.Generals && inst.HasGenerals) ||
                        (profile.GameClient.GameType == Core.Models.Enums.GameType.ZeroHour && inst.HasZeroHour))
                    .ToList();

                if (gameTypeMatches.Count == 1)
                {
                    var matchingInstallation = gameTypeMatches.First();
                    _logger.LogInformation(
                        "Rebound profile {ProfileId} from stale installation {OldId} to current installation {NewId} by game type match (no path match found)",
                        profile.Id,
                        profile.GameInstallationId,
                        matchingInstallation.Id);
                    return matchingInstallation;
                }
                else if (gameTypeMatches.Count > 1)
                {
                    // Multiple matching installations found - this is dangerous!
                    // Different installations may have different patches/mods.
                    // Require explicit user confirmation for rebinding.
                    _logger.LogError(
                        "Profile {ProfileId} installation {OldId} not found. " +
                        "Found {Count} alternative installations by game type. " +
                        "Automatic rebinding disabled for safety - user must edit profile to select installation.",
                        profile.Id,
                        profile.GameInstallationId,
                        gameTypeMatches.Count);
                    return null;
                }
            }

            _logger.LogError("Could not resolve or rebind installation for profile {ProfileId}", profile.Id);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving installation for profile {ProfileId}", profile.Id);
            return null;
        }
    }

    /// <summary>
    /// Applies game settings from the profile to the Options.ini file.
    /// </summary>
    /// <param name="profile">The game profile with settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task ApplyGameSettingsAsync(GameProfile profile, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("[Settings] Starting game settings application for profile {ProfileId}", profile.Id);

            // Check if profile has any custom game settings
            if (!HasGameSettings(profile))
            {
                _logger.LogDebug("[Settings] Profile {ProfileId} has no custom game settings, skipping Options.ini update", profile.Id);
                return;
            }

            var gameType = profile.GameClient.GameType;
            _logger.LogInformation("[Settings] Profile has custom settings - applying for {GameType}", gameType);

            // Load current options or create new
            _logger.LogDebug("[Settings] Loading existing Options.ini for {GameType}", gameType);
            var loadResult = await _gameSettingsService.LoadOptionsAsync(gameType);
            var options = loadResult.Success && loadResult.Data != null
                ? loadResult.Data
                : new IniOptions();

            if (loadResult.Success)
            {
                _logger.LogDebug("[Settings] Options.ini loaded successfully");
            }
            else
            {
                _logger.LogWarning("[Settings] Options.ini load failed, creating new: {Error}", loadResult.FirstError);
            }

            // Apply profile settings
            _logger.LogDebug("[Settings] Merging profile settings into Options.ini");
            ApplyProfileSettingsToOptions(profile, options);

            // Save to Options.ini
            _logger.LogDebug("[Settings] Saving modified Options.ini for {GameType}", gameType);
            var saveResult = await _gameSettingsService.SaveOptionsAsync(gameType, options);
            if (saveResult.Success)
            {
                _logger.LogInformation("[Settings] Successfully wrote Options.ini for profile {ProfileId}", profile.Id);
            }
            else
            {
                _logger.LogWarning(
                    "[Settings] Failed to save Options.ini for profile {ProfileId}: {Errors}",
                    profile.Id,
                    string.Join(", ", saveResult.Errors));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Settings] Exception applying game settings for profile {ProfileId}", profile.Id);

            // Don't fail the launch if settings can't be applied
        }
    }

    /// <summary>
    /// Verifies that all CAS content required by the manifests is available.
    /// </summary>
    /// <param name="manifests">The manifests to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success if all CAS content is available, failure with missing hash list otherwise.</returns>
    private async Task<OperationResult<bool>> VerifyCasContentAvailabilityAsync(IEnumerable<ContentManifest> manifests, CancellationToken cancellationToken)
    {
        var missingHashes = new List<string>();

        foreach (var manifest in manifests)
        {
            if (manifest.Files != null)
            {
                foreach (var file in manifest.Files.Where(f => f.SourceType == ContentSourceType.ContentAddressable && !string.IsNullOrEmpty(f.Hash)))
                {
                    var existsResult = await _casService.ExistsAsync(file.Hash, cancellationToken);
                    if (!existsResult.Success || !existsResult.Data)
                    {
                        missingHashes.Add(file.Hash);
                        _logger.LogWarning(
                            "[CAS Preflight] Missing CAS object {Hash} required by file {RelativePath} in manifest {ManifestId}",
                            file.Hash,
                            file.RelativePath,
                            manifest.Id);
                    }
                }
            }
        }

        if (missingHashes.Any())
        {
            var distinctMissing = missingHashes.Distinct().ToList();
            _logger.LogError("[CAS Preflight] Found {Count} missing CAS objects: {Hashes}", distinctMissing.Count, string.Join(", ", distinctMissing.Take(10)));
            return OperationResult<bool>.CreateFailure($"Missing {distinctMissing.Count} required CAS objects. Content must be downloaded before launching.");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }
}
