using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Extensions.Enums;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.UserData;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.UserData.Services;

/// <summary>
/// Service for tracking and managing user data files (maps, replays, etc.)
/// that are installed to the user's Documents folder.
/// Content bound for a user-writable destination is always copied out of CAS so that later writes
/// by the game or by GenHub cannot reach the canonical CAS object.
/// </summary>
public class UserDataTrackerService(
    IConfigurationProviderService configProvider,
    IFileOperationsService fileOperations,
    ILogger<UserDataTrackerService> logger,
    IGamePathProvider pathProvider) : IUserDataTracker
{
    private static readonly SemaphoreSlim IndexLock = new(1, 1);
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly string _userDataTrackingPath = Path.Combine(configProvider.GetApplicationDataPath(), DirectoryNames.UserData);
    private readonly string _manifestsPath = Path.Combine(configProvider.GetApplicationDataPath(), DirectoryNames.UserData, DirectoryNames.UserDataManifests);
    private readonly string _backupsPath = Path.Combine(configProvider.GetApplicationDataPath(), DirectoryNames.UserData, DirectoryNames.UserDataBackups);
    private readonly string _indexPath = Path.Combine(configProvider.GetApplicationDataPath(), DirectoryNames.UserData, FileTypes.UserDataIndexFileName);

    private UserDataIndex? _cachedIndex;

    /// <inheritdoc />
    public async Task<OperationResult<UserDataManifest>> InstallUserDataAsync(
        string manifestId,
        string profileId,
        GameType targetGame,
        IEnumerable<ManifestFile> files,
        string manifestVersion,
        string? manifestName = null,
        CancellationToken cancellationToken = default)
    {
        EnsureDirectoriesExist();

        logger.LogInformation(
            "[UserData] Installing user data for manifest {ManifestId}, profile {ProfileId}, game {Game}",
            manifestId,
            profileId,
            targetGame);

        await IndexLock.WaitAsync(cancellationToken);
        try
        {
            // Filter to only user data files
            var userDataFiles = files
                .Where(f => f.InstallTarget != ContentInstallTarget.Workspace &&
                           f.InstallTarget != ContentInstallTarget.System)
                .ToList();

            if (userDataFiles.Count == 0)
            {
                logger.LogDebug("[UserData] No user data files to install");
                return OperationResult<UserDataManifest>.CreateFailure("No user data files to install");
            }

            logger.LogInformation("[UserData] Processing {Count} user data files", userDataFiles.Count);

            var userDataManifest = new UserDataManifest
            {
                ManifestId = manifestId,
                ProfileId = profileId,
                TargetGame = targetGame,
                ManifestVersion = manifestVersion,
                ManifestName = manifestName,
                InstalledAt = DateTime.UtcNow,
                IsActive = true,
            };

            var userDataBasePath = GetUserDataBasePath(targetGame);
            var resolvedFiles = new List<(ManifestFile File, string TargetPath)>(userDataFiles.Count);
            foreach (var file in userDataFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var targetPath = ResolveUserDataTargetPath(file.InstallTarget, file.RelativePath, userDataBasePath);
                    resolvedFiles.Add((file, targetPath));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[UserData] Invalid file path in manifest: {Path}", file.RelativePath);
                    return OperationResult<UserDataManifest>.CreateFailure($"Invalid file path in manifest: {file.RelativePath}");
                }
            }

            long totalSize = 0;
            var existingManifest = await LoadUserDataManifestByKeyAsync(userDataManifest.InstallationKey, cancellationToken);
            var priorFiles = existingManifest?.InstalledFiles?.ToDictionary(
                f => f.AbsolutePath,
                f => f,
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

            foreach (var (file, targetPath) in resolvedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                logger.LogDebug("[UserData] Installing {RelativePath} to {TargetPath}", file.RelativePath, targetPath);

                UserDataFileEntry? priorEntry = null;
                priorFiles?.TryGetValue(targetPath, out priorEntry);

                var installResult = await InstallSingleUserDataFileAsync(file, targetPath, targetGame, userDataManifest.InstallationKey, priorEntry, cancellationToken);
                if (!installResult.Success || installResult.Data == null)
                {
                    var error = installResult.FirstError ?? $"Failed to install '{targetPath}'.";
                    if (!await CleanupFailedInstallAsync(userDataManifest, manifestId))
                    {
                        error += $" Some of your original files could not be put back and were kept at '{_backupsPath}'.";
                    }

                    return OperationResult<UserDataManifest>.CreateFailure(error);
                }

                var entry = installResult.Data;
                userDataManifest.InstalledFiles.Add(entry);
                totalSize += entry.FileSize;
            }

            userDataManifest.TotalSizeBytes = totalSize;

            try
            {
                // Save the manifest
                await SaveUserDataManifestAsync(userDataManifest, cancellationToken);

                // Update the index
                await UpdateIndexUnlockedAsync(userDataManifest, isAdd: true, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _ = await CleanupFailedInstallAsync(userDataManifest, manifestId);
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[UserData] Failed to persist manifest or update index for {ManifestId}; cleaning up installed files", manifestId);
                _ = await CleanupFailedInstallAsync(userDataManifest, manifestId);
                throw;
            }

            logger.LogInformation(
                "[UserData] Successfully installed {Count} files ({Size} bytes) for manifest {ManifestId}",
                userDataManifest.InstalledFiles.Count,
                totalSize,
                manifestId);

            return OperationResult<UserDataManifest>.CreateSuccess(userDataManifest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[UserData] Failed to install user data for manifest {ManifestId}", manifestId);
            return OperationResult<UserDataManifest>.CreateFailure($"Failed to install user data: {ex.Message}");
        }
        finally
        {
            IndexLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> UninstallUserDataAsync(
        string manifestId,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[UserData] Uninstalling user data for manifest {ManifestId}, profile {ProfileId}", manifestId, profileId);

        await IndexLock.WaitAsync(cancellationToken);
        try
        {
            var manifestResult = await GetUserDataManifestAsync(manifestId, profileId, cancellationToken);
            if (!manifestResult.Success || manifestResult.Data == null)
            {
                logger.LogWarning("[UserData] No user data manifest found for {ManifestId}/{ProfileId}", manifestId, profileId);
                return OperationResult<bool>.CreateSuccess(true); // Nothing to uninstall
            }

            var manifest = manifestResult.Data;

            // Keep the manifest and index entry when a pristine original could not be put back: they
            // are the only record of which backup belongs to which path, so discarding them would
            // strand the user's originals under machine-generated names with nothing referencing them.
            if (!await CleanupInstalledFilesAsync(manifest, cancellationToken))
            {
                logger.LogError(
                    "[UserData] Uninstall of {ManifestId} left one or more pristine backups unrestored; keeping its tracking data so the originals stay recoverable",
                    manifestId);
                return OperationResult<bool>.CreateFailure(
                    $"Uninstalled files for '{manifestId}' but could not restore every original. Your originals are still under '{_backupsPath}' and GenHub kept tracking them so the uninstall can be retried.");
            }

            // Remove the manifest file
            await DeleteUserDataManifestAsync(manifestId, profileId, cancellationToken);

            // Update the index
            await UpdateIndexUnlockedAsync(manifest, isAdd: false, cancellationToken);

            logger.LogInformation("[UserData] Successfully uninstalled user data for manifest {ManifestId}", manifestId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[UserData] Failed to uninstall user data for manifest {ManifestId}", manifestId);
            return OperationResult<bool>.CreateFailure($"Failed to uninstall user data: {ex.Message}");
        }
        finally
        {
            IndexLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> ActivateProfileUserDataAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("[UserData] Activating user data for profile {ProfileId}", profileId);

        try
        {
            var manifestsResult = await GetProfileUserDataAsync(profileId, cancellationToken);
            if (!manifestsResult.Success)
            {
                return OperationResult<bool>.CreateFailure(manifestsResult.FirstError ?? "Failed to get user data manifests");
            }

            if (manifestsResult.Data == null || manifestsResult.Data.Count == 0)
            {
                return OperationResult<bool>.CreateSuccess(true); // No user data to activate
            }

            foreach (var manifest in manifestsResult.Data)
            {
                if (manifest.IsActive)
                {
                    continue; // Already active
                }

                var activationResult = await ActivateSingleManifestAsync(manifest, profileId, cancellationToken);
                if (!activationResult.Success)
                {
                    return activationResult;
                }
            }

            logger.LogInformation("[UserData] Activated {Count} manifests for profile {ProfileId}", manifestsResult.Data.Count, profileId);

            await IndexLock.WaitAsync(cancellationToken);
            try
            {
                var index = await LoadIndexUnlockedAsync(cancellationToken);
                index.ActiveProfileId = profileId;
                await SaveIndexAsync(index, cancellationToken);
            }
            finally
            {
                IndexLock.Release();
            }

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[UserData] Failed to activate user data for profile {ProfileId}", profileId);
            return OperationResult<bool>.CreateFailure($"Failed to activate user data: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> DeactivateProfileUserDataAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("[UserData] Deactivating user data for profile {ProfileId}", profileId);

        try
        {
            var manifestsResult = await GetProfileUserDataAsync(profileId, cancellationToken);
            if (!manifestsResult.Success)
            {
                return OperationResult<bool>.CreateFailure(manifestsResult.FirstError ?? "Failed to get user data manifests");
            }

            if (manifestsResult.Data == null || manifestsResult.Data.Count == 0)
            {
                return OperationResult<bool>.CreateSuccess(true); // No user data to deactivate
            }

            var allSuccess = true;
            var deactivatedCount = 0;

            foreach (var manifest in manifestsResult.Data)
            {
                if (!manifest.IsActive)
                {
                    continue; // Already inactive
                }

                var manifestHasErrors = false;
                var userDataBasePath = GetUserDataBasePath(manifest.TargetGame);

                // Remove hard links and copied files but keep tracking
                foreach (var file in manifest.InstalledFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (File.Exists(file.AbsolutePath))
                    {
                        try
                        {
                            var isMatch = await fileOperations.VerifyFileHashAsync(file.AbsolutePath, file.SourceHash, cancellationToken);
                            cancellationToken.ThrowIfCancellationRequested();

                            if (isMatch)
                            {
                                File.Delete(file.AbsolutePath);
                                CleanupEmptyDirectories(Path.GetDirectoryName(file.AbsolutePath), userDataBasePath);
                            }
                            else
                            {
                                logger.LogWarning("[UserData] File hash mismatch, user may have modified: {Path}; preserving file", file.AbsolutePath);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            manifestHasErrors = true;
                            allSuccess = false;
                            throw;
                        }
                        catch (Exception ex)
                        {
                            manifestHasErrors = true;
                            allSuccess = false;
                            logger.LogError(ex, "[UserData] Failed to remove file during deactivation: {Path}", file.AbsolutePath);
                        }
                    }

                    // If an original user file was backed up and the target file was removed or absent, restore it upon deactivation
                    if (!File.Exists(file.AbsolutePath) && !string.IsNullOrEmpty(file.BackupPath) && File.Exists(file.BackupPath))
                    {
                        try
                        {
                            var targetDir = Path.GetDirectoryName(file.AbsolutePath);
                            if (!string.IsNullOrEmpty(targetDir))
                            {
                                Directory.CreateDirectory(targetDir);
                            }

                            var restoredFrom = file.BackupPath;
                            RestoreAndConsumeBackup(file, logger);
                            logger.LogInformation("[UserData] Restored backup during deactivation: {Backup} -> {Path}", restoredFrom, file.AbsolutePath);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "[UserData] Failed to restore backup during deactivation: {Path}", file.AbsolutePath);
                            manifestHasErrors = true;
                            allSuccess = false;
                        }
                    }
                }

                // Update manifest state only after all files in this manifest are processed without errors
                if (!manifestHasErrors)
                {
                    manifest.IsActive = false;
                    await SaveUserDataManifestAsync(manifest, CancellationToken.None);
                    deactivatedCount++;
                }
                else
                {
                    logger.LogWarning("[UserData] Deactivation had errors for manifest {ManifestId}; keeping IsActive unchanged for retry", manifest.ManifestId);
                }
            }

            if (!allSuccess)
            {
                return OperationResult<bool>.CreateFailure("One or more files failed during deactivation; active state preserved for retry");
            }

            logger.LogInformation("[UserData] Deactivated {Count} manifests for profile {ProfileId}", deactivatedCount, profileId);

            await IndexLock.WaitAsync(cancellationToken);
            try
            {
                var index = await LoadIndexUnlockedAsync(cancellationToken);
                if (string.Equals(index.ActiveProfileId, profileId, StringComparison.OrdinalIgnoreCase))
                {
                    index.ActiveProfileId = null;
                    await SaveIndexAsync(index, cancellationToken);
                }
            }
            finally
            {
                IndexLock.Release();
            }

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[UserData] Failed to deactivate user data for profile {ProfileId}", profileId);
            return OperationResult<bool>.CreateFailure($"Failed to deactivate user data: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<string?>> GetActiveProfileIdAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var index = await LoadIndexAsync(cancellationToken);
            return OperationResult<string?>.CreateSuccess(index.ActiveProfileId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[UserData] Failed to get active profile ID");
            return OperationResult<string?>.CreateFailure($"Failed to get active profile ID: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<UserDataManifest>>> GetProfileUserDataAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var index = await LoadIndexAsync(cancellationToken);
            if (!index.ProfileInstallations.TryGetValue(profileId, out var installationKeys))
            {
                return OperationResult<IReadOnlyList<UserDataManifest>>.CreateSuccess([]);
            }

            var manifests = new List<UserDataManifest>();
            foreach (var key in installationKeys)
            {
                var manifest = await LoadUserDataManifestByKeyAsync(key, cancellationToken);
                if (manifest != null)
                {
                    manifests.Add(manifest);
                }
            }

            return OperationResult<IReadOnlyList<UserDataManifest>>.CreateSuccess(manifests);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[UserData] Failed to get profile user data for {ProfileId}", profileId);
            return OperationResult<IReadOnlyList<UserDataManifest>>.CreateFailure($"Failed to get profile user data: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<UserDataManifest>>> GetGameUserDataAsync(
        GameType targetGame,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureDirectoriesExist();

            var manifests = new List<UserDataManifest>();
            var manifestFiles = Directory.GetFiles(_manifestsPath, "*" + FileTypes.UserDataManifestExtension, SearchOption.TopDirectoryOnly);

            foreach (var file in manifestFiles)
            {
                var manifest = await LoadUserDataManifestFromFileAsync(file, cancellationToken);
                if (manifest?.TargetGame == targetGame)
                {
                    manifests.Add(manifest);
                }
            }

            return OperationResult<IReadOnlyList<UserDataManifest>>.CreateSuccess(manifests);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[UserData] Failed to get game user data for {Game}", targetGame);
            return OperationResult<IReadOnlyList<UserDataManifest>>.CreateFailure($"Failed to get game user data: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<UserDataManifest?>> GetUserDataManifestAsync(
        string manifestId,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var key = $"{manifestId}_{profileId}";
            var manifest = await LoadUserDataManifestByKeyAsync(key, cancellationToken);
            return OperationResult<UserDataManifest?>.CreateSuccess(manifest);
        }
        catch (OperationCanceledException)
        {
            // A cancelled read must not reach the uninstall path as "no manifest, nothing to do".
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[UserData] Failed to get user data manifest {ManifestId}/{ProfileId}", manifestId, profileId);
            return OperationResult<UserDataManifest?>.CreateFailure($"Failed to get user data manifest: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> VerifyInstallationAsync(
        string manifestId,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var manifestResult = await GetUserDataManifestAsync(manifestId, profileId, cancellationToken);
            if (!manifestResult.Success || manifestResult.Data == null)
            {
                return OperationResult<bool>.CreateFailure("User data manifest not found");
            }

            var manifest = manifestResult.Data;
            var allValid = true;

            foreach (var file in manifest.InstalledFiles)
            {
                if (!File.Exists(file.AbsolutePath))
                {
                    logger.LogWarning("[UserData] File missing: {Path}", file.AbsolutePath);
                    allValid = false;
                    continue;
                }

                if (!file.IsHardLink && !await fileOperations.VerifyFileHashAsync(file.AbsolutePath, file.SourceHash, cancellationToken))
                {
                    logger.LogWarning("[UserData] File hash mismatch: {Path}", file.AbsolutePath);
                    allValid = false;
                }
            }

            manifest.LastVerifiedAt = DateTime.UtcNow;
            await SaveUserDataManifestAsync(manifest, cancellationToken);

            return OperationResult<bool>.CreateSuccess(allValid);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[UserData] Failed to verify installation {ManifestId}/{ProfileId}", manifestId, profileId);
            return OperationResult<bool>.CreateFailure($"Failed to verify installation: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<string?>> CheckFileConflictAsync(
        string absolutePath,
        CancellationToken cancellationToken = default)
    {
        await IndexLock.WaitAsync(cancellationToken);
        try
        {
            return await CheckFileConflictUnlockedAsync(absolutePath, cancellationToken);
        }
        finally
        {
            IndexLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> CleanupProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[UserData] Cleaning up all user data for profile {ProfileId}", profileId);

        try
        {
            var manifestsResult = await GetProfileUserDataAsync(profileId, cancellationToken);
            if (!manifestsResult.Success || manifestsResult.Data == null)
            {
                return OperationResult<bool>.CreateSuccess(true);
            }

            var uninstallErrors = new List<string>();
            foreach (var manifest in manifestsResult.Data)
            {
                var uninstallResult = await UninstallUserDataAsync(manifest.ManifestId, profileId, cancellationToken);
                if (!uninstallResult.Success)
                {
                    uninstallErrors.AddRange(uninstallResult.Errors);
                }
            }

            // A discarded uninstall failure is a silent data-safety failure: the user's pristine
            // originals are still under the backups tree and nothing above would ever say so.
            if (uninstallErrors.Count > 0)
            {
                logger.LogError(
                    "[UserData] Cleanup of profile {ProfileId} left {Count} uninstall(s) unfinished; their originals are still tracked under {BackupsPath}",
                    profileId,
                    uninstallErrors.Count,
                    _backupsPath);
                return OperationResult<bool>.CreateFailure(uninstallErrors);
            }

            logger.LogInformation("[UserData] Cleaned up {Count} manifests for profile {ProfileId}", manifestsResult.Data.Count, profileId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[UserData] Failed to cleanup profile {ProfileId}", profileId);
            return OperationResult<bool>.CreateFailure($"Failed to cleanup profile: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<long>> GetTotalUserDataSizeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var index = await LoadIndexAsync(cancellationToken);
            long totalSize = 0;

            foreach (var key in index.InstallationKeys)
            {
                var manifest = await LoadUserDataManifestByKeyAsync(key, cancellationToken);
                if (manifest != null)
                {
                    totalSize += manifest.TotalSizeBytes;
                }
            }

            return OperationResult<long>.CreateSuccess(totalSize);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[UserData] Failed to get total user data size");
            return OperationResult<long>.CreateFailure($"Failed to get total user data size: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> DeleteAllUserDataAsync(CancellationToken cancellationToken = default)
    {
        logger.LogWarning("[UserData] DELETE ALL USER DATA REQUESTED");

        try
        {
            // Acquire lock to prevent other operations
            await IndexLock.WaitAsync(cancellationToken);
            try
            {
                // 1. Delete all tracked files from the file system
                // We load the index to find what we need to delete
                var index = await LoadIndexUnlockedAsync(cancellationToken);

                // Uninstall all installations (this handles backup restoration and file deletion)
                var allBackupsRestored = true;
                foreach (var profileId in index.ProfileInstallations.Keys.ToList())
                {
                    // Get keys for this profile
                    if (index.ProfileInstallations.TryGetValue(profileId, out var keys))
                    {
                        foreach (var key in keys)
                        {
                            try
                            {
                                // We are already holding the lock, so we can't call UninstallUserDataAsync which tries to acquire it.
                                // Instead, we directly clean up the files.
                                var manifest = await LoadUserDataManifestByKeyAsync(key, cancellationToken);
                                if (manifest == null)
                                {
                                    // A key whose manifest file is simply gone is a stale index entry
                                    // with nothing left to restore, and must not block the cleanup
                                    // forever. Only a manifest that exists but cannot be read leaves
                                    // backups we can no longer put back.
                                    if (File.Exists(GetManifestFilePath(key)))
                                    {
                                        logger.LogError("[UserData] Manifest for installation key {Key} could not be read; its backups cannot be restored", key);
                                        allBackupsRestored = false;
                                    }
                                    else
                                    {
                                        logger.LogWarning("[UserData] Index entry {Key} has no manifest; nothing to restore for it", key);
                                    }

                                    continue;
                                }

                                if (!await CleanupInstalledFilesAsync(manifest, cancellationToken))
                                {
                                    allBackupsRestored = false;
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                // Abort before step 3 removes the manifests and the index: those are
                                // the only map from a backup file back to the path it belongs at.
                                throw;
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "[UserData] Failed to cleanup user data for installation key {Key}", key);
                                allBackupsRestored = false;
                            }
                        }
                    }
                }

                // 2. Nuke the directories to be sure
                if (Directory.Exists(_userDataTrackingPath))
                {
                    // Sanity check: ensure we're not deleting a system root or unrelated directory
                    if (!Path.GetFullPath(_userDataTrackingPath).Contains(AppConstants.AppName, StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogError("[UserData] Refusing to delete UserData directory that doesn't appear application-specific: {Path}", _userDataTrackingPath);
                        return OperationResult<bool>.CreateFailure("UserData tracking path does not appear to be application-specific");
                    }

                    if (allBackupsRestored)
                    {
                        logger.LogInformation("[UserData] Deleting UserData directory: {Path}", _userDataTrackingPath);
                        Directory.Delete(_userDataTrackingPath, true);
                        _cachedIndex = new UserDataIndex();
                    }
                    else
                    {
                        // Keep the manifests and the index alongside the retained backups: they are
                        // the only map from a machine-named backup file back to the path it belongs
                        // at, and a later delete-all clears whatever is left once the restores work.
                        logger.LogWarning(
                            "[UserData] One or more pristine game data backups could not be restored, so they were NOT deleted. Your originals remain at {BackupsPath} and GenHub kept tracking them; retry the deletion or restore them by hand.",
                            _backupsPath);
                    }
                }

                // 3. Re-create empty directories
                EnsureDirectoriesExist();

                if (!allBackupsRestored)
                {
                    return OperationResult<bool>.CreateFailure(
                        $"Removed what could be removed, but one or more pristine game data backups could not be restored. Your originals were kept at '{_backupsPath}' along with the tracking data that records where each one belongs, so the deletion can be retried.");
                }

                return OperationResult<bool>.CreateSuccess(true);
            }
            finally
            {
                IndexLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[UserData] Failed to delete all user data");
            return OperationResult<bool>.CreateFailure($"Failed to delete all user data: {ex.Message}");
        }
    }

    private static string ResolveUserDataTargetPath(ContentInstallTarget installTarget, string relativePath, string userDataBasePath)
    {
        var normalizedRelativePath = relativePath.Replace('\\', '/');
        var targetPath = installTarget switch
        {
            ContentInstallTarget.UserDataDirectory => Path.Combine(userDataBasePath, normalizedRelativePath),
            ContentInstallTarget.UserMapsDirectory => Path.Combine(userDataBasePath, GameSettingsConstants.FolderNames.Maps, StripLeadingDirectory(normalizedRelativePath, "Maps")),
            ContentInstallTarget.UserReplaysDirectory => Path.Combine(userDataBasePath, GameSettingsConstants.FolderNames.Replays, StripLeadingDirectory(normalizedRelativePath, "Replays")),
            ContentInstallTarget.UserScreenshotsDirectory => Path.Combine(userDataBasePath, GameSettingsConstants.FolderNames.Screenshots, StripLeadingDirectory(normalizedRelativePath, "Screenshots")),
            _ => Path.Combine(userDataBasePath, normalizedRelativePath),
        };

        var fullPath = Path.GetFullPath(targetPath);
        var basePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(userDataBasePath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!fullPath.StartsWith(basePath + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException($"Relative path escapes the user data directory: {relativePath}");
        }

        return fullPath;
    }

    /// <summary>
    /// Strips a leading directory name from a relative path if present.
    /// </summary>
    /// <param name="path">The path to process.</param>
    /// <param name="directoryName">The directory name to strip (without slashes).</param>
    /// <returns>The path with the leading directory removed, or the original path if not present.</returns>
    private static string StripLeadingDirectory(string path, string directoryName)
    {
        // Handle both forward and back slashes
        var normalized = path.Replace('\\', '/');
        var prefix = directoryName + "/";

        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return normalized[prefix.Length..];
        }

        return path;
    }

    /// <summary>
    /// Moves a deployed file that no longer matches its recorded hash to a clearly named sibling so
    /// the user's edit is never discarded when the pristine backup is restored over the original path.
    /// </summary>
    /// <param name="filePath">The deployed file to move aside.</param>
    /// <returns>The path the modified file was moved to.</returns>
    private static string MoveModifiedFileAside(string filePath)
    {
        var preservedPath = filePath + UserDataConstants.UserModifiedSuffix;
        var attempt = 1;
        while (File.Exists(preservedPath) || Directory.Exists(preservedPath))
        {
            preservedPath = $"{filePath}{UserDataConstants.UserModifiedSuffix}.{attempt}";
            attempt++;
        }

        File.Move(filePath, preservedPath);
        return preservedPath;
    }

    /// <summary>
    /// Copies a backup back over a deployed path, unlinking the destination first. An older install
    /// may have left a hard link to a CAS object there, and copying onto it in place would write the
    /// backup's content into the canonical object rather than replacing the deployed file.
    /// </summary>
    /// <param name="backupPath">The backup to restore from.</param>
    /// <param name="targetPath">The path to restore to.</param>
    private static void RestoreBackupCopy(string backupPath, string targetPath)
    {
        FileOperationsService.DeleteFileIfExists(targetPath);
        File.Copy(backupPath, targetPath, overwrite: true);
    }

    /// <summary>
    /// Restores a backup over the deployed path and consumes it. The protected content is back where
    /// it belongs, so leaving the backup file and its recorded path behind would make the next
    /// uninstall read the restored original as a user modification, move it aside and put an
    /// identical duplicate in its place.
    /// </summary>
    /// <param name="file">The entry whose backup should be restored and then cleared.</param>
    /// <param name="logger">The logger used to record a backup file that could not be deleted.</param>
    private static void RestoreAndConsumeBackup(UserDataFileEntry file, ILogger logger)
    {
        if (file.BackupPath is not null)
        {
            var backupPath = file.BackupPath;
            RestoreBackupCopy(backupPath, file.AbsolutePath);
            file.BackupPath = null;
            file.WasOverwritten = false;

            DeleteConsumedBackup(backupPath, logger);
        }
    }

    /// <summary>
    /// Deletes a backup whose content has already been put back at the path it belongs to. The
    /// restore is what protects the user's data, so a delete that fails - an antivirus scanner or an
    /// indexer holding the file open for a moment - must not turn the restore into a failure: the
    /// retry would read the restored original as a modification and duplicate it.
    /// </summary>
    /// <param name="backupPath">The backup file to remove.</param>
    /// <param name="logger">The logger used to record a backup file that could not be deleted.</param>
    private static void DeleteConsumedBackup(string backupPath, ILogger logger)
    {
        try
        {
            File.Delete(backupPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                ex,
                "[UserData] Restored backup {BackupPath} but could not delete it; it is now a stray copy and can be removed by hand",
                backupPath);
        }
    }

    private static void RestoreBackupQuietly(string? backupPath, string targetPath, bool wasOverwritten, ILogger logger)
    {
        if (wasOverwritten && !string.IsNullOrEmpty(backupPath) && File.Exists(backupPath))
        {
            try
            {
                RestoreBackupCopy(backupPath, targetPath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[UserData] Failed to restore safety backup from {BackupPath} to {TargetPath}", backupPath, targetPath);
                return;
            }

            DeleteConsumedBackup(backupPath, logger);
        }
    }

    private static void CleanupSupersededBackups(IReadOnlyList<string> supersededBackups, ILogger logger)
    {
        foreach (var oldBackup in supersededBackups)
        {
            try
            {
                if (File.Exists(oldBackup))
                {
                    File.Delete(oldBackup);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[UserData] Failed to delete superseded backup file {OldBackup}", oldBackup);
            }
        }
    }

    private static void CleanupEmptyDirectories(string? directoryPath, string? stopAtDirectory = null)
    {
        if (string.IsNullOrEmpty(directoryPath) || string.IsNullOrEmpty(stopAtDirectory))
        {
            return;
        }

        try
        {
            var normalizedStop = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stopAtDirectory));
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            while (Directory.Exists(directoryPath))
            {
                var fullDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
                if (string.Equals(fullDir, normalizedStop, comparison) ||
                    !fullDir.StartsWith(normalizedStop + Path.DirectorySeparatorChar, comparison))
                {
                    break;
                }

                if (Directory.EnumerateFileSystemEntries(directoryPath).Any())
                {
                    break;
                }

                Directory.Delete(directoryPath);
                directoryPath = Path.GetDirectoryName(directoryPath);

                if (string.IsNullOrEmpty(directoryPath))
                {
                    break;
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private async Task<OperationResult<bool>> ActivateSingleManifestAsync(
        UserDataManifest manifest,
        string profileId,
        CancellationToken cancellationToken)
    {
        var filesActivatedInThisManifest = new List<UserDataFileEntry>();
        var supersededBackups = new List<string>();

        try
        {
            foreach (var file in manifest.InstalledFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileResult = await ActivateSingleFileAsync(file, manifest, filesActivatedInThisManifest, supersededBackups, cancellationToken);
                if (!fileResult.Success)
                {
                    var userDataBasePath = GetUserDataBasePath(manifest.TargetGame);
                    RollbackActivatedFiles(filesActivatedInThisManifest, userDataBasePath);

                    manifest.IsActive = false;
                    try
                    {
                        await SaveUserDataManifestAsync(manifest, CancellationToken.None);
                    }
                    catch (Exception saveEx)
                    {
                        logger.LogError(saveEx, "[UserData] Failed to persist rolled-back manifest state for {ManifestId}", manifest.ManifestId);
                    }

                    CleanupSupersededBackups(supersededBackups, logger);

                    return fileResult;
                }
            }

            manifest.IsActive = true;
            await SaveUserDataManifestAsync(manifest, cancellationToken);
            CleanupSupersededBackups(supersededBackups, logger);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException)
        {
            var userDataBasePath = GetUserDataBasePath(manifest.TargetGame);
            RollbackActivatedFiles(filesActivatedInThisManifest, userDataBasePath);

            manifest.IsActive = false;
            try
            {
                await SaveUserDataManifestAsync(manifest, CancellationToken.None);
            }
            catch (Exception saveEx)
            {
                logger.LogError(saveEx, "[UserData] Failed to persist cancelled manifest state for {ManifestId}", manifest.ManifestId);
            }

            CleanupSupersededBackups(supersededBackups, logger);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[UserData] Failed during activation of manifest {ManifestId} for profile {ProfileId}; rolling back", manifest.ManifestId, profileId);
            var userDataBasePath = GetUserDataBasePath(manifest.TargetGame);
            RollbackActivatedFiles(filesActivatedInThisManifest, userDataBasePath);

            manifest.IsActive = false;
            try
            {
                await SaveUserDataManifestAsync(manifest, CancellationToken.None);
            }
            catch (Exception saveEx)
            {
                logger.LogError(saveEx, "[UserData] Failed to persist rolled-back manifest state for {ManifestId}", manifest.ManifestId);
            }

            CleanupSupersededBackups(supersededBackups, logger);
            throw;
        }
    }

    private async Task<OperationResult<bool>> ActivateSingleFileAsync(
        UserDataFileEntry file,
        UserDataManifest manifest,
        List<UserDataFileEntry> filesActivatedInThisManifest,
        List<string> supersededBackups,
        CancellationToken cancellationToken)
    {
        if (File.Exists(file.AbsolutePath))
        {
            if (await fileOperations.VerifyFileHashAsync(file.AbsolutePath, file.SourceHash, cancellationToken))
            {
                return OperationResult<bool>.CreateSuccess(true);
            }

            var oldBackup = file.BackupPath;
            var backupPath = await BackupExistingFileAsync(file.AbsolutePath, manifest.TargetGame, cancellationToken);
            if (string.IsNullOrEmpty(backupPath))
            {
                logger.LogError("[UserData] Failed to create safety backup for {Path} during activation", file.AbsolutePath);
                return OperationResult<bool>.CreateFailure($"Failed to create safety backup for '{file.AbsolutePath}' during activation");
            }

            var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.IsNullOrEmpty(oldBackup) && !string.Equals(oldBackup, backupPath, pathComparison))
            {
                supersededBackups.Add(oldBackup);
            }

            file.BackupPath = backupPath;
            file.WasOverwritten = true;

            FileOperationsService.DeleteFileIfExists(file.AbsolutePath);
        }

        filesActivatedInThisManifest.Add(file);

        if (string.IsNullOrEmpty(file.CasHash))
        {
            logger.LogError("[UserData] File {Path} has no CAS hash; cannot activate", file.AbsolutePath);
            return OperationResult<bool>.CreateFailure($"File '{file.AbsolutePath}' has no CAS hash");
        }

        var targetDir = Path.GetDirectoryName(file.AbsolutePath);
        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        var fileMaterialized = false;
        try
        {
            var (materialized, isHardLink) = await MaterializeFromCasAsync(
                file.CasHash,
                file.AbsolutePath,
                file.InstallTarget,
                cancellationToken);

            fileMaterialized = materialized;
            if (materialized)
            {
                file.IsHardLink = isHardLink;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[UserData] Exception while materializing file {Path} during activation", file.AbsolutePath);
        }

        if (!fileMaterialized)
        {
            logger.LogError("[UserData] Failed to materialize file {Path} during activation", file.AbsolutePath);
            return OperationResult<bool>.CreateFailure($"Failed to materialize file '{file.AbsolutePath}' during activation");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private async Task<OperationResult<UserDataFileEntry>> InstallSingleUserDataFileAsync(
        ManifestFile file,
        string targetPath,
        GameType targetGame,
        string installationKey,
        UserDataFileEntry? priorEntry,
        CancellationToken cancellationToken)
    {
        var conflictResult = await CheckFileConflictUnlockedAsync(targetPath, cancellationToken);
        if (!conflictResult.Success)
        {
            logger.LogError("[UserData] Failed to check file conflict for {Path}: {Error}; aborting installation", targetPath, conflictResult.FirstError);
            return OperationResult<UserDataFileEntry>.CreateFailure($"Failed to check file conflict for '{targetPath}': {conflictResult.FirstError}");
        }

        var isSameManifest = false;
        if (!string.IsNullOrEmpty(conflictResult.Data) && conflictResult.Data != installationKey)
        {
            var conflictingManifest = await LoadUserDataManifestByKeyAsync(conflictResult.Data, cancellationToken);
            var lastUnderscoreIndex = installationKey.LastIndexOf('_');
            var currentManifestId = lastUnderscoreIndex > 0 ? installationKey[..lastUnderscoreIndex] : installationKey;

            if (conflictingManifest != null &&
                string.Equals(conflictingManifest.ManifestId, currentManifestId, StringComparison.OrdinalIgnoreCase))
            {
                isSameManifest = true;
                logger.LogDebug(
                    "[UserData] File {Path} is already managed by manifest {ManifestId} under installation {Key}; sharing installation without conflict",
                    targetPath,
                    currentManifestId,
                    conflictResult.Data);

                if (priorEntry == null)
                {
                    var existingFile = conflictingManifest.InstalledFiles.FirstOrDefault(
                        f => string.Equals(f.AbsolutePath, targetPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
                    if (existingFile != null)
                    {
                        priorEntry = existingFile;
                    }
                }
            }
            else
            {
                logger.LogError("[UserData] File conflict with installation {Key}: {Path}; aborting installation", conflictResult.Data, targetPath);
                return OperationResult<UserDataFileEntry>.CreateFailure($"File '{targetPath}' is already managed by installation '{conflictResult.Data}'. Installation aborted.");
            }
        }

        var wasOverwritten = false;
        string? backupPath = null;

        if (File.Exists(targetPath))
        {
            if (string.IsNullOrEmpty(conflictResult.Data))
            {
                backupPath = await BackupExistingFileAsync(targetPath, targetGame, cancellationToken);
                if (string.IsNullOrEmpty(backupPath))
                {
                    logger.LogError("[UserData] Failed to create safety backup for user file {Path}; aborting installation to prevent data loss", targetPath);
                    return OperationResult<UserDataFileEntry>.CreateFailure($"Failed to create safety backup for '{targetPath}'. Installation aborted.");
                }

                wasOverwritten = true;
                logger.LogInformation("[UserData] Backed up existing user file: {Path} -> {Backup}", targetPath, backupPath);
            }
            else if ((conflictResult.Data == installationKey || isSameManifest) && priorEntry != null)
            {
                wasOverwritten = priorEntry.WasOverwritten;
                backupPath = priorEntry.BackupPath;
            }

            FileOperationsService.DeleteFileIfExists(targetPath);
        }
        else if ((conflictResult.Data == installationKey || isSameManifest) && priorEntry != null)
        {
            wasOverwritten = priorEntry.WasOverwritten;
            backupPath = priorEntry.BackupPath;
        }

        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        if (string.IsNullOrEmpty(file.Hash))
        {
            logger.LogError("[UserData] File {Path} has no hash; aborting installation", file.RelativePath);
            RestoreBackupQuietly(backupPath, targetPath, wasOverwritten, logger);
            return OperationResult<UserDataFileEntry>.CreateFailure($"File '{file.RelativePath}' has no hash. Installation aborted.");
        }

        var (materialized, isHardLink) = await MaterializeFileFromCasAsync(file.Hash, targetPath, file.InstallTarget, backupPath, wasOverwritten, cancellationToken);
        if (!materialized)
        {
            logger.LogError("[UserData] Failed to install file {Path}; aborting installation", targetPath);
            RestoreBackupQuietly(backupPath, targetPath, wasOverwritten, logger);
            return OperationResult<UserDataFileEntry>.CreateFailure($"Failed to install file '{targetPath}'. Installation aborted.");
        }

        return OperationResult<UserDataFileEntry>.CreateSuccess(new UserDataFileEntry
        {
            RelativePath = file.RelativePath,
            AbsolutePath = targetPath,
            SourceHash = file.Hash,
            FileSize = file.Size,
            InstallTarget = file.InstallTarget,
            WasOverwritten = wasOverwritten,
            BackupPath = backupPath,
            InstalledAt = DateTime.UtcNow,
            IsHardLink = isHardLink,
            CasHash = file.Hash,
        });
    }

    private async Task<(bool Materialized, bool IsHardLink)> MaterializeFileFromCasAsync(
        string hash,
        string targetPath,
        ContentInstallTarget installTarget,
        string? backupPath,
        bool wasOverwritten,
        CancellationToken cancellationToken)
    {
        try
        {
            return await MaterializeFromCasAsync(hash, targetPath, installTarget, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            RestoreBackupQuietly(backupPath, targetPath, wasOverwritten, logger);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[UserData] Exception while materializing file {Path} from CAS", targetPath);
        }

        return (false, false);
    }

    /// <summary>
    /// Materializes CAS content at the destination. User-writable destinations always receive an
    /// independent copy: a hard link would share the underlying storage with the CAS object, so any
    /// in-place write by the game or by GenHub would rewrite the canonical object and break the
    /// hash-to-content invariant for every profile referencing it.
    /// </summary>
    /// <param name="hash">The CAS hash of the content to materialize.</param>
    /// <param name="targetPath">The destination file path.</param>
    /// <param name="installTarget">The install target the destination was resolved from.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Whether the file was materialized and whether it is a hard link.</returns>
    private async Task<(bool Materialized, bool IsHardLink)> MaterializeFromCasAsync(
        string hash,
        string targetPath,
        ContentInstallTarget installTarget,
        CancellationToken cancellationToken)
    {
        if (installTarget.IsUserWritableTarget())
        {
            var userCopyResult = await fileOperations.CopyFromCasAsync(hash, targetPath, contentType: null, cancellationToken: cancellationToken);
            if (userCopyResult)
            {
                logger.LogDebug("[UserData] Copied file for {Path} (user-writable destination)", targetPath);
                return (true, false);
            }

            return (false, false);
        }

        var linkResult = await fileOperations.LinkFromCasAsync(
            hash,
            targetPath,
            useHardLink: true,
            contentType: null,
            cancellationToken: cancellationToken);

        if (linkResult)
        {
            logger.LogDebug("[UserData] Created hard link for {Path}", targetPath);
            return (true, true);
        }

        var copyResult = await fileOperations.CopyFromCasAsync(hash, targetPath, contentType: null, cancellationToken: cancellationToken);
        if (copyResult)
        {
            logger.LogDebug("[UserData] Copied file for {Path} (hard link failed)", targetPath);
            return (true, false);
        }

        return (false, false);
    }

    private void RollbackActivatedFiles(IReadOnlyList<UserDataFileEntry> filesActivated, string userDataBasePath)
    {
        foreach (var file in filesActivated)
        {
            try
            {
                if (!string.IsNullOrEmpty(file.BackupPath) && File.Exists(file.BackupPath))
                {
                    var targetDir = Path.GetDirectoryName(file.AbsolutePath);
                    if (!string.IsNullOrEmpty(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    RestoreAndConsumeBackup(file, logger);
                }
                else
                {
                    if (!string.IsNullOrEmpty(file.BackupPath))
                    {
                        logger.LogWarning("[UserData] Backup file not found during rollback for {Path}: {BackupPath}", file.AbsolutePath, file.BackupPath);
                    }

                    if (File.Exists(file.AbsolutePath))
                    {
                        File.Delete(file.AbsolutePath);
                    }

                    CleanupEmptyDirectories(Path.GetDirectoryName(file.AbsolutePath), userDataBasePath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[UserData] Error rolling back activation for {Path}", file.AbsolutePath);
            }
        }
    }

    private string GetUserDataBasePath(GameType gameType) => pathProvider.GetOptionsDirectory(gameType);

    /// <summary>
    /// Rolls a failed installation back. The manifest has not been persisted at this point, so a
    /// backup that cannot be put back is referenced by nothing at all; say so loudly rather than
    /// leaving the user to identify a machine-named file in the backups tree.
    /// </summary>
    /// <param name="manifest">The partially installed manifest to roll back.</param>
    /// <param name="manifestId">The manifest identifier, for logging.</param>
    /// <returns><c>true</c> when every backup was restored; otherwise, <c>false</c>.</returns>
    private async Task<bool> CleanupFailedInstallAsync(UserDataManifest manifest, string manifestId)
    {
        if (await CleanupInstalledFilesAsync(manifest, CancellationToken.None))
        {
            return true;
        }

        logger.LogError(
            "[UserData] Rolling back the failed install of {ManifestId} left one or more originals unrestored; they are kept at {BackupsPath} but no manifest records where they belong",
            manifestId,
            _backupsPath);
        return false;
    }

    /// <summary>
    /// Removes the deployed files for a manifest and restores the pristine originals GenHub backed up.
    /// A deployed file confirmed to differ from its recorded hash is moved aside instead of being
    /// discarded, so the user's edit survives and the backup can still be restored over the original
    /// path. A file whose hash could not be computed is left alone: an unreadable or briefly locked
    /// file is not evidence that the user changed it.
    /// </summary>
    /// <param name="manifest">The manifest whose installed files should be removed.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><c>true</c> when every file was cleanly uninstalled and every backup for the manifest was restored; otherwise, <c>false</c>.</returns>
    private async Task<bool> CleanupInstalledFilesAsync(UserDataManifest manifest, CancellationToken cancellationToken)
    {
        var userDataBasePath = GetUserDataBasePath(manifest.TargetGame);
        var allCleanedUp = true;
        var allBackupsRestored = true;
        var index = await LoadIndexUnlockedAsync(cancellationToken);

        foreach (var file in manifest.InstalledFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (index.FileToInstallationMap.TryGetValue(file.AbsolutePath, out var currentOwnerKey) &&
                currentOwnerKey != manifest.InstallationKey)
            {
                logger.LogDebug(
                    "[UserData] Skipping cleanup of {Path} for installation {Key}; currently owned by {OwnerKey}",
                    file.AbsolutePath,
                    manifest.InstallationKey,
                    currentOwnerKey);
                continue;
            }

            var hasBackup = !string.IsNullOrEmpty(file.BackupPath) && File.Exists(file.BackupPath);
            var backupRestored = false;
            var fileProcessed = true;

            try
            {
                var restoreNeeded = hasBackup;

                if (File.Exists(file.AbsolutePath))
                {
                    switch (await fileOperations.CheckFileHashAsync(file.AbsolutePath, file.SourceHash, cancellationToken))
                    {
                        case FileHashVerification.Match:
                            File.Delete(file.AbsolutePath);
                            if (File.Exists(file.AbsolutePath))
                            {
                                fileProcessed = false;
                            }
                            else
                            {
                                logger.LogDebug("[UserData] Deleted file: {Path}", file.AbsolutePath);
                                CleanupEmptyDirectories(Path.GetDirectoryName(file.AbsolutePath), userDataBasePath);
                            }

                            break;

                        case FileHashVerification.Mismatch when hasBackup:
                            var preservedPath = MoveModifiedFileAside(file.AbsolutePath);
                            logger.LogWarning(
                                "[UserData] File hash mismatch for {Path}; your modified copy was preserved at {PreservedPath} so the original could be restored",
                                file.AbsolutePath,
                                preservedPath);
                            break;

                        case FileHashVerification.Mismatch:
                            restoreNeeded = false;
                            logger.LogWarning("[UserData] File hash mismatch and no backup to restore, leaving in place: {Path}", file.AbsolutePath);
                            break;

                        default:
                            restoreNeeded = false;
                            fileProcessed = false;
                            logger.LogWarning(
                                "[UserData] Could not verify {Path} against its recorded hash, so it is left untouched along with any backup; the deployed file may still be pristine",
                                file.AbsolutePath);
                            break;
                    }
                }

                if (restoreNeeded)
                {
                    var targetDir = Path.GetDirectoryName(file.AbsolutePath);
                    if (!string.IsNullOrEmpty(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    // Delete-then-copy rather than File.Move: backups live under the application data
                    // tree while the deployed path is under Documents, which is routinely redirected
                    // to another drive or to OneDrive, and File.Move cannot cross a volume boundary.
                    if (file.BackupPath is not null)
                    {
                        RestoreBackupCopy(file.BackupPath, file.AbsolutePath);
                        backupRestored = true;
                        logger.LogInformation("[UserData] Restored backup: {Backup} -> {Path}", file.BackupPath, file.AbsolutePath);

                        DeleteConsumedBackup(file.BackupPath, logger);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                fileProcessed = false;
                logger.LogWarning(ex, "[UserData] Failed to uninstall file: {Path}", file.AbsolutePath);
            }

            if (!fileProcessed)
            {
                allCleanedUp = false;
            }

            if (hasBackup && !backupRestored)
            {
                allBackupsRestored = false;
                logger.LogWarning(
                    "[UserData] Backup for {Path} was not restored; the recorded backup is {BackupPath}",
                    file.AbsolutePath,
                    file.BackupPath);
            }
        }

        return allCleanedUp && allBackupsRestored;
    }

    private void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(_userDataTrackingPath);
        Directory.CreateDirectory(_manifestsPath);
        Directory.CreateDirectory(_backupsPath);
    }

    private async Task<string?> BackupExistingFileAsync(string filePath, GameType gameType, CancellationToken cancellationToken)
    {
        try
        {
            var fileName = Path.GetFileName(filePath);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
            var relativeDirPath = string.Empty;
            try
            {
                var rel = Path.GetRelativePath(GetUserDataBasePath(gameType), filePath);
                if (!rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel))
                {
                    relativeDirPath = Path.GetDirectoryName(rel) ?? string.Empty;
                }
            }
            catch
            {
                relativeDirPath = string.Empty;
            }

            var backupDir = Path.Combine(_backupsPath, gameType.ToString(), relativeDirPath);
            Directory.CreateDirectory(backupDir);

            var backupPath = Path.Combine(backupDir, $"{Path.GetFileNameWithoutExtension(fileName)}.{timestamp}_{uniqueSuffix}{Path.GetExtension(fileName)}{FileTypes.BackupExtension}");

            // Ensure backupPath never escapes _backupsPath
            var fullBackupPath = Path.GetFullPath(backupPath);
            var fullBackupsRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_backupsPath)) + Path.DirectorySeparatorChar;
            var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!fullBackupPath.StartsWith(fullBackupsRoot, pathComparison))
            {
                backupPath = Path.Combine(_backupsPath, gameType.ToString(), $"{Path.GetFileNameWithoutExtension(fileName)}.{timestamp}_{uniqueSuffix}{Path.GetExtension(fileName)}{FileTypes.BackupExtension}");
            }

            await Task.Run(() => File.Copy(filePath, backupPath, overwrite: false), cancellationToken);

            return backupPath;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[UserData] Failed to backup file: {Path}", filePath);
            return null;
        }
    }

    private string GetManifestFilePath(string installationKey)
    {
        return Path.Combine(_manifestsPath, $"{installationKey}{FileTypes.UserDataManifestExtension}");
    }

    private async Task SaveUserDataManifestAsync(UserDataManifest manifest, CancellationToken cancellationToken)
    {
        EnsureDirectoriesExist();
        var filePath = GetManifestFilePath(manifest.InstallationKey);
        var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(manifest, _jsonOptions);
            await File.WriteAllTextAsync(tempPath, json, cancellationToken);
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            FileOperationsService.DeleteFileIfExists(tempPath);
            throw;
        }
    }

    private async Task DeleteUserDataManifestAsync(string manifestId, string profileId, CancellationToken cancellationToken)
    {
        var filePath = GetManifestFilePath($"{manifestId}_{profileId}");
        if (File.Exists(filePath))
        {
            await Task.Run(() => File.Delete(filePath), cancellationToken);
        }
    }

    private async Task<UserDataManifest?> LoadUserDataManifestByKeyAsync(string installationKey, CancellationToken cancellationToken)
    {
        EnsureDirectoriesExist();
        var filePath = GetManifestFilePath(installationKey);
        return await LoadUserDataManifestFromFileAsync(filePath, cancellationToken);
    }

    private async Task<UserDataManifest?> LoadUserDataManifestFromFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            return JsonSerializer.Deserialize<UserDataManifest>(json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Cancellation must escape: a caller that reads a null manifest as "unreadable, its
            // backups can no longer be put back" would turn an abort into a retention decision.
            logger.LogWarning(ex, "[UserData] Failed to load manifest from {Path}", filePath);
            return null;
        }
    }

    private async Task<UserDataIndex> LoadIndexAsync(CancellationToken cancellationToken)
    {
        EnsureDirectoriesExist();

        await IndexLock.WaitAsync(cancellationToken);
        try
        {
            return await LoadIndexUnlockedAsync(cancellationToken);
        }
        finally
        {
            IndexLock.Release();
        }
    }

    /// <summary>
    /// Loads the index without acquiring the lock. Caller must hold IndexLock.
    /// </summary>
    private async Task<UserDataIndex> LoadIndexUnlockedAsync(CancellationToken cancellationToken)
    {
        EnsureDirectoriesExist();

        if (_cachedIndex != null)
        {
            return _cachedIndex;
        }

        if (!File.Exists(_indexPath))
        {
            _cachedIndex = new UserDataIndex();
            return _cachedIndex;
        }

        var json = await File.ReadAllTextAsync(_indexPath, cancellationToken);
        _cachedIndex = JsonSerializer.Deserialize<UserDataIndex>(json) ?? new UserDataIndex();
        return _cachedIndex;
    }

    private async Task SaveIndexAsync(UserDataIndex index, CancellationToken cancellationToken)
    {
        index.LastUpdatedAt = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(index, _jsonOptions);
        await File.WriteAllTextAsync(_indexPath, json, cancellationToken);
        _cachedIndex = index;
    }

    private async Task<OperationResult<string?>> CheckFileConflictUnlockedAsync(
        string absolutePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var index = await LoadIndexUnlockedAsync(cancellationToken);
            var normalizedPath = Path.GetFullPath(absolutePath);

            if (index.FileToInstallationMap.TryGetValue(normalizedPath, out var installationKey))
            {
                var manifest = await LoadUserDataManifestByKeyAsync(installationKey, cancellationToken);
                if (manifest != null && manifest.IsActive)
                {
                    return OperationResult<string?>.CreateSuccess(installationKey);
                }

                var manifestFilePath = GetManifestFilePath(installationKey);
                if (!File.Exists(manifestFilePath) || (manifest != null && !manifest.IsActive))
                {
                    // Installation is inactive or manifest no longer exists; clean up stale index mapping and persist
                    index.FileToInstallationMap.Remove(normalizedPath);
                    await SaveIndexAsync(index, cancellationToken);
                }
                else
                {
                    // Manifest file exists on disk but could not be read; retain conflict conservatively
                    return OperationResult<string?>.CreateSuccess(installationKey);
                }
            }

            return OperationResult<string?>.CreateSuccess(null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[UserData] Failed to check file conflict for {Path}", absolutePath);
            return OperationResult<string?>.CreateFailure($"Failed to check file conflict: {ex.Message}");
        }
    }

    private async Task UpdateIndexAsync(UserDataManifest manifest, bool isAdd, CancellationToken cancellationToken)
    {
        await IndexLock.WaitAsync(cancellationToken);
        try
        {
            await UpdateIndexUnlockedAsync(manifest, isAdd, cancellationToken);
        }
        finally
        {
            IndexLock.Release();
        }
    }

    private async Task UpdateIndexUnlockedAsync(UserDataManifest manifest, bool isAdd, CancellationToken cancellationToken)
    {
        var index = await LoadIndexUnlockedAsync(cancellationToken);
        var key = manifest.InstallationKey;

        if (isAdd)
        {
            if (!index.InstallationKeys.Contains(key))
            {
                index.InstallationKeys.Add(key);
            }

            // Update file mappings
            foreach (var path in manifest.InstalledFiles.Select(file => file.AbsolutePath))
            {
                index.FileToInstallationMap[path] = key;
            }

            // Update profile mappings
            if (!index.ProfileInstallations.TryGetValue(manifest.ProfileId, out var profileKeys))
            {
                profileKeys = [];
                index.ProfileInstallations[manifest.ProfileId] = profileKeys;
            }

            if (!profileKeys.Contains(key))
            {
                profileKeys.Add(key);
            }

            // Update manifest mappings
            if (!index.ManifestInstallations.TryGetValue(manifest.ManifestId, out var manifestKeys))
            {
                manifestKeys = [];
                index.ManifestInstallations[manifest.ManifestId] = manifestKeys;
            }

            if (!manifestKeys.Contains(key))
            {
                manifestKeys.Add(key);
            }
        }
        else
        {
            index.InstallationKeys.Remove(key);

            // Remove file mappings only if still mapped to this installation
            foreach (var path in manifest.InstalledFiles.Select(file => file.AbsolutePath))
            {
                if (index.FileToInstallationMap.TryGetValue(path, out var mappedKey) &&
                    mappedKey == key)
                {
                    index.FileToInstallationMap.Remove(path);
                }
            }

            // Remove from profile mappings
            if (index.ProfileInstallations.TryGetValue(manifest.ProfileId, out var profileKeys))
            {
                profileKeys.Remove(key);
                if (profileKeys.Count == 0)
                {
                    index.ProfileInstallations.Remove(manifest.ProfileId);
                }
            }

            // Remove from manifest mappings
            if (index.ManifestInstallations.TryGetValue(manifest.ManifestId, out var manifestKeys))
            {
                manifestKeys.Remove(key);
                if (manifestKeys.Count == 0)
                {
                    index.ManifestInstallations.Remove(manifest.ManifestId);
                }
            }
        }

        await SaveIndexAsync(index, cancellationToken);
    }
}
