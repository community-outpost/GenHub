using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameSettings;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Workspace;
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
    IDependencyResolver dependencyResolver,
    ICasService casService,
    IGameSettingsService gameSettingsService,
    IStorageLocationService storageLocationService,
    INotificationService notificationService,
    ILogger<ProfileLauncherFacade> logger) : IProfileLauncherFacade
{
    /// <summary>
    /// Timeout for game settings application to prevent blocking the launch process.
    /// </summary>
    private static readonly TimeSpan GameSettingsApplicationTimeout = TimeSpan.FromSeconds(5);

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameLaunchInfo>> LaunchProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("=== START Launch Profile: {ProfileId} ===", profileId);

            // Get the profile
            logger.LogDebug("[Launch] Step 1: Loading profile from repository");
            var profileResult = await profileManager.GetProfileAsync(profileId, cancellationToken);
            if (profileResult.Failed)
            {
                logger.LogError("[Launch] Failed to load profile: {Errors}", string.Join(", ", profileResult.Errors));
                return ProfileOperationResult<GameLaunchInfo>.CreateFailure(string.Join(", ", profileResult.Errors));
            }

            var profile = profileResult.Data!;
            logger.LogDebug(
                "[Launch] Profile loaded - Name: '{Name}', GameType: {GameType}, EnabledContent: {ContentCount} items",
                profile.Name,
                profile.GameClient.GameType,
                profile.EnabledContentIds?.Count ?? 0);

            // Try to resolve or rebind the installation if it's stale
            logger.LogDebug("[Launch] Step 2: Resolving game installation ID: {InstallationId}", profile.GameInstallationId);
            var resolvedInstallationResult = await ResolveOrRebindInstallationAsync(profile, cancellationToken);
            if (resolvedInstallationResult.Failed)
            {
                logger.LogError("[Launch] Installation resolution failed: {Error}", resolvedInstallationResult.FirstError);
                return ProfileOperationResult<GameLaunchInfo>.CreateFailure(resolvedInstallationResult.FirstError ?? "Could not resolve game installation for profile");
            }

            var resolvedInstallation = resolvedInstallationResult.Data;
            if (resolvedInstallation == null)
            {
                return ProfileOperationResult<GameLaunchInfo>.CreateFailure("Resolved installation data is null");
            }

            logger.LogDebug(
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
                var updateResult = await profileManager.UpdateProfileAsync(profileId, updateRequest, cancellationToken);
                if (updateResult.Success)
                {
                    profile.GameInstallationId = resolvedInstallation.Id;
                    logger.LogInformation("Rebound profile {ProfileId} to installation {InstallationId}", profileId, resolvedInstallation.Id);
                }
            }

            // Validate the profile before launching
            logger.LogDebug("[Launch] Step 3: Validating profile for launch");
            var validationResult = await ValidateLaunchAsync(profileId, cancellationToken);
            if (validationResult.Failed)
            {
                logger.LogError("[Launch] Validation failed: {Errors}", string.Join(", ", validationResult.Errors));
                return ProfileOperationResult<GameLaunchInfo>.CreateFailure(string.Join(", ", validationResult.Errors));
            }

            logger.LogDebug("[Launch] Validation passed");

            // Options.ini application moved to GameLauncher.LaunchProfileAsync() (before process start)
            // This eliminates duplicate writes and race conditions that caused black screens.
            // See: GameLauncher.ApplyProfileSettingsToIniOptionsAsync()
            logger.LogDebug("[Launch] Step 4: Options.ini will be applied by GameLauncher (delegated)");

            var effectiveStrategy = profile.WorkspaceStrategy;
            logger.LogDebug("[Launch] Step 5: Checking workspace strategy and admin rights - Strategy: {Strategy}", effectiveStrategy);

            // Admin check for symlink strategies.
            // If not admin and using a symlink-based strategy, permanently switch the profile to HardLink strategy.
            var isAdmin = false;
            if (OperatingSystem.IsWindows())
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
                logger.LogInformation(
                    "Profile {ProfileId} launch - Admin check: IsAdmin={IsAdmin}, User={User}, Strategy={Strategy}",
                    profileId,
                    isAdmin,
                    identity.Name,
                    effectiveStrategy);
            }
            else
            {
                logger.LogInformation(
                    "Profile {ProfileId} launch - Non-Windows platform, admin check skipped, Strategy={Strategy}",
                    profileId,
                    effectiveStrategy);
            }

            if (!isAdmin && (effectiveStrategy == WorkspaceStrategy.HybridCopySymlink || effectiveStrategy == WorkspaceStrategy.SymlinkOnly))
            {
                // No admin rights - switch profile to HardLink strategy permanently
                var originalStrategy = effectiveStrategy;
                effectiveStrategy = WorkspaceStrategy.HardLink;

                logger.LogInformation(
                    "Profile {ProfileId} - Switching from {OriginalStrategy} to HardLink strategy due to missing admin rights",
                    profileId,
                    originalStrategy);

                notificationService.ShowInfo(
                    "Workspace Strategy Changed",
                    $"'{profile.Name}' requires admin for {originalStrategy}. Switching to HardLink strategy.",
                    NotificationDurations.Long);
            }

            // Use dynamic workspace path based on the game installation location
            var casPoolPath = storageLocationService.GetCasPoolPath(resolvedInstallation);
            var workspacePath = storageLocationService.GetWorkspacePath(resolvedInstallation);
            logger.LogInformation(
                "[Launch] Using dynamic storage paths - Installation: {InstallPath}, CAS: {CasPath}, Workspace: {WorkspacePath}",
                resolvedInstallation.InstallationPath,
                casPoolPath,
                workspacePath);

            notificationService.ShowInfo(
                "Launching Profile",
                $"Starting '{profile.Name}' with {effectiveStrategy} workspace strategy...",
                NotificationDurations.Medium);

            // Persist the effective strategy to the profile if it changed due to lack of admin rights
            if (effectiveStrategy != profile.WorkspaceStrategy)
            {
                var updateRequest = new UpdateProfileRequest
                {
                    PreferredStrategy = effectiveStrategy,
                };
                var strategyUpdateResult = await profileManager.UpdateProfileAsync(profileId, updateRequest, cancellationToken);
                if (strategyUpdateResult.Success)
                {
                    profile.WorkspaceStrategy = effectiveStrategy;
                    logger.LogInformation(
                        "Updated profile {ProfileId} workspace strategy to {Strategy} due to admin rights requirement",
                        profileId,
                        effectiveStrategy);
                }
                else
                {
                    logger.LogWarning(
                        "Failed to persist strategy change for profile {ProfileId}: {Error}",
                        profileId,
                        strategyUpdateResult.FirstError);
                }
            }

            // Launch the game using the profile
            logger.LogDebug("[Launch] Step 6: Delegating to GameLauncher for workspace prep and process start");

            var launchResult = await gameLauncher.LaunchProfileAsync(profile, cancellationToken: cancellationToken);

            if (launchResult.Failed)
            {
                logger.LogError("[Launch] GameLauncher failed: {Errors}", string.Join(", ", launchResult.Errors));

                // If HardLink failed due to cross-drive, show specific error about FullCopy
                if (profile.WorkspaceStrategy == WorkspaceStrategy.HardLink &&
                    launchResult.Errors.Any(e => e.Contains("different volumes") || e.Contains("cross-drive")))
                {
                    var gameDrive = Path.GetPathRoot(resolvedInstallation.InstallationPath);
                    var errorMessage = $"HardLink strategy failed because your workspace is on a different drive than the game on {gameDrive} drive. " +
                        $"You can manually change to FullCopy strategy (uses more disk space) or move your workspace to the same drive as your game.";

                    notificationService.ShowError(
                        "Launch Failed - Cross-Drive Issue",
                        errorMessage,
                        NotificationDurations.Critical);

                    return ProfileOperationResult<GameLaunchInfo>.CreateFailure(errorMessage);
                }

                // General error notification
                notificationService.ShowError(
                    "Launch Failed",
                    $"Cannot launch '{profile.Name}': {launchResult.FirstError ?? "Unknown error"}",
                    NotificationDurations.VeryLong);

                return ProfileOperationResult<GameLaunchInfo>.CreateFailure(string.Join(", ", launchResult.Errors));
            }

            var launchInfo = launchResult.Data!;
            logger.LogInformation(
                "=== LAUNCH SUCCESS: Profile {ProfileId}, ProcessId {ProcessId} ===",
                profileId,
                launchInfo.ProcessInfo.ProcessId);

            return ProfileOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to launch profile {ProfileId}", profileId);
            return ProfileOperationResult<GameLaunchInfo>.CreateFailure($"Failed to launch profile: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<bool>> ValidateLaunchAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Validating launch for profile {ProfileId}", profileId);

            // Get the profile first
            var profileResult = await profileManager.GetProfileAsync(profileId, cancellationToken);
            if (profileResult.Failed)
            {
                return ProfileOperationResult<bool>.CreateFailure(string.Join(", ", profileResult.Errors));
            }

            var profile = profileResult.Data!;

            List<string> errors = [];

            // Basic validation
            if (string.IsNullOrWhiteSpace(profile.GameInstallationId))
            {
                errors.Add("Game installation is required for launch");
            }

            // A game profile must have content enabled to be launchable
            if (profile.EnabledContentIds == null || profile.EnabledContentIds.Count == 0)
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
                    var manifestResult = await manifestPool.GetManifestAsync(ManifestId.Create(contentId), cancellationToken);
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
                    logger.LogWarning(ex, "Skipping invalid manifest ID during validation: {ContentId}", contentId);
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

            if (errors.Count > 0)
            {
                logger.LogWarning("Profile {ProfileId} launch validation failed: {Errors}", profile.Id, string.Join(", ", errors));
                return ProfileOperationResult<bool>.CreateFailure(string.Join(", ", errors));
            }

            // Validate dependencies between manifests
            var dependencyErrors = ValidateDependencies(manifests, profile.GameClient.GameType);
            if (dependencyErrors.Count > 0)
            {
                errors.AddRange(dependencyErrors);
                logger.LogWarning("Profile {ProfileId} dependency validation failed: {Errors}", profile.Id, string.Join(", ", dependencyErrors));
                return ProfileOperationResult<bool>.CreateFailure(string.Join(", ", errors));
            }

            // CAS preflight check
            try
            {
                var casStats = await casService.GetStatsAsync(cancellationToken);
                logger.LogDebug("CAS preflight check passed for profile {ProfileId}: {TotalObjects} objects, {TotalSize} bytes", profile.Id, casStats.ObjectCount, casStats.TotalSize);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "CAS preflight check failed for profile {ProfileId}", profile.Id);
                return ProfileOperationResult<bool>.CreateFailure("CAS system is not available");
            }

            logger.LogDebug("Profile {ProfileId} launch validation successful", profile.Id);
            return ProfileOperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to validate launch for profile {ProfileId}", profileId);
            return ProfileOperationResult<bool>.CreateFailure($"Launch validation failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameProcessInfo>> GetLaunchStatusAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Getting launch status for profile {ProfileId}", profileId);

            var launches = await launchRegistry.GetAllActiveLaunchesAsync();
            var launch = launches.FirstOrDefault(l => l.ProfileId == profileId);
            if (launch == null)
            {
                return ProfileOperationResult<GameProcessInfo>.CreateFailure($"No active launch found for profile {profileId}");
            }

            logger.LogDebug("Profile {ProfileId} launch status: {Status}", profileId, launch.ProcessInfo.IsRunning ? "Running" : "Not Running");

            return ProfileOperationResult<GameProcessInfo>.CreateSuccess(launch.ProcessInfo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get launch status for profile {ProfileId}", profileId);
            return ProfileOperationResult<GameProcessInfo>.CreateFailure($"Failed to get launch status: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<bool>> StopProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Stopping profile {ProfileId}", profileId);

            var launches = await launchRegistry.GetAllActiveLaunchesAsync();
            var launch = launches.FirstOrDefault(l => l.ProfileId == profileId);
            if (launch == null)
            {
                return ProfileOperationResult<bool>.CreateFailure($"No active launch found for profile {profileId}");
            }

            var stopResult = await gameLauncher.TerminateGameAsync(launch.LaunchId, cancellationToken);
            if (stopResult.Failed)
            {
                return ProfileOperationResult<bool>.CreateFailure(string.Join(", ", stopResult.Errors));
            }

            // Workspace is not cleaned up when stopping - it persists across launches.
            // This allows quick re-launches without re-creating symlinks/copies.
            // Workspace is only cleaned up when:
            // 1. Profile is deleted
            // 2. Content changes require workspace refresh
            logger.LogInformation("Successfully stopped profile {ProfileId}", profileId);
            return ProfileOperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to stop profile {ProfileId}", profileId);
            return ProfileOperationResult<bool>.CreateFailure($"Failed to stop profile: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<WorkspaceInfo>> PrepareWorkspaceAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Preparing workspace for profile {ProfileId}", profileId);

            // Get the profile to understand what content needs to be prepared
            var profileResult = await profileManager.GetProfileAsync(profileId, cancellationToken);
            if (profileResult.Failed)
            {
                return ProfileOperationResult<WorkspaceInfo>.CreateFailure(string.Join(", ", profileResult.Errors));
            }

            var profile = profileResult.Data!;

            // Try to resolve or rebind the installation if it's stale
            var resolvedInstallationResult = await ResolveOrRebindInstallationAsync(profile, cancellationToken);
            if (resolvedInstallationResult.Failed)
            {
                return ProfileOperationResult<WorkspaceInfo>.CreateFailure(resolvedInstallationResult.FirstError ?? "Could not resolve game installation for profile");
            }

            var resolvedInstallation = resolvedInstallationResult.Data;
            if (resolvedInstallation == null)
            {
                return ProfileOperationResult<WorkspaceInfo>.CreateFailure("Resolved installation data is null");
            }

            // Update the profile with the resolved installation if it changed
            if (resolvedInstallation.Id != profile.GameInstallationId)
            {
                var updateRequest = new UpdateProfileRequest
                {
                    GameInstallationId = resolvedInstallation.Id,
                };
                var updateResult = await profileManager.UpdateProfileAsync(profileId, updateRequest, cancellationToken);
                if (updateResult.Success)
                {
                    profile.GameInstallationId = resolvedInstallation.Id;
                    logger.LogInformation("Rebound profile {ProfileId} to installation {InstallationId} during workspace preparation", profileId, resolvedInstallation.Id);
                }
            }

            // Build list of manifests from enabled content IDs only
            var manifests = new List<ContentManifest>();
            var resolvedContentIds = new HashSet<string>(profile.EnabledContentIds ?? Enumerable.Empty<string>());

            // Resolve dependencies recursively
            var resolutionResult = await dependencyResolver.ResolveDependenciesWithManifestsAsync(profile.EnabledContentIds ?? Enumerable.Empty<string>(), cancellationToken);
            if (!resolutionResult.Success)
            {
                return ProfileOperationResult<WorkspaceInfo>.CreateFailure(string.Join(", ", resolutionResult.Errors));
            }

            manifests = [.. resolutionResult.ResolvedManifests];

            // CAS preflight check - verify all CAS content is available before workspace preparation.
            // This prevents late failure and ensures early error detection.
            logger.LogDebug("[Workspace] Running CAS preflight check for {ManifestCount} manifests", manifests.Count);
            var casCheckResult = await VerifyCasContentAvailabilityAsync(manifests, cancellationToken);
            if (!casCheckResult.Success)
            {
                logger.LogError("[Workspace] CAS preflight check failed: {Error}", casCheckResult.FirstError);
                return ProfileOperationResult<WorkspaceInfo>.CreateFailure(casCheckResult.FirstError ?? "Required content is not available in CAS");
            }

            logger.LogDebug("[Workspace] CAS preflight check passed");

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
                    logger.LogDebug("[Workspace] Source path for GameClient {ManifestId}: {SourcePath}", manifest.Id.Value, profile.GameClient.WorkingDirectory);
                    continue;
                }

                // For all other content types, query the manifest pool for the content directory
                var contentDirResult = await manifestPool.GetContentDirectoryAsync(manifest.Id, cancellationToken);
                if (contentDirResult.Success && !string.IsNullOrEmpty(contentDirResult.Data))
                {
                    manifestSourcePaths[manifest.Id.Value] = contentDirResult.Data;
                    logger.LogDebug(
                        "[Workspace] Source path for content {ManifestId} ({ContentType}): {SourcePath}",
                        manifest.Id.Value,
                        manifest.ContentType,
                        contentDirResult.Data);
                }
                else
                {
                    logger.LogWarning(
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

            // Use dynamic workspace path based on game installation location
            workspaceConfig.WorkspaceRootPath = storageLocationService.GetWorkspacePath(resolvedInstallation);

            var prepareResult = await workspaceManager.PrepareWorkspaceAsync(workspaceConfig, cancellationToken: cancellationToken);
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
            var updateProfileResult = await profileManager.UpdateProfileAsync(profileId, workspaceUpdateRequest, cancellationToken);
            if (updateProfileResult.Failed)
            {
                logger.LogWarning("Failed to update profile {ProfileId} with active workspace ID: {Errors}", profileId, string.Join(", ", updateProfileResult.Errors));
            }

            logger.LogInformation("Successfully prepared workspace {WorkspaceId} for profile {ProfileId}", workspaceInfo.Id, profileId);

            return ProfileOperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to prepare workspace for profile {ProfileId}", profileId);
            return ProfileOperationResult<WorkspaceInfo>.CreateFailure($"Failed to prepare workspace: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<bool>> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Deleting profile {ProfileId}", profileId);

            if (string.IsNullOrWhiteSpace(profileId))
            {
                return ProfileOperationResult<bool>.CreateFailure("Profile ID cannot be empty");
            }

            // Acquire the profile launch lock to ensure we don't delete during launch registration
            // This uses the same semaphore as launch operations, so deletion waits for launch
            // to complete its initial registration without polling or timeouts
            using (await gameLauncher.AcquireProfileLockAsync(profileId, cancellationToken))
            {
                // Check if the profile is currently running
                var launches = await launchRegistry.GetAllActiveLaunchesAsync();
                var activeLaunch = launches.FirstOrDefault(l => l.ProfileId == profileId);
                if (activeLaunch != null)
                {
                    // Double-check that the process is actually running (not in a transitional state)
                    var isProcessRunning = false;
                    try
                    {
                        var process = Process.GetProcessById(activeLaunch.ProcessInfo.ProcessId);
                        isProcessRunning = !process.HasExited;
                        process.Dispose();
                    }
                    catch (ArgumentException)
                    {
                        // Process doesn't exist - safe to delete
                        logger.LogDebug("Process {ProcessId} for profile {ProfileId} no longer exists, allowing deletion", activeLaunch.ProcessInfo.ProcessId, profileId);
                        isProcessRunning = false;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to verify process status for profile {ProfileId}, blocking deletion for safety", profileId);
                        isProcessRunning = true;
                    }

                    if (isProcessRunning)
                    {
                        logger.LogWarning("Cannot delete profile {ProfileId} - process {ProcessId} is still running", profileId, activeLaunch.ProcessInfo.ProcessId);
                        return ProfileOperationResult<bool>.CreateFailure(
                            "Cannot delete a running profile. Please stop the profile before deleting it.");
                    }

                    // Process has exited but registry hasn't been cleaned up yet - safe to proceed
                    logger.LogDebug("Profile {ProfileId} launch is in registry but process has exited, allowing deletion", profileId);
                }

                // Get profile to check for active workspace before deleting
                var profileResult = await profileManager.GetProfileAsync(profileId, cancellationToken);
                if (profileResult.Success && profileResult.Data != null && !string.IsNullOrEmpty(profileResult.Data.ActiveWorkspaceId))
                {
                    logger.LogInformation("Cleaning up workspace {WorkspaceId} for profile {ProfileId} before deletion", profileResult.Data.ActiveWorkspaceId, profileId);
                    var cleanupResult = await workspaceManager.CleanupWorkspaceAsync(profileResult.Data.ActiveWorkspaceId, cancellationToken);
                    if (cleanupResult.Failed)
                    {
                        logger.LogWarning("Failed to cleanup workspace {WorkspaceId} for profile {ProfileId}: {Error}", profileResult.Data.ActiveWorkspaceId, profileId, cleanupResult.FirstError);

                        // Continue with profile deletion even if workspace cleanup fails
                    }
                }

                var deleteResult = await profileManager.DeleteProfileAsync(profileId, cancellationToken);
                if (deleteResult.Success)
                {
                    logger.LogInformation("Successfully deleted profile {ProfileId}", profileId);
                    return ProfileOperationResult<bool>.CreateSuccess(true);
                }
                else
                {
                    logger.LogError("Failed to delete profile {ProfileId}: {Errors}", profileId, string.Join(", ", deleteResult.Errors));
                    return ProfileOperationResult<bool>.CreateFailure(string.Join(", ", deleteResult.Errors));
                }
            }
        }
        catch (IOException ioEx) when (ioEx.Message.Contains("being used by another process"))
        {
            logger.LogError(ioEx, "Cannot delete profile {ProfileId} because workspace files are locked", profileId);
            return ProfileOperationResult<bool>.CreateFailure(
                "Cannot delete profile because workspace files are being used. Please ensure the game is fully stopped before deleting.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while deleting profile {ProfileId}.", profileId);
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
        if (dependency.CompatibleVersions.Count > 0)
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
        if (dependency.CompatibleVersions.Count > 0)
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

        return parts.Count > 0 ? $"({string.Join(" and ", parts)})" : string.Empty;
    }

    /// <summary>
    /// Validates dependencies between manifests to ensure compatibility.
    /// </summary>
    /// <param name="manifests">The list of manifests to validate.</param>
    /// <param name="profileGameType">The game type from the profile's GameClient.</param>
    /// <returns>A list of validation error messages.</returns>
    private List<string> ValidateDependencies(List<ContentManifest> manifests, GameType profileGameType)
    {
        List<string> errors = [];

        try
        {
            // Create a lookup for quick manifest searches
            var manifestsByType = manifests.GroupBy(m => m.ContentType).ToDictionary(g => g.Key, g => g.ToList());
            var manifestsById = manifests.ToDictionary(m => m.Id.ToString(), m => m);

            logger.LogDebug("Validating dependencies for {Count} manifests", manifests.Count);

            foreach (var manifest in manifests)
            {
                // Skip if no dependencies
                if (manifest.Dependencies == null || manifest.Dependencies.Count == 0)
                {
                    continue;
                }

                logger.LogDebug("Validating {Count} dependencies for manifest {ManifestName}", manifest.Dependencies.Count, manifest.Name);

                foreach (var dependency in manifest.Dependencies)
                {
                    // Validate by dependency type
                    if (!manifestsByType.TryGetValue(dependency.DependencyType, out var potentialMatches) || potentialMatches.Count == 0)
                    {
                        errors.Add($"Content '{manifest.Name}' requires {dependency.DependencyType} content, but none is selected");
                        logger.LogWarning(
                            "Dependency validation failed: {ManifestName} requires {DependencyType} but none found",
                            manifest.Name,
                            dependency.DependencyType);
                        continue;
                    }

                    // Check if specific dependency ID is required (not a generic type-based constraint)
                    if (dependency.Id.ToString() != ManifestConstants.DefaultContentDependencyId)
                    {
                        ContentManifest? requiredManifest = null;

                        // First try exact ID match
                        if (manifestsById.TryGetValue(dependency.Id.ToString(), out var exactMatch))
                        {
                            requiredManifest = exactMatch;
                        }

                        // If StrictPublisher is false, try semantic matching (any publisher satisfies the dependency)
                        else if (!dependency.StrictPublisher)
                        {
                            // Parse the dependency ID to get contentType and contentName segments
                            // Format: schemaVersion.userVersion.publisher.contentType.contentName
                            var depIdSegments = dependency.Id.ToString().Split('.');
                            if (depIdSegments.Length >= 5)
                            {
                                var depContentType = depIdSegments[3];
                                var depContentName = depIdSegments[4];

                                // Find any manifest that matches contentType and contentName (regardless of publisher)
                                requiredManifest = potentialMatches.FirstOrDefault(m =>
                                {
                                    var manifestIdSegments = m.Id.ToString().Split('.');
                                    if (manifestIdSegments.Length >= 5)
                                    {
                                        var manifestContentType = manifestIdSegments[3];
                                        var manifestContentName = manifestIdSegments[4];
                                        return string.Equals(manifestContentType, depContentType, StringComparison.OrdinalIgnoreCase) &&
                                               string.Equals(manifestContentName, depContentName, StringComparison.OrdinalIgnoreCase);
                                    }

                                    return false;
                                });

                                if (requiredManifest != null)
                                {
                                    logger.LogDebug(
                                        "Semantic dependency match: {DependencyId} satisfied by {MatchedId} (StrictPublisher=false)",
                                        dependency.Id,
                                        requiredManifest.Id);
                                }
                            }
                        }

                        if (requiredManifest == null)
                        {
                            errors.Add($"Content '{manifest.Name}' requires specific content '{dependency.Name}' (ID: {dependency.Id}), but it is not selected");
                            logger.LogWarning(
                                "Dependency validation failed: {ManifestName} requires specific dependency {DependencyId} but not found",
                                manifest.Name,
                                dependency.Id);
                            continue;
                        }

                        // Validate version compatibility if specified
                        if (!string.IsNullOrEmpty(dependency.MinVersion) || !string.IsNullOrEmpty(dependency.MaxVersion) || dependency.CompatibleVersions.Count > 0)
                        {
                            if (!IsVersionCompatible(requiredManifest.Version, dependency))
                            {
                                var versionInfo = BuildVersionRequirementString(dependency);
                                errors.Add($"Content '{manifest.Name}' requires '{dependency.Name}' {versionInfo}, but version {requiredManifest.Version} is selected");
                                logger.LogWarning(
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
                        // Generic dependency - just check that any of that type exists (already validated above)
                        logger.LogDebug("Generic dependency {DependencyType} satisfied for {ManifestName}", dependency.DependencyType, manifest.Name);
                    }

                    // Validate GameType compatibility for GameInstallation dependencies
                    if (dependency.DependencyType == Core.Models.Enums.ContentType.GameInstallation)
                    {
                        var gameInstallations = potentialMatches;
                        var compatibleInstallation = gameInstallations.FirstOrDefault(gi => gi.TargetGame == profileGameType);

                        if (compatibleInstallation == null)
                        {
                            errors.Add($"Content '{manifest.Name}' requires {profileGameType} game installation, but selected installation is for a different game");
                            logger.LogWarning(
                                "GameType mismatch: {ManifestName} requires {RequiredGameType}, but no matching installation found",
                                manifest.Name,
                                profileGameType);
                        }
                    }

                    // Validate CompatibleGameTypes for all dependency types
                    if (dependency.CompatibleGameTypes != null && dependency.CompatibleGameTypes.Count > 0)
                    {
                        if (!dependency.CompatibleGameTypes.Contains(profileGameType))
                        {
                            var compatibleGamesStr = string.Join(", ", dependency.CompatibleGameTypes);
                            errors.Add($"Content '{manifest.Name}' dependency '{dependency.Name}' is only compatible with {compatibleGamesStr}, but profile is for {profileGameType}");
                            logger.LogWarning(
                                "GameType compatibility failed: {ManifestName} dependency {DependencyName} requires {CompatibleGameTypes}, but profile is {ProfileGameType}",
                                manifest.Name,
                                dependency.Name,
                                compatibleGamesStr,
                                profileGameType);
                        }
                    }

                    // Validate RequiredPublisherTypes (using StrictPublisher and PublisherType)
                    if (dependency.StrictPublisher && !string.IsNullOrEmpty(dependency.PublisherType))
                    {
                        // Get the publisher type from the matched dependency manifest
                        var dependencyManifest = potentialMatches.FirstOrDefault();
                        if (dependencyManifest != null)
                        {
                            var publisherType = dependencyManifest.Publisher?.PublisherType ?? PublisherTypeConstants.Unknown;

                            if (!string.Equals(dependency.PublisherType, publisherType, StringComparison.OrdinalIgnoreCase))
                            {
                                errors.Add($"Content '{manifest.Name}' dependency '{dependency.Name}' requires publisher type '{dependency.PublisherType}', but found '{publisherType}'");
                                logger.LogWarning(
                                    "Publisher type mismatch: {ManifestName} dependency {DependencyName} requires {RequiredPublisher}, but found {ActualPublisher}",
                                    manifest.Name,
                                    dependency.Name,
                                    dependency.PublisherType,
                                    publisherType);
                            }
                        }
                    }

                    // Validate IncompatiblePublisherTypes (not implemented in current ContentDependency model)
                    /*
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
                                logger.LogWarning(
                                    "Publisher type conflict: {ManifestName} dependency {DependencyName} is incompatible with {IncompatiblePublisher}",
                                    manifest.Name,
                                    dependency.Name,
                                    publisherType);
                            }
                        }
                    }
                    */
                }

                if (manifest.Dependencies.Count > 0)
                {
                    foreach (var dependency in manifest.Dependencies.Where(d => d.ConflictsWith.Count > 0))
                    {
                        foreach (var conflictId in dependency.ConflictsWith)
                        {
                            if (manifestsById.ContainsKey(conflictId.ToString()))
                            {
                                errors.Add($"Content '{manifest.Name}' conflicts with '{manifestsById[conflictId.ToString()].Name}' - these cannot be enabled together");
                                logger.LogWarning(
                                    "Conflict detected: {ManifestName} conflicts with {ConflictingManifest}",
                                    manifest.Name,
                                    manifestsById[conflictId.ToString()].Name);
                            }
                        }
                    }
                }
            }

            if (errors.Count > 0)
            {
                logger.LogWarning("Dependency validation found {Count} errors", errors.Count);
            }
            else
            {
                logger.LogDebug("Dependency validation passed for all manifests");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during dependency validation");
            errors.Add($"Dependency validation error: {ex.Message}");
        }

        return errors;
    }

    /// <summary>
    /// Resolves the installation for a profile, rebinding to a current installation if the original is stale.
    /// </summary>
    /// <param name="profile">The game profile.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved game installation, or failure result if not found.</returns>
    private async Task<OperationResult<Core.Models.GameInstallations.GameInstallation>> ResolveOrRebindInstallationAsync(GameProfile profile, CancellationToken cancellationToken)
    {
        try
        {
            // First try to get the installation by the stored ID
            var installationResult = await installationService.GetInstallationAsync(profile.GameInstallationId, cancellationToken);
            if (installationResult.Success && installationResult.Data != null)
            {
                return OperationResult<Core.Models.GameInstallations.GameInstallation>.CreateSuccess(installationResult.Data);
            }

            // If that failed, try to find a current installation that matches the game type and installation path
            logger.LogWarning("Profile {ProfileId} references stale installation ID {InstallationId}, attempting to rebind", profile.Id, profile.GameInstallationId);

            var allInstallationsResult = await installationService.GetAllInstallationsAsync(cancellationToken);
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
                    logger.LogInformation(
                        "Rebound profile {ProfileId} from stale installation {OldId} to current installation {NewId} by path match ({Path})",
                        profile.Id,
                        profile.GameInstallationId,
                        matchingInstallation.Id,
                        profile.GameClient.WorkingDirectory);
                    return OperationResult<Core.Models.GameInstallations.GameInstallation>.CreateSuccess(matchingInstallation);
                }
                else if (exactPathMatches.Count > 1)
                {
                    // This should never happen - multiple installations with same path
                    logger.LogWarning(
                        "Profile {ProfileId} has {Count} installations with matching path {Path}, using first match",
                        profile.Id,
                        exactPathMatches.Count,
                        profile.GameClient.WorkingDirectory);
                    return OperationResult<Core.Models.GameInstallations.GameInstallation>.CreateSuccess(exactPathMatches.First());
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
                    logger.LogInformation(
                        "Rebound profile {ProfileId} from stale installation {OldId} to current installation {NewId} by game type match (no path match found)",
                        profile.Id,
                        profile.GameInstallationId,
                        matchingInstallation.Id);
                    return OperationResult<Core.Models.GameInstallations.GameInstallation>.CreateSuccess(matchingInstallation);
                }
                else if (gameTypeMatches.Count > 1)
                {
                    // Multiple matching installations found - this is dangerous!
                    // Different installations may have different patches/mods.
                    // Require explicit user confirmation for rebinding.
                    var message =
                        $"Found {gameTypeMatches.Count} installations for {profile.GameClient.GameType}. " +
                        $"Please edit the profile to manually select the correct installation to avoid conflicts.";

                    logger.LogError(
                        "Profile {ProfileId} installation {OldId} not found. " +
                        "Found {Count} alternative installations - requiring manual selection.",
                        profile.Id,
                        profile.GameInstallationId,
                        gameTypeMatches.Count);

                    return OperationResult<Core.Models.GameInstallations.GameInstallation>.CreateFailure(message);
                }
            }

            logger.LogError("Could not resolve or rebind installation for profile {ProfileId}", profile.Id);
            return OperationResult<Core.Models.GameInstallations.GameInstallation>.CreateFailure(
                $"No valid installation found for {profile.GameClient.GameType}. " +
                $"Please verify your game installation and update the profile settings.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error resolving installation for profile {ProfileId}", profile.Id);
            return OperationResult<Core.Models.GameInstallations.GameInstallation>.CreateFailure(
                $"Failed to resolve game installation: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies game settings from the profile to the Options.ini file.
    /// </summary>
    /// <param name="profile">The game profile with settings.</param>
    private async Task ApplyGameSettingsAsync(GameProfile profile)
    {
        try
        {
            logger.LogDebug("[Settings] Starting game settings application for profile {ProfileId}", profile.Id);

            // Check if profile has any custom game settings
            if (!profile.HasCustomSettings())
            {
                logger.LogDebug("[Settings] Profile {ProfileId} has no custom game settings, skipping Options.ini update", profile.Id);
                return;
            }

            var gameType = profile.GameClient.GameType;
            logger.LogInformation("[Settings] Profile has custom settings - applying for {GameType}", gameType);

            // Load current options or create new
            logger.LogDebug("[Settings] Loading existing Options.ini for {GameType}", gameType);
            var loadResult = await gameSettingsService.LoadOptionsAsync(gameType);
            var options = loadResult.Success && loadResult.Data != null
                ? loadResult.Data
                : new IniOptions();

            if (loadResult.Success)
            {
                logger.LogDebug("[Settings] Options.ini loaded successfully");
            }
            else
            {
                logger.LogWarning("[Settings] Options.ini load failed, creating new: {Error}", loadResult.FirstError);
            }

            // Apply profile settings
            logger.LogDebug("[Settings] Merging profile settings into Options.ini");
            GameSettingsMapper.ApplyToOptions(profile, options, logger);

            // Save to Options.ini
            logger.LogDebug("[Settings] Saving modified Options.ini for {GameType}", gameType);
            var saveResult = await gameSettingsService.SaveOptionsAsync(gameType, options);
            if (saveResult.Success)
            {
                logger.LogInformation("[Settings] Successfully wrote Options.ini for profile {ProfileId}", profile.Id);
            }
            else
            {
                logger.LogWarning(
                    "[Settings] Failed to save Options.ini for profile {ProfileId}: {Errors}",
                    profile.Id,
                    string.Join(", ", saveResult.Errors));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Settings] Exception applying game settings for profile {ProfileId}", profile.Id);

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
        List<string> missingHashes = [];

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
                        logger.LogWarning(
                            "[CAS Preflight] Missing CAS object {Hash} required by file {RelativePath} in manifest {ManifestId}",
                            file.Hash,
                            file.RelativePath,
                            manifest.Id);
                    }
                }
            }
        }

        if (missingHashes.Count > 0)
        {
            var distinctMissing = missingHashes.Distinct().ToList();
            logger.LogError("[CAS Preflight] Found {Count} missing CAS objects: {Hashes}", distinctMissing.Count, string.Join(", ", distinctMissing.Take(10)));
            return OperationResult<bool>.CreateFailure($"Missing {distinctMissing.Count} required CAS objects. Content must be downloaded before launching.");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }
}
