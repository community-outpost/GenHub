using System;
using System.Collections.Concurrent;
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
    private const string UnknownErrorMessage = "unknown error";
    private static readonly ConcurrentDictionary<GameType, SemaphoreSlim> _gameSyncLocks = new();
    private static readonly ConcurrentDictionary<GameType, string> _activeProfileByGame = new();

    /// <inheritdoc />
    public async Task<OperationResult<bool>> PrepareProfileUserDataAsync(
        string profileId,
        IEnumerable<ContentManifest> manifests,
        GameType targetGame,
        CancellationToken cancellationToken = default)
    {
        var gameLock = GetGameLock(targetGame);
        await gameLock.WaitAsync(cancellationToken);
        try
        {
            return await PrepareProfileUserDataInternalAsync(profileId, manifests, targetGame, cancellationToken);
        }
        finally
        {
            gameLock.Release();
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
        logger.LogInformation(
            "[ProfileContentLinker] Switching user data from profile {OldProfileId} to {NewProfileId} (skipCleanup: {SkipCleanup})",
            oldProfileId ?? "(none)",
            newProfileId,
            skipCleanup);

        var gameLock = GetGameLock(targetGame);
        await gameLock.WaitAsync(cancellationToken);
        try
        {
            // If skipping cleanup, adopt old profile's manifests for the new profile
            if (skipCleanup && !string.IsNullOrEmpty(oldProfileId))
            {
                var oldUserDataResult = await userDataTracker.GetProfileUserDataAsync(oldProfileId, cancellationToken);
                if (oldUserDataResult.Success && oldUserDataResult.Data != null)
                {
                    var matchingManifests = oldUserDataResult.Data
                        .Where(m => m.TargetGame == targetGame || m.TargetGame == GameType.Unknown)
                        .ToList();

                    var fileCount = matchingManifests.Sum(m => m.InstalledFiles.Count);
                    if (fileCount > 100)
                    {
                        logger.LogInformation("[ProfileContentLinker] Linking large number of maps ({Count}). This might take a while.", fileCount);
                    }

                    // Register this manifest's files for the new profile as well
                    // This ensures they are tracked and won't be deleted when switching FROM the new profile later
                    foreach (var manifest in matchingManifests)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var adoptRes = await userDataTracker.InstallUserDataAsync(
                            manifest.ManifestId,
                            newProfileId,
                            targetGame,
                            manifest.InstalledFiles.Select(f => new ManifestFile
                            {
                                RelativePath = f.RelativePath,
                                Hash = f.SourceHash ?? f.CasHash ?? string.Empty,
                                Size = f.FileSize,
                                InstallTarget = f.InstallTarget,
                            }),
                            manifest.ManifestVersion,
                            manifest.ManifestName,
                            cancellationToken);

                        if (!adoptRes.Success)
                        {
                            logger.LogError("[ProfileContentLinker] Failed to adopt user data for manifest {ManifestId} into profile {NewProfileId}: {Error}", manifest.ManifestId, newProfileId, adoptRes.FirstError);
                            return OperationResult<bool>.CreateFailure(adoptRes.FirstError ?? $"Failed to adopt user data for manifest {manifest.ManifestId}");
                        }
                    }
                }
            }

            // Prepare new profile's user data (deactivates other active profiles for targetGame)
            return await PrepareProfileUserDataInternalAsync(newProfileId, newManifests, targetGame, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ProfileContentLinker] Failed to switch user data to profile {ProfileId}", newProfileId);
            return OperationResult<bool>.CreateFailure($"Failed to switch user data: {ex.Message}");
        }
        finally
        {
            gameLock.Release();
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
            foreach (var kvp in _activeProfileByGame.Where(k => string.Equals(k.Value, profileId, StringComparison.OrdinalIgnoreCase)))
            {
                _activeProfileByGame.TryRemove(KeyValuePair.Create(kvp.Key, kvp.Value));
            }

            var cleanupResult = await userDataTracker.CleanupProfileAsync(profileId, cancellationToken);
            return cleanupResult;
        }
        catch (OperationCanceledException)
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
        var gameLock = GetGameLock(targetGame);
        await gameLock.WaitAsync(cancellationToken);
        try
        {
            logger.LogInformation("[ProfileContentLinker] Updating user data for profile {ProfileId}", profileId);

            // Get current user data for the profile
            var currentResult = await userDataTracker.GetProfileUserDataAsync(profileId, cancellationToken);
            var currentManifests = currentResult.Success && currentResult.Data != null
                ? currentResult.Data.Where(m => m.TargetGame == targetGame || m.TargetGame == GameType.Unknown).ToList()
                : [];
            var currentManifestIds = currentManifests.Select(m => m.ManifestId).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Filter to manifests with user data
            var userDataManifests = newManifests.Where(HasProfileUserData).ToList();

            var newManifestIds = userDataManifests.Select(m => m.Id.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

            bool shouldActivate = currentManifests.Any(m => m.IsActive) ||
                (_activeProfileByGame.TryGetValue(targetGame, out var activeId) &&
                 string.Equals(activeId, profileId, StringComparison.OrdinalIgnoreCase));

            var syncContext = new UserDataSyncContext(profileId, targetGame, currentManifests, shouldActivate);

            var uninstalledSoFar = new List<string>();
            var installedSoFar = new List<ContentManifest>();

            // Find manifests to remove (in current but not in new)
            var toRemove = currentManifestIds.Except(newManifestIds, StringComparer.OrdinalIgnoreCase).ToList();
            var removeResult = await RemoveDeselectedContentAsync(syncContext, toRemove, installedSoFar, uninstalledSoFar, cancellationToken);
            if (!removeResult.Success)
            {
                return removeResult;
            }

            // Find manifests to add (in new but not in current)
            var toAdd = userDataManifests.Where(m => !currentManifestIds.Contains(m.Id.Value)).ToList();
            var installResult = await InstallAddedContentAsync(syncContext, toAdd, installedSoFar, uninstalledSoFar, cancellationToken);
            if (!installResult.Success)
            {
                return installResult;
            }

            if (shouldActivate)
            {
                var activateOp = await ActivateUpdatedUserDataAsync(profileId, targetGame, toAdd, toRemove, currentManifests, cancellationToken);
                if (!activateOp.Success)
                {
                    return activateOp;
                }
            }

            logger.LogInformation(
                "[ProfileContentLinker] Updated user data for profile {ProfileId}: removed {Removed}, added {Added}",
                profileId,
                toRemove.Count,
                toAdd.Count);

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ProfileContentLinker] Failed to update profile {ProfileId}", profileId);
            return OperationResult<bool>.CreateFailure($"Failed to update profile: {ex.Message}");
        }
        finally
        {
            gameLock.Release();
        }
    }

    /// <inheritdoc />
    /// <returns>The active profile ID, or null if no profile is active.</returns>
    public string? GetActiveProfileId()
    {
        return _activeProfileByGame.Values.FirstOrDefault();
    }

    /// <inheritdoc />
    /// <returns>The active profile ID for the specified game type, or null if no profile is active.</returns>
    public string? GetActiveProfileId(GameType targetGame)
    {
        return _activeProfileByGame.TryGetValue(targetGame, out var activeId) ? activeId : null;
    }

    /// <inheritdoc />
    /// <returns>True if the specified profile is currently active; otherwise, false.</returns>
    public bool IsProfileActive(string profileId)
    {
        return _activeProfileByGame.Values.Any(id => string.Equals(id, profileId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Clears the active profiles map. Intended for use in unit tests to ensure test isolation.
    /// </summary>
    internal static void ResetActiveProfilesForTesting()
    {
        _activeProfileByGame.Clear();
    }

    private static SemaphoreSlim GetGameLock(GameType gameType) =>
        _gameSyncLocks.GetOrAdd(gameType, static _ => new SemaphoreSlim(1, 1));

    private static bool HasProfileUserData(ContentManifest manifest)
    {
        return GetUserDataFiles(manifest).Count > 0;
    }

    private static IReadOnlyList<ManifestFile> GetUserDataFiles(ContentManifest manifest)
    {
        return manifest.Files
            .Where(file => file.InstallTarget != ContentInstallTarget.System &&
                           (file.InstallTarget != ContentInstallTarget.Workspace ||
                            manifest.ContentType is ContentType.Map or ContentType.MapPack))
            .Select(file => (manifest.ContentType is ContentType.Map or ContentType.MapPack) &&
                            file.InstallTarget == ContentInstallTarget.Workspace
                ? CreateUserMapsFile(file)
                : file)
            .ToList();
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

    private sealed record UserDataSyncContext(
        string ProfileId,
        GameType TargetGame,
        IReadOnlyList<UserDataManifest> CurrentManifests,
        bool ShouldActivate);

    private async Task<OperationResult<bool>> RemoveDeselectedContentAsync(
        UserDataSyncContext context,
        IReadOnlyList<string> toRemove,
        IReadOnlyList<ContentManifest> installedSoFar,
        List<string> uninstalledSoFar,
        CancellationToken cancellationToken)
    {
        foreach (var manifestId in toRemove)
        {
            logger.LogInformation("[ProfileContentLinker] Removing deselected content: {ManifestId}", manifestId);
            var uninstallRes = await userDataTracker.UninstallUserDataAsync(manifestId, context.ProfileId, cancellationToken);
            if (!uninstallRes.Success)
            {
                logger.LogError("[ProfileContentLinker] Failed to uninstall user data for manifest {ManifestId}: {Error}", manifestId, uninstallRes.FirstError);
                var rollbackFailed = await RollbackSyncAsync(context.ProfileId, context.TargetGame, installedSoFar, uninstalledSoFar, context.CurrentManifests, context.ShouldActivate);
                var errorMessage = rollbackFailed
                    ? $"Failed to remove user data for manifest {manifestId}: {uninstallRes.FirstError ?? UnknownErrorMessage} (live rollback was incomplete)"
                    : uninstallRes.FirstError ?? $"Failed to remove user data for manifest {manifestId}";
                return OperationResult<bool>.CreateFailure(errorMessage);
            }

            uninstalledSoFar.Add(manifestId);
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private async Task<OperationResult<bool>> InstallAddedContentAsync(
        UserDataSyncContext context,
        IReadOnlyList<ContentManifest> toAdd,
        List<ContentManifest> installedSoFar,
        IReadOnlyList<string> uninstalledSoFar,
        CancellationToken cancellationToken)
    {
        foreach (var manifest in toAdd)
        {
            logger.LogInformation("[ProfileContentLinker] Installing new content: {ManifestId}", manifest.Id.Value);
            var installRes = await InstallManifestUserDataAsync(manifest, context.ProfileId, context.TargetGame, cancellationToken);
            if (!installRes.Success)
            {
                logger.LogError("[ProfileContentLinker] Failed to install user data for manifest {ManifestId}: {Error}", manifest.Id.Value, installRes.FirstError);
                var rollbackFailed = await RollbackSyncAsync(context.ProfileId, context.TargetGame, installedSoFar, uninstalledSoFar, context.CurrentManifests, context.ShouldActivate);
                var errorMessage = rollbackFailed
                    ? $"Failed to install user data for manifest {manifest.Id.Value}: {installRes.FirstError ?? UnknownErrorMessage} (live rollback was incomplete)"
                    : installRes.FirstError ?? $"Failed to install user data for manifest {manifest.Id.Value}";
                return OperationResult<bool>.CreateFailure(errorMessage);
            }

            installedSoFar.Add(manifest);
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private async Task<OperationResult<bool>> ActivateUpdatedUserDataAsync(
        string profileId,
        GameType targetGame,
        IReadOnlyList<ContentManifest> toAdd,
        IReadOnlyList<string> toRemove,
        IReadOnlyList<UserDataManifest> currentManifests,
        CancellationToken cancellationToken)
    {
        var activateResult = await userDataTracker.ActivateProfileUserDataAsync(profileId, cancellationToken);
        if (!activateResult.Success)
        {
            logger.LogError("[ProfileContentLinker] Failed to activate user data for profile {ProfileId}: {Error}", profileId, activateResult.FirstError);
            var rollbackFailed = await RollbackSyncAsync(profileId, targetGame, toAdd, toRemove, currentManifests, shouldActivate: true);
            var errorMessage = rollbackFailed
                ? $"Failed to activate user data: {activateResult.FirstError ?? UnknownErrorMessage} (live rollback was incomplete)"
                : $"Failed to activate user data: {activateResult.FirstError ?? UnknownErrorMessage}";

            return OperationResult<bool>.CreateFailure(errorMessage);
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private async Task<OperationResult<bool>> PrepareProfileUserDataInternalAsync(
        string profileId,
        IEnumerable<ContentManifest> manifests,
        GameType targetGame,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("[ProfileContentLinker] Preparing user data for profile {ProfileId}", profileId);

            await DeactivateLingeringActiveProfilesAsync(profileId, targetGame, cancellationToken);

            var userDataManifests = manifests.Where(HasProfileUserData).ToList();
            if (userDataManifests.Count > 0)
            {
                var processResult = await ProcessManifestUserDataAsync(profileId, userDataManifests, targetGame, cancellationToken);
                if (!processResult.Success)
                {
                    return processResult;
                }

                var activateResult = await userDataTracker.ActivateProfileUserDataAsync(profileId, cancellationToken);
                if (!activateResult.Success)
                {
                    logger.LogError("[ProfileContentLinker] Failed to activate user data for profile {ProfileId}", profileId);
                    return OperationResult<bool>.CreateFailure(activateResult.FirstError ?? "Failed to activate user data");
                }
            }
            else
            {
                logger.LogDebug("[ProfileContentLinker] No user data manifests for profile {ProfileId}", profileId);
            }

            _activeProfileByGame[targetGame] = profileId;
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

    private async Task DeactivateLingeringActiveProfilesAsync(string profileId, GameType targetGame, CancellationToken cancellationToken)
    {
        var gameUserDataResult = await userDataTracker.GetGameUserDataAsync(targetGame, cancellationToken);
        if (gameUserDataResult.Success && gameUserDataResult.Data != null)
        {
            var otherActiveProfiles = gameUserDataResult.Data
                .Where(m => m.IsActive && !string.Equals(m.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.ProfileId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var otherProfileId in otherActiveProfiles)
            {
                logger.LogInformation("[ProfileContentLinker] Deactivating lingering user data from profile {OtherProfileId}", otherProfileId);
                var deactivateResult = await userDataTracker.DeactivateProfileUserDataAsync(otherProfileId, cancellationToken);
                if (!deactivateResult.Success)
                {
                    logger.LogWarning("[ProfileContentLinker] Failed to deactivate lingering user data for profile {OtherProfileId}: {Error}", otherProfileId, deactivateResult.FirstError);
                }
            }
        }
        else if (!gameUserDataResult.Success)
        {
            logger.LogWarning("[ProfileContentLinker] Failed to query existing user data for game {GameType} while checking lingering active profiles: {Error}", targetGame, gameUserDataResult.FirstError);
        }
    }

    private async Task<OperationResult<bool>> ProcessManifestUserDataAsync(
        string profileId,
        IReadOnlyList<ContentManifest> userDataManifests,
        GameType targetGame,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("[ProfileContentLinker] Processing {Count} manifests with user data", userDataManifests.Count);

        foreach (var manifest in userDataManifests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var processRes = await ProcessSingleManifestUserDataAsync(manifest, profileId, targetGame, cancellationToken);
            if (!processRes.Success)
            {
                return processRes;
            }
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private async Task<OperationResult<bool>> ProcessSingleManifestUserDataAsync(
        ContentManifest manifest,
        string profileId,
        GameType targetGame,
        CancellationToken cancellationToken)
    {
        var existingResult = await userDataTracker.GetUserDataManifestAsync(manifest.Id.Value, profileId, cancellationToken);
        if (existingResult.Success && existingResult.Data != null)
        {
            var verifyResult = await userDataTracker.VerifyInstallationAsync(manifest.Id.Value, profileId, cancellationToken);
            if (!verifyResult.Success || !verifyResult.Data)
            {
                return await ReinstallManifestUserDataAsync(manifest, profileId, targetGame, cancellationToken);
            }

            return OperationResult<bool>.CreateSuccess(true);
        }

        var installRes = await InstallManifestUserDataAsync(manifest, profileId, targetGame, cancellationToken);
        if (!installRes.Success)
        {
            logger.LogError("[ProfileContentLinker] Failed to install user data for manifest {ManifestId}: {Error}", manifest.Id.Value, installRes.FirstError);
            return OperationResult<bool>.CreateFailure(installRes.FirstError ?? $"Failed to install user data for manifest {manifest.Id.Value}");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private async Task<OperationResult<bool>> ReinstallManifestUserDataAsync(
        ContentManifest manifest,
        string profileId,
        GameType targetGame,
        CancellationToken cancellationToken)
    {
        logger.LogWarning("[ProfileContentLinker] User data verification failed for {ManifestId}, reinstalling", manifest.Id.Value);

        var uninstallResult = await userDataTracker.UninstallUserDataAsync(manifest.Id.Value, profileId, cancellationToken);
        if (!uninstallResult.Success)
        {
            logger.LogError(
                "[ProfileContentLinker] Cannot reinstall {ManifestId}: the previous installation could not be fully removed: {Error}",
                manifest.Id.Value,
                uninstallResult.FirstError);
            return OperationResult<bool>.CreateFailure(uninstallResult.Errors);
        }

        var installRes = await InstallManifestUserDataAsync(manifest, profileId, targetGame, cancellationToken);
        if (!installRes.Success)
        {
            logger.LogError("[ProfileContentLinker] Failed to reinstall user data for manifest {ManifestId}: {Error}", manifest.Id.Value, installRes.FirstError);
            return OperationResult<bool>.CreateFailure(installRes.Errors);
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    /// <summary>
    /// Rolls back partially applied installations and uninstalls during live synchronization failure.
    /// </summary>
    private async Task<bool> RollbackSyncAsync(
        string profileId,
        GameType targetGame,
        IReadOnlyList<ContentManifest> installedSoFar,
        IReadOnlyList<string> uninstalledSoFar,
        IReadOnlyList<UserDataManifest> originalManifests,
        bool shouldActivate)
    {
        bool rollbackFailed = false;

        if (await RollbackInstalledManifestsAsync(profileId, installedSoFar))
        {
            rollbackFailed = true;
        }

        if (await RollbackUninstalledManifestsAsync(profileId, targetGame, uninstalledSoFar, originalManifests))
        {
            rollbackFailed = true;
        }

        if (await RollbackActiveStateAsync(profileId, shouldActivate))
        {
            rollbackFailed = true;
        }

        if (rollbackFailed)
        {
            logger.LogError("[ProfileContentLinker] Rollback after failure completed with errors for profile {ProfileId}", profileId);
        }
        else
        {
            logger.LogInformation("[ProfileContentLinker] Successfully rolled back user data changes for profile {ProfileId}", profileId);
        }

        return rollbackFailed;
    }

    private async Task<bool> RollbackInstalledManifestsAsync(string profileId, IReadOnlyList<ContentManifest> installedSoFar)
    {
        bool failed = false;
        foreach (var manifestId in installedSoFar.Select(manifest => manifest.Id.Value))
        {
            var uninstallRes = await userDataTracker.UninstallUserDataAsync(manifestId, profileId, CancellationToken.None);
            if (!uninstallRes.Success)
            {
                failed = true;
                logger.LogWarning("[ProfileContentLinker] Rollback uninstall failed for manifest {ManifestId}: {Error}", manifestId, uninstallRes.FirstError);
            }
        }

        return failed;
    }

    private async Task<bool> RollbackUninstalledManifestsAsync(
        string profileId,
        GameType targetGame,
        IReadOnlyList<string> uninstalledSoFar,
        IReadOnlyList<UserDataManifest> originalManifests)
    {
        bool failed = false;
        foreach (var manifest in originalManifests.Where(m => uninstalledSoFar.Contains(m.ManifestId, StringComparer.OrdinalIgnoreCase)))
        {
            var installRes = await userDataTracker.InstallUserDataAsync(
                manifest.ManifestId,
                profileId,
                targetGame,
                manifest.InstalledFiles.Select(f => new ManifestFile
                {
                    RelativePath = f.RelativePath,
                    Hash = f.SourceHash ?? f.CasHash ?? string.Empty,
                    Size = f.FileSize,
                    InstallTarget = f.InstallTarget,
                }),
                manifest.ManifestVersion,
                manifest.ManifestName,
                CancellationToken.None);

            if (!installRes.Success)
            {
                failed = true;
                logger.LogWarning("[ProfileContentLinker] Rollback reinstall failed for manifest {ManifestId}: {Error}", manifest.ManifestId, installRes.FirstError);
            }
        }

        return failed;
    }

    private async Task<bool> RollbackActiveStateAsync(string profileId, bool shouldActivate)
    {
        if (shouldActivate)
        {
            var reactivateRes = await userDataTracker.ActivateProfileUserDataAsync(profileId, CancellationToken.None);
            if (reactivateRes != null && !reactivateRes.Success)
            {
                logger.LogWarning("[ProfileContentLinker] Rollback activation failed for profile {ProfileId}: {Error}", profileId, reactivateRes.FirstError);
                return true;
            }
        }
        else
        {
            var deactivateRes = await userDataTracker.DeactivateProfileUserDataAsync(profileId, CancellationToken.None);
            if (deactivateRes != null && !deactivateRes.Success)
            {
                logger.LogWarning("[ProfileContentLinker] Rollback deactivation failed for profile {ProfileId}: {Error}", profileId, deactivateRes.FirstError);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Installs user data files from a manifest for a specific profile.
    /// </summary>
    /// <returns>A task representing the asynchronous installation operation result.</returns>
    private async Task<OperationResult<UserDataManifest>> InstallManifestUserDataAsync(
        ContentManifest manifest,
        string profileId,
        GameType targetGame,
        CancellationToken cancellationToken)
    {
        var userDataFiles = GetUserDataFiles(manifest);

        if (userDataFiles.Count == 0)
        {
            return OperationResult<UserDataManifest>.CreateSuccess(new UserDataManifest
            {
                ManifestId = manifest.Id.Value,
                ProfileId = profileId,
                TargetGame = targetGame,
                ManifestVersion = manifest.Version,
                ManifestName = manifest.Name,
            });
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
