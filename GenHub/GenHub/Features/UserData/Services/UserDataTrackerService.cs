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

    private readonly string _userDataTrackingPath = Path.Combine(configProvider.GetContentStoragePath(), "UserData");
    private readonly string _manifestsPath = Path.Combine(configProvider.GetContentStoragePath(), "UserData", "manifests");
    private readonly string _backupsPath = Path.Combine(configProvider.GetContentStoragePath(), "UserData", "backups");
    private readonly string _indexPath = Path.Combine(configProvider.GetContentStoragePath(), "UserData", "index.json");

    private UserDataIndex? _cachedIndex;

    /// <inheritdoc />
    public async Task<OperationResult<UserDataManifest>> InstallUserDataAsync(
        string manifestId,
        string profileId,
        GameType targetGame,
        IEnumerable<ManifestFile> files,
        string manifestVersion,
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

            if (!userDataFiles.Any())
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
                var wasOverwrite = false;
                string? backupPath = null;

                if (File.Exists(targetPath))
                {
                    // File exists - back it up if it's not from another installation
                    if (conflictResult.Success && string.IsNullOrEmpty(conflictResult.Data))
                    {
                        // User's own file - back it up
                        backupPath = await BackupExistingFileAsync(targetPath, targetGame, cancellationToken);
                        wasOverwrite = true;
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
                    WasOverwrite = wasOverwrite,
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

            foreach (var file in manifest.InstalledFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (File.Exists(file.AbsolutePath))
                    {
                        // Verify we should delete this file (hash matches or is our hard link)
                        if (file.IsHardLink || await VerifyFileHashAsync(file.AbsolutePath, file.SourceHash, cancellationToken))
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

            // Remove the manifest file
            await DeleteUserDataManifestAsync(manifestId, profileId, cancellationToken);

            // Update the index
            await UpdateIndexAsync(manifest, isAdd: false, cancellationToken);

            logger.LogInformation("[UserData] Successfully uninstalled user data for manifest {ManifestId}", manifestId);
            return OperationResult<bool>.CreateSuccess(true);
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
                return OperationResult<IReadOnlyList<UserDataManifest>>.CreateSuccess(Array.Empty<UserDataManifest>());
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
            var manifestFiles = Directory.GetFiles(_manifestsPath, "*.userdata.json", SearchOption.TopDirectoryOnly);

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
                    if (!await VerifyFileHashAsync(file.AbsolutePath, file.SourceHash, cancellationToken))
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
            ContentInstallTarget.UserMapsDirectory => Path.Combine(userDataBasePath, GameSettingsConstants.FolderNames.Maps, relativePath),
            ContentInstallTarget.UserReplaysDirectory => Path.Combine(userDataBasePath, GameSettingsConstants.FolderNames.Replays, relativePath),
            ContentInstallTarget.UserScreenshotsDirectory => Path.Combine(userDataBasePath, GameSettingsConstants.FolderNames.Screenshots, relativePath),
            _ => Path.Combine(userDataBasePath, relativePath),
        };
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

            var backupPath = Path.Combine(backupDir, $"{Path.GetFileNameWithoutExtension(fileName)}.{timestamp}{Path.GetExtension(fileName)}.bak");

            await Task.Run(() => File.Copy(filePath, backupPath, overwrite: true), cancellationToken);

            return backupPath;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[UserData] Failed to backup file: {Path}", filePath);
            return null;
        }
    }

    private async Task<bool> VerifyFileHashAsync(string filePath, string expectedHash, CancellationToken cancellationToken)
    {
        return await fileOperations.VerifyFileHashAsync(filePath, expectedHash, cancellationToken);
    }

    private string GetManifestFilePath(string installationKey)
    {
        return Path.Combine(_manifestsPath, $"{installationKey}.userdata.json");
    }

    private async Task SaveUserDataManifestAsync(UserDataManifest manifest, CancellationToken cancellationToken)
    {
        EnsureDirectoriesExist();
        var filePath = GetManifestFilePath(manifest.InstallationKey);
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
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
        var json = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true });
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
