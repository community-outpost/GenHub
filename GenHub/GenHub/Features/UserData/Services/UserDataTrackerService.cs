using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
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
/// Uses hard links to CAS content when possible for efficient disk usage.
/// </summary>
public class UserDataTrackerService(
    IConfigurationProviderService configProvider,
    IFileOperationsService fileOperations,
    ILogger<UserDataTrackerService> logger) : IUserDataTracker
{
    private static readonly SemaphoreSlim IndexLock = new(1, 1);
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly string _userDataTrackingPath = Path.Combine(configProvider.GetApplicationDataPath(), "UserData");
    private readonly string _manifestsPath = Path.Combine(configProvider.GetApplicationDataPath(), "UserData", "manifests");
    private readonly string _backupsPath = Path.Combine(configProvider.GetApplicationDataPath(), "UserData", "backups");
    private readonly string _indexPath = Path.Combine(configProvider.GetApplicationDataPath(), "UserData", "index.json");

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
            long totalSize = 0;

            foreach (var file in userDataFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var targetPath = ResolveUserDataTargetPath(file.InstallTarget, file.RelativePath, userDataBasePath);

                logger.LogDebug("[UserData] Installing {RelativePath} to {TargetPath}", file.RelativePath, targetPath);

                // Check for conflicts
                var conflictResult = await CheckFileConflictAsync(targetPath, cancellationToken);
                var wasOverwritten = false;
                string? backupPath = null;

                if (File.Exists(targetPath))
                {
                    // File exists - back it up if it's not from another installation
                    if (conflictResult.Success && string.IsNullOrEmpty(conflictResult.Data))
                    {
                        // User's own file - back it up
                        backupPath = await BackupExistingFileAsync(targetPath, targetGame, cancellationToken);
                        wasOverwritten = true;
                        logger.LogInformation("[UserData] Backed up existing user file: {Path} -> {Backup}", targetPath, backupPath);
                    }
                    else if (conflictResult.Data != userDataManifest.InstallationKey)
                    {
                        // Another installation owns this file - skip or handle conflict
                        logger.LogWarning("[UserData] File conflict with installation {Key}: {Path}", conflictResult.Data, targetPath);
                    }

                    // Delete existing to replace
                    FileOperationsService.DeleteFileIfExists(targetPath);
                }

                // Ensure target directory exists
                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                // Try to create hard link from CAS if possible
                var isHardLink = false;
                if (!string.IsNullOrEmpty(file.Hash))
                {
                    var linkResult = await fileOperations.LinkFromCasAsync(
                        file.Hash,
                        targetPath,
                        useHardLink: true,
                        cancellationToken);

                    if (linkResult)
                    {
                        isHardLink = true;
                        logger.LogDebug("[UserData] Created hard link for {Path}", targetPath);
                    }
                    else
                    {
                        // Fall back to copy
                        var copyResult = await fileOperations.CopyFromCasAsync(file.Hash, targetPath, cancellationToken);
                        if (!copyResult)
                        {
                            logger.LogError("[UserData] Failed to install file {Path}", targetPath);
                            continue;
                        }

                        logger.LogDebug("[UserData] Copied file for {Path} (hard link failed)", targetPath);
                    }
                }
                else
                {
                    logger.LogWarning("[UserData] File {Path} has no hash, skipping", file.RelativePath);
                    continue;
                }

                var entry = new UserDataFileEntry
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
                };

                userDataManifest.InstalledFiles.Add(entry);
                totalSize += file.Size;
            }

            userDataManifest.TotalSizeBytes = totalSize;

            // Save the manifest
            await SaveUserDataManifestAsync(userDataManifest, cancellationToken);

            // Update the index
            await UpdateIndexAsync(userDataManifest, isAdd: true, cancellationToken);

            logger.LogInformation(
                "[UserData] Successfully installed {Count} files ({Size} bytes) for manifest {ManifestId}",
                userDataManifest.InstalledFiles.Count,
                totalSize,
                manifestId);

            return OperationResult<UserDataManifest>.CreateSuccess(userDataManifest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[UserData] Failed to install user data for manifest {ManifestId}", manifestId);
            return OperationResult<UserDataManifest>.CreateFailure($"Failed to install user data: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> UninstallUserDataAsync(
        string manifestId,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[UserData] Uninstalling user data for manifest {ManifestId}, profile {ProfileId}", manifestId, profileId);

        try
        {
            var manifestResult = await GetUserDataManifestAsync(manifestId, profileId, cancellationToken);
            if (!manifestResult.Success || manifestResult.Data == null)
            {
                logger.LogWarning("[UserData] No user data manifest found for {ManifestId}/{ProfileId}", manifestId, profileId);
                return OperationResult<bool>.CreateSuccess(true); // Nothing to uninstall
            }

            var manifest = manifestResult.Data;

            await CleanupInstalledFilesAsync(manifest, cancellationToken);

            // Remove the manifest file
            await DeleteUserDataManifestAsync(manifestId, profileId, cancellationToken);

            // Update the index
            await UpdateIndexAsync(manifest, isAdd: false, cancellationToken);

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
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> ActivateProfileUserDataAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[UserData] Activating user data for profile {ProfileId}", profileId);

        try
        {
            var manifestsResult = await GetProfileUserDataAsync(profileId, cancellationToken);
            if (!manifestsResult.Success || manifestsResult.Data == null)
            {
                return OperationResult<bool>.CreateSuccess(true); // No user data to activate
            }

            foreach (var manifest in manifestsResult.Data)
            {
                if (manifest.IsActive)
                {
                    continue; // Already active
                }

                // Re-create hard links for all files
                foreach (var file in manifest.InstalledFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (File.Exists(file.AbsolutePath))
                    {
                        continue; // File already exists
                    }

                    if (!string.IsNullOrEmpty(file.CasHash))
                    {
                        var targetDir = Path.GetDirectoryName(file.AbsolutePath);
                        if (!string.IsNullOrEmpty(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }

                        var linkResult = await fileOperations.LinkFromCasAsync(
                            file.CasHash,
                            file.AbsolutePath,
                            useHardLink: true,
                            cancellationToken);

                        if (!linkResult)
                        {
                            // Fall back to copy
                            await fileOperations.CopyFromCasAsync(file.CasHash, file.AbsolutePath, cancellationToken);
                        }
                    }
                }

                // Update manifest state
                manifest.IsActive = true;
                await SaveUserDataManifestAsync(manifest, cancellationToken);
            }

            logger.LogInformation("[UserData] Activated {Count} manifests for profile {ProfileId}", manifestsResult.Data.Count, profileId);
            return OperationResult<bool>.CreateSuccess(true);
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
        logger.LogInformation("[UserData] Deactivating user data for profile {ProfileId}", profileId);

        try
        {
            var manifestsResult = await GetProfileUserDataAsync(profileId, cancellationToken);
            if (!manifestsResult.Success || manifestsResult.Data == null)
            {
                return OperationResult<bool>.CreateSuccess(true); // No user data to deactivate
            }

            foreach (var manifest in manifestsResult.Data)
            {
                if (!manifest.IsActive)
                {
                    continue; // Already inactive
                }

                // Remove hard links but keep tracking
                foreach (var file in manifest.InstalledFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (file.IsHardLink && File.Exists(file.AbsolutePath))
                    {
                        try
                        {
                            File.Delete(file.AbsolutePath);
                            CleanupEmptyDirectories(Path.GetDirectoryName(file.AbsolutePath));
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "[UserData] Failed to remove hard link: {Path}", file.AbsolutePath);
                        }
                    }
                }

                // Update manifest state
                manifest.IsActive = false;
                await SaveUserDataManifestAsync(manifest, cancellationToken);
            }

            logger.LogInformation("[UserData] Deactivated {Count} manifests for profile {ProfileId}", manifestsResult.Data.Count, profileId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[UserData] Failed to deactivate user data for profile {ProfileId}", profileId);
            return OperationResult<bool>.CreateFailure($"Failed to deactivate user data: {ex.Message}");
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
                if (manifest != null && manifest.TargetGame == targetGame)
                {
                    manifests.Add(manifest);
                }
            }

            return OperationResult<IReadOnlyList<UserDataManifest>>.CreateSuccess(manifests);
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

                if (!file.IsHardLink)
                {
                    // Verify hash for copied files
                    if (!await fileOperations.VerifyFileHashAsync(file.AbsolutePath, file.SourceHash, cancellationToken))
                    {
                        logger.LogWarning("[UserData] File hash mismatch: {Path}", file.AbsolutePath);
                        allValid = false;
                    }
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
        try
        {
            var index = await LoadIndexAsync(cancellationToken);
            var normalizedPath = Path.GetFullPath(absolutePath);

            if (index.FileToInstallationMap.TryGetValue(normalizedPath, out var installationKey))
            {
                return OperationResult<string?>.CreateSuccess(installationKey);
            }

            return OperationResult<string?>.CreateSuccess(null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[UserData] Failed to check file conflict for {Path}", absolutePath);
            return OperationResult<string?>.CreateFailure($"Failed to check file conflict: {ex.Message}");
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

            foreach (var manifest in manifestsResult.Data)
            {
                await UninstallUserDataAsync(manifest.ManifestId, profileId, cancellationToken);
            }

            logger.LogInformation("[UserData] Cleaned up {Count} manifests for profile {ProfileId}", manifestsResult.Data.Count, profileId);
            return OperationResult<bool>.CreateSuccess(true);
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
                                // Instead, we directly clean up the files. We don't need to update the index or delete the manifest file
                                // because we are about to delete the entire UserData directory.
                                var manifest = await LoadUserDataManifestByKeyAsync(key, cancellationToken);
                                if (manifest != null)
                                {
                                    await CleanupInstalledFilesAsync(manifest, cancellationToken);
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "[UserData] Failed to cleanup user data for installation key {Key}", key);
                            }
                        }
                    }
                }

                // 2. Clear the in-memory index
                _cachedIndex = new UserDataIndex();

                // 3. Nuke the directories to be sure
                if (Directory.Exists(_userDataTrackingPath))
                {
                    // Sanity check: ensure we're not deleting a system root or unrelated directory
                    if (!Path.GetFullPath(_userDataTrackingPath).Contains(AppConstants.AppName, StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogError("[UserData] Refusing to delete UserData directory that doesn't appear application-specific: {Path}", _userDataTrackingPath);
                        return OperationResult<bool>.CreateFailure("UserData tracking path does not appear to be application-specific");
                    }

                    logger.LogInformation("[UserData] Deleting UserData directory: {Path}", _userDataTrackingPath);
                    Directory.Delete(_userDataTrackingPath, true);
                }

                // 4. Re-create empty directories
                EnsureDirectoriesExist();

                return OperationResult<bool>.CreateSuccess(true);
            }
            finally
            {
                IndexLock.Release();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[UserData] Failed to delete all user data");
            return OperationResult<bool>.CreateFailure($"Failed to delete all user data: {ex.Message}");
        }
    }

    private static string GetUserDataBasePath(GameType gameType)
    {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        return gameType switch
        {
            GameType.Generals => Path.Combine(documentsPath, GameSettingsConstants.FolderNames.Generals),
            GameType.ZeroHour => Path.Combine(documentsPath, GameSettingsConstants.FolderNames.ZeroHour),
            _ => Path.Combine(documentsPath, GameSettingsConstants.FolderNames.ZeroHour),
        };
    }

    private static string ResolveUserDataTargetPath(ContentInstallTarget installTarget, string relativePath, string userDataBasePath)
    {
        return installTarget switch
        {
            ContentInstallTarget.UserDataDirectory => Path.Combine(userDataBasePath, relativePath),
            ContentInstallTarget.UserMapsDirectory => Path.Combine(userDataBasePath, GameSettingsConstants.FolderNames.Maps, StripLeadingDirectory(relativePath, "Maps")),
            ContentInstallTarget.UserReplaysDirectory => Path.Combine(userDataBasePath, GameSettingsConstants.FolderNames.Replays, StripLeadingDirectory(relativePath, "Replays")),
            ContentInstallTarget.UserScreenshotsDirectory => Path.Combine(userDataBasePath, GameSettingsConstants.FolderNames.Screenshots, StripLeadingDirectory(relativePath, "Screenshots")),
            _ => Path.Combine(userDataBasePath, relativePath),
        };
    }

    /// <summary>
    /// Strips a leading directory name from a path if present.
    /// Handles both forward and back slashes.
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

    private static void CleanupEmptyDirectories(string? directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath))
        {
            return;
        }

        try
        {
            while (Directory.Exists(directoryPath) &&
                   !Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
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

    private async Task CleanupInstalledFilesAsync(UserDataManifest manifest, CancellationToken cancellationToken)
    {
        foreach (var file in manifest.InstalledFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (File.Exists(file.AbsolutePath))
                {
                    // Verify we should delete this file (hash matches or is our hard link)
                    if (file.IsHardLink || await fileOperations.VerifyFileHashAsync(file.AbsolutePath, file.SourceHash, cancellationToken))
                    {
                        File.Delete(file.AbsolutePath);
                        logger.LogDebug("[UserData] Deleted file: {Path}", file.AbsolutePath);

                        // Clean up empty directories
                        CleanupEmptyDirectories(Path.GetDirectoryName(file.AbsolutePath));
                    }
                    else
                    {
                        logger.LogWarning("[UserData] File hash mismatch, user may have modified: {Path}", file.AbsolutePath);
                    }
                }

                // Restore backup if exists
                if (!string.IsNullOrEmpty(file.BackupPath) && File.Exists(file.BackupPath))
                {
                    var targetDir = Path.GetDirectoryName(file.AbsolutePath);
                    if (!string.IsNullOrEmpty(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    File.Move(file.BackupPath, file.AbsolutePath);
                    logger.LogInformation("[UserData] Restored backup: {Backup} -> {Path}", file.BackupPath, file.AbsolutePath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[UserData] Failed to uninstall file: {Path}", file.AbsolutePath);
            }
        }
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
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var relativeDirPath = Path.GetDirectoryName(Path.GetRelativePath(GetUserDataBasePath(gameType), filePath)) ?? string.Empty;
            var backupDir = Path.Combine(_backupsPath, gameType.ToString(), relativeDirPath);
            Directory.CreateDirectory(backupDir);

            var backupPath = Path.Combine(backupDir, $"{Path.GetFileNameWithoutExtension(fileName)}.{timestamp}{Path.GetExtension(fileName)}{FileTypes.BackupExtension}");

            await Task.Run(() => File.Copy(filePath, backupPath, overwrite: true), cancellationToken);

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
        var json = JsonSerializer.Serialize(manifest, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
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
        catch (Exception ex)
        {
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

    private async Task UpdateIndexAsync(UserDataManifest manifest, bool isAdd, CancellationToken cancellationToken)
    {
        await IndexLock.WaitAsync(cancellationToken);
        try
        {
            // Use LoadIndexUnlockedAsync to avoid deadlock (we already hold IndexLock)
            var index = await LoadIndexUnlockedAsync(cancellationToken);
            var key = manifest.InstallationKey;

            if (isAdd)
            {
                if (!index.InstallationKeys.Contains(key))
                {
                    index.InstallationKeys.Add(key);
                }

                // Update file mappings
                foreach (var file in manifest.InstalledFiles)
                {
                    index.FileToInstallationMap[file.AbsolutePath] = key;
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

                // Remove file mappings
                foreach (var file in manifest.InstalledFiles)
                {
                    index.FileToInstallationMap.Remove(file.AbsolutePath);
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
        finally
        {
            IndexLock.Release();
        }
    }
}
