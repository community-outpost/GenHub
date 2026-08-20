using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.UserData;
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
                .Where(HasProfileUserData)
                .ToList();

            if (userDataManifests.Count == 0)
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

                        // Reinstall, but never on top of an uninstall that could not put the user's
                        // originals back: redeploying would bury the unfinished restore.
                        var uninstallResult = await userDataTracker.UninstallUserDataAsync(manifest.Id.Value, profileId, cancellationToken);
                        if (!uninstallResult.Success)
                        {
                            logger.LogError(
                                "[ProfileContentLinker] Cannot reinstall {ManifestId}: the previous installation could not be fully removed: {Error}",
                                manifest.Id.Value,
                                uninstallResult.FirstError);
                            return OperationResult<bool>.CreateFailure(uninstallResult.Errors);
                        }

                        var reinstallResult = await InstallManifestUserDataAsync(manifest, profileId, targetGame, cancellationToken);
                        if (!reinstallResult.Success)
                        {
                            return OperationResult<bool>.CreateFailure(reinstallResult);
                        }
                    }
                    else if (!existingResult.Data.IsActive)
                    {
                        logger.LogDebug("[ProfileContentLinker] Activating existing user data for {ManifestId}", manifest.Id.Value);
                    }
                }
                else
                {
                    // New installation needed
                    var installResult = await InstallManifestUserDataAsync(manifest, profileId, targetGame, cancellationToken);
                    if (!installResult.Success)
                    {
                        return OperationResult<bool>.CreateFailure(installResult);
                    }
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ProfileContentLinker] Failed to prepare user data for profile {ProfileId}", profileId);
            return OperationResult<bool>.CreateFailure($"Failed to prepare user data: {ex.Message}");
        }
    }

    /// <inheritdoc />
    /// <returns>A task representing the result of the operation.</returns>
    public async Task<OperationResult<bool>> SwitchProfileUserDataAsync(
        string? oldProfileId,
        string newProfileId,
        IEnumerable<ContentManifest> newManifests,
        GameType targetGame,
        bool skipCleanup = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(oldProfileId))
        {
            var activeProfileResult = await userDataTracker.GetActiveProfileIdAsync(cancellationToken);
            if (activeProfileResult.Success && !string.IsNullOrEmpty(activeProfileResult.Data))
            {
                oldProfileId = activeProfileResult.Data;
            }
        }

        logger.LogInformation(
            "[ProfileContentLinker] Switching user data from profile {OldProfileId} to {NewProfileId} (skipCleanup: {SkipCleanup})",
            oldProfileId ?? "(none)",
            newProfileId,
            skipCleanup);

        try
        {
            // Deactivate old profile's user data (if any) unless skipping cleanup
            if (!skipCleanup && !string.IsNullOrEmpty(oldProfileId) && oldProfileId != newProfileId)
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

            // If skipping cleanup, we might have many maps to link/verify
            if (skipCleanup && !string.IsNullOrEmpty(oldProfileId))
            {
                var oldUserDataResult = await userDataTracker.GetProfileUserDataAsync(oldProfileId, cancellationToken);
                if (oldUserDataResult.Success && oldUserDataResult.Data != null)
                {
                    var fileCount = oldUserDataResult.Data.Sum(m => m.InstalledFiles.Count);
                    if (fileCount > 100)
                    {
                        logger.LogInformation("[ProfileContentLinker] Linking large number of maps ({Count}). This might take a while.", fileCount);

                        // The UI will show a warning based on this log or via the progress reporting (if we had it here)
                    }

                    // Adopt old profile's manifests for the new profile
                    foreach (var manifest in oldUserDataResult.Data)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // Register this manifest's files for the new profile as well
                        // This ensures they are tracked and won't be deleted when switching FROM the new profile later
                        var adoptResult = await userDataTracker.InstallUserDataAsync(
                            manifest.ManifestId,
                            newProfileId,
                            targetGame,
                            manifest.InstalledFiles.Select(f => new ManifestFile
                            {
                                RelativePath = f.RelativePath,
                                Hash = f.CasHash ?? string.Empty,
                                Size = f.FileSize,
                                InstallTarget = f.InstallTarget,
                            }),
                            manifest.ManifestVersion,
                            manifest.ManifestName,
                            cancellationToken);

                        if (!adoptResult.Success)
                        {
                            logger.LogWarning("[ProfileContentLinker] Failed to adopt manifest {ManifestId} for profile {ProfileId}: {Error}", manifest.ManifestId, newProfileId, adoptResult.FirstError);
                        }
                    }
                }
            }

            // Prepare new profile's user data
            return await PrepareProfileUserDataAsync(newProfileId, newManifests, targetGame, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ProfileContentLinker] Failed to switch user data to profile {ProfileId}", newProfileId);
            return OperationResult<bool>.CreateFailure($"Failed to switch user data: {ex.Message}");
        }
    }

    /// <inheritdoc />
    /// <returns>A task representing the result of the operation.</returns>
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ProfileContentLinker] Failed to cleanup profile {ProfileId}", profileId);
            return OperationResult<bool>.CreateFailure($"Failed to cleanup profile: {ex.Message}");
        }
    }

    /// <inheritdoc />
    /// <returns>A task representing the result of the operation.</returns>
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
                : [];

            // Filter to manifests with user data
            var userDataManifests = newManifests
                .Where(HasProfileUserData)
                .ToList();

            var newManifestIds = userDataManifests.Select(m => m.Id.Value).ToHashSet();

            // Find manifests to remove (in current but not in new)
            var toRemove = currentManifestIds.Except(newManifestIds).ToList();
            var uninstallErrors = new List<string>();
            foreach (var manifestId in toRemove)
            {
                logger.LogInformation("[ProfileContentLinker] Removing deselected content: {ManifestId}", manifestId);
                var uninstallResult = await userDataTracker.UninstallUserDataAsync(manifestId, profileId, cancellationToken);
                if (!uninstallResult.Success)
                {
                    logger.LogError(
                        "[ProfileContentLinker] Failed to remove deselected content {ManifestId}: {Error}",
                        manifestId,
                        uninstallResult.FirstError);
                    uninstallErrors.AddRange(uninstallResult.Errors);
                }
            }

            // Find manifests to add (in new but not in current)
            var toAdd = userDataManifests.Where(m => !currentManifestIds.Contains(m.Id.Value)).ToList();
            foreach (var manifest in toAdd)
            {
                logger.LogInformation("[ProfileContentLinker] Installing new content: {ManifestId}", manifest.Id.Value);
                var installResult = await InstallManifestUserDataAsync(manifest, profileId, targetGame, cancellationToken);
                if (!installResult.Success)
                {
                    return OperationResult<bool>.CreateFailure(installResult);
                }
            }

            // Activate if this is the active profile
            bool shouldActivate = false;
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

            return uninstallErrors.Count > 0
                ? OperationResult<bool>.CreateFailure(uninstallErrors)
                : OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ProfileContentLinker] Failed to update profile {ProfileId}", profileId);
            return OperationResult<bool>.CreateFailure($"Failed to update profile: {ex.Message}");
        }
    }

    /// <inheritdoc />
    /// <returns>The active profile ID, or null if no profile is active.</returns>
    public string? GetActiveProfileId()
    {
        lock (_activeProfileLock)
        {
            return _activeProfileId;
        }
    }

    /// <inheritdoc />
    /// <returns>True if the specified profile is currently active; otherwise, false.</returns>
    public bool IsProfileActive(string profileId)
    {
        lock (_activeProfileLock)
        {
            return _activeProfileId == profileId;
        }
    }

    private static bool HasProfileUserData(ContentManifest manifest)
    {
        return manifest.Files.Any(f =>
            f.InstallTarget != ContentInstallTarget.System &&
            (f.InstallTarget != ContentInstallTarget.Workspace ||
             manifest.ContentType is ContentType.Map or ContentType.MapPack));
    }

    private static ManifestFile CreateUserMapsFile(ManifestFile file)
    {
        return new ManifestFile
        {
            RelativePath = file.RelativePath,
            SourceType = file.SourceType,
            InstallTarget = ContentInstallTarget.UserMapsDirectory,
            Size = file.Size,
            Hash = file.Hash,
            Permissions = file.Permissions,
            IsExecutable = file.IsExecutable,
            DownloadUrl = file.DownloadUrl,
            IsRequired = file.IsRequired,
            SourcePath = file.SourcePath,
            PatchSourceFile = file.PatchSourceFile,
            PackageInfo = file.PackageInfo,
        };
    }

    /// <summary>
    /// Installs user data files from a manifest for a specific profile.
    /// </summary>
    /// <returns>A task representing the asynchronous installation operation.</returns>
    private async Task<OperationResult<UserDataManifest>> InstallManifestUserDataAsync(
        ContentManifest manifest,
        string profileId,
        GameType targetGame,
        CancellationToken cancellationToken)
    {
        var userDataFiles = manifest.Files
            .Where(file => file.InstallTarget != ContentInstallTarget.System &&
                           (file.InstallTarget != ContentInstallTarget.Workspace ||
                            manifest.ContentType is ContentType.Map or ContentType.MapPack))
            .Select(file => (manifest.ContentType is ContentType.Map or ContentType.MapPack) &&
                            file.InstallTarget == ContentInstallTarget.Workspace
                ? CreateUserMapsFile(file)
                : file)
            .ToList();

        if (userDataFiles.Count == 0)
        {
            return OperationResult<UserDataManifest>.CreateSuccess(new UserDataManifest());
        }

        logger.LogDebug(
            "[ProfileContentLinker] Installing {Count} user data files from manifest {ManifestId}",
            userDataFiles.Count,
            manifest.Id.Value);

        return await userDataTracker.InstallUserDataAsync(
            manifest.Id.Value,
            profileId,
            targetGame,
            userDataFiles,
            manifest.Version,
            manifest.Name,
            cancellationToken);
    }
}
