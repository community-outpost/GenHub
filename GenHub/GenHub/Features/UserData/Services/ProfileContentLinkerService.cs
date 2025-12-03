using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.UserData.Services;

/// <summary>
/// Service for managing user data content (maps, replays, etc.) when switching between profiles.
/// Handles the lifecycle of content linking based on profile activation.
/// Uses hard links for efficient disk usage when possible.
/// </summary>
public class ProfileContentLinkerService(
    IUserDataTracker userDataTracker,
    ILogger<ProfileContentLinkerService> logger) : IProfileContentLinker
{
    private readonly object _activeProfileLock = new();

    private string? _activeProfileId;

    /// <inheritdoc />
    public async Task<OperationResult<bool>> PrepareProfileUserDataAsync(
        string profileId,
        IEnumerable<ContentManifest> manifests,
        GameType targetGame,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[ProfileContentLinker] Preparing user data for profile {ProfileId}", profileId);

        try
        {
            // Filter to manifests with user data files
            var userDataManifests = manifests
                .Where(m => m.Files.Any(f =>
                    f.InstallTarget != ContentInstallTarget.Workspace &&
                    f.InstallTarget != ContentInstallTarget.System))
                .ToList();

            if (!userDataManifests.Any())
            {
                logger.LogDebug("[ProfileContentLinker] No user data manifests for profile {ProfileId}", profileId);
                return OperationResult<bool>.CreateSuccess(true);
            }

            logger.LogInformation("[ProfileContentLinker] Processing {Count} manifests with user data", userDataManifests.Count);

            // Install/update each manifest's user data
            foreach (var manifest in userDataManifests)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Check if already installed
                var existingResult = await userDataTracker.GetUserDataManifestAsync(
                    manifest.Id.Value,
                    profileId,
                    cancellationToken);

                if (existingResult.Success && existingResult.Data != null)
                {
                    // Already installed - verify and activate if needed
                    var verifyResult = await userDataTracker.VerifyInstallationAsync(
                        manifest.Id.Value,
                        profileId,
                        cancellationToken);

                    if (!verifyResult.Success || !verifyResult.Data)
                    {
                        logger.LogWarning(
                            "[ProfileContentLinker] User data verification failed for {ManifestId}, reinstalling",
                            manifest.Id.Value);

                        // Reinstall
                        await userDataTracker.UninstallUserDataAsync(manifest.Id.Value, profileId, cancellationToken);
                        await InstallManifestUserDataAsync(manifest, profileId, targetGame, cancellationToken);
                    }
                    else if (!existingResult.Data.IsActive)
                    {
                        logger.LogDebug("[ProfileContentLinker] Activating existing user data for {ManifestId}", manifest.Id.Value);
                    }
                }
                else
                {
                    // New installation needed
                    await InstallManifestUserDataAsync(manifest, profileId, targetGame, cancellationToken);
                }
            }

            // Activate all user data for this profile
            var activateResult = await userDataTracker.ActivateProfileUserDataAsync(profileId, cancellationToken);
            if (!activateResult.Success)
            {
                logger.LogError("[ProfileContentLinker] Failed to activate user data for profile {ProfileId}", profileId);
                return OperationResult<bool>.CreateFailure(activateResult.FirstError ?? "Failed to activate user data");
            }

            // Set as active profile
            lock (_activeProfileLock)
            {
                _activeProfileId = profileId;
            }

            logger.LogInformation("[ProfileContentLinker] Successfully prepared user data for profile {ProfileId}", profileId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ProfileContentLinker] Failed to prepare user data for profile {ProfileId}", profileId);
            return OperationResult<bool>.CreateFailure($"Failed to prepare user data: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> SwitchProfileUserDataAsync(
        string? oldProfileId,
        string newProfileId,
        IEnumerable<ContentManifest> newManifests,
        GameType targetGame,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "[ProfileContentLinker] Switching user data from profile {OldProfileId} to {NewProfileId}",
            oldProfileId ?? "(none)",
            newProfileId);

        try
        {
            // Deactivate old profile's user data (if any)
            if (!string.IsNullOrEmpty(oldProfileId) && oldProfileId != newProfileId)
            {
                var deactivateResult = await userDataTracker.DeactivateProfileUserDataAsync(oldProfileId, cancellationToken);
                if (!deactivateResult.Success)
                {
                    logger.LogWarning(
                        "[ProfileContentLinker] Failed to deactivate old profile user data: {Error}",
                        deactivateResult.FirstError);

                    // Continue anyway - activation of new profile is more important
                }
            }

            // Prepare new profile's user data
            return await PrepareProfileUserDataAsync(newProfileId, newManifests, targetGame, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ProfileContentLinker] Failed to switch user data to profile {ProfileId}", newProfileId);
            return OperationResult<bool>.CreateFailure($"Failed to switch user data: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> CleanupDeletedProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[ProfileContentLinker] Cleaning up user data for deleted profile {ProfileId}", profileId);

        try
        {
            // Clear active profile if it's being deleted
            lock (_activeProfileLock)
            {
                if (_activeProfileId == profileId)
                {
                    _activeProfileId = null;
                }
            }

            var cleanupResult = await userDataTracker.CleanupProfileAsync(profileId, cancellationToken);
            return cleanupResult;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ProfileContentLinker] Failed to cleanup profile {ProfileId}", profileId);
            return OperationResult<bool>.CreateFailure($"Failed to cleanup profile: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> UpdateProfileUserDataAsync(
        string profileId,
        IEnumerable<ContentManifest> newManifests,
        GameType targetGame,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[ProfileContentLinker] Updating user data for profile {ProfileId}", profileId);

        try
        {
            // Get current user data for the profile
            var currentResult = await userDataTracker.GetProfileUserDataAsync(profileId, cancellationToken);
            var currentManifestIds = currentResult.Success && currentResult.Data != null
                ? currentResult.Data.Select(m => m.ManifestId).ToHashSet()
                : new HashSet<string>();

            // Filter to manifests with user data
            var userDataManifests = newManifests
                .Where(m => m.Files.Any(f =>
                    f.InstallTarget != ContentInstallTarget.Workspace &&
                    f.InstallTarget != ContentInstallTarget.System))
                .ToList();

            var newManifestIds = userDataManifests.Select(m => m.Id.Value).ToHashSet();

            // Find manifests to remove (in current but not in new)
            var toRemove = currentManifestIds.Except(newManifestIds).ToList();
            foreach (var manifestId in toRemove)
            {
                logger.LogInformation("[ProfileContentLinker] Removing deselected content: {ManifestId}", manifestId);
                await userDataTracker.UninstallUserDataAsync(manifestId, profileId, cancellationToken);
            }

            // Find manifests to add (in new but not in current)
            var toAdd = userDataManifests.Where(m => !currentManifestIds.Contains(m.Id.Value)).ToList();
            foreach (var manifest in toAdd)
            {
                logger.LogInformation("[ProfileContentLinker] Installing new content: {ManifestId}", manifest.Id.Value);
                await InstallManifestUserDataAsync(manifest, profileId, targetGame, cancellationToken);
            }

            // Activate if this is the active profile
            bool shouldActivate;
            lock (_activeProfileLock)
            {
                shouldActivate = _activeProfileId == profileId;
            }

            if (shouldActivate)
            {
                var activateResult = await userDataTracker.ActivateProfileUserDataAsync(profileId, cancellationToken);
                if (!activateResult.Success)
                {
                    logger.LogWarning("[ProfileContentLinker] Failed to activate user data: {Error}", activateResult.FirstError);
                }
            }

            logger.LogInformation(
                "[ProfileContentLinker] Updated user data for profile {ProfileId}: removed {Removed}, added {Added}",
                profileId,
                toRemove.Count,
                toAdd.Count);

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ProfileContentLinker] Failed to update profile {ProfileId}", profileId);
            return OperationResult<bool>.CreateFailure($"Failed to update profile: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public string? GetActiveProfileId()
    {
        lock (_activeProfileLock)
        {
            return _activeProfileId;
        }
    }

    /// <inheritdoc />
    public bool IsProfileActive(string profileId)
    {
        lock (_activeProfileLock)
        {
            return _activeProfileId == profileId;
        }
    }

    /// <summary>
    /// Installs user data files from a manifest for a specific profile.
    /// </summary>
    private async Task InstallManifestUserDataAsync(
        ContentManifest manifest,
        string profileId,
        GameType targetGame,
        CancellationToken cancellationToken)
    {
        var userDataFiles = manifest.Files
            .Where(f => f.InstallTarget != ContentInstallTarget.Workspace &&
                       f.InstallTarget != ContentInstallTarget.System)
            .ToList();

        if (!userDataFiles.Any())
        {
            return;
        }

        logger.LogDebug(
            "[ProfileContentLinker] Installing {Count} user data files from manifest {ManifestId}",
            userDataFiles.Count,
            manifest.Id.Value);

        await userDataTracker.InstallUserDataAsync(
            manifest.Id.Value,
            profileId,
            targetGame,
            userDataFiles,
            manifest.Version,
            cancellationToken);
    }
}
